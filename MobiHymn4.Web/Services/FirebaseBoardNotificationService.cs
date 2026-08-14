using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MobiHymn4.Shared;
using MobiHymn4.Shared.Models;
using MobiHymn4.Shared.Services;

namespace MobiHymn4.Web.Services;

/// <summary>
/// Web port of MAUI BoardNotificationService:
/// FCM token → users/{uid}/fcmTokens, unread listener → badges, mark read, foreground tray.
/// </summary>
public sealed class FirebaseBoardNotificationService : IBoardNotificationService, IAsyncDisposable
{
    /// <summary>Roles unmuted for setlist push by default (server onBoardWrite matches).</summary>
    static readonly HashSet<string> DefaultNotifyRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "projector", "accompaniment"
    };

    readonly FirebaseJs firebase;
    readonly IAuthService auth;
    readonly IProfileService profiles;
    readonly IGroupService groups;
    readonly IAppPreferences prefs;
    readonly BoardUiState boardUi;
    readonly NavigationManager nav;

    string? notificationsSubId;
    string? lastRegisteredToken;
    string? lastRegisteredUid;
    public string? LastRegisterError { get; private set; }
    DotNetObjectReference<FirebaseBoardNotificationService>? selfRef;
    readonly HashSet<string> knownUnreadIds = new(StringComparer.Ordinal);
    bool unreadSnapshotSeeded;
    bool started;
    bool clickWired;

    public FirebaseBoardNotificationService(
        FirebaseJs firebase,
        IAuthService auth,
        IProfileService profiles,
        IGroupService groups,
        IAppPreferences prefs,
        BoardUiState boardUi,
        NavigationManager nav)
    {
        this.firebase = firebase;
        this.auth = auth;
        this.profiles = profiles;
        this.groups = groups;
        this.prefs = prefs;
        this.boardUi = boardUi;
        this.nav = nav;
        auth.AuthStateChanged += (_, _) => _ = OnAuthChangedAsync();
        profiles.ProfileChanged += (_, _) =>
        {
            if (!auth.IsSignedIn || string.IsNullOrWhiteSpace(lastRegisteredToken))
                _ = RegisterTokenAsync();
        };
    }

    public async Task StartAsync()
    {
        if (started)
        {
            Console.WriteLine("[BoardOpen] FirebaseBoardNotificationService.StartAsync skipped — already started");
            return;
        }
        started = true;
        Console.WriteLine("[BoardOpen] FirebaseBoardNotificationService.StartAsync begin");
        await WireClickHandlerAsync();
        await OnAuthChangedAsync();
    }

    public async Task StopAsync()
    {
        await TearDownSubscriptionAsync();
        started = false;
        lastRegisteredToken = null;
        lastRegisteredUid = null;
        knownUnreadIds.Clear();
        unreadSnapshotSeeded = false;
        boardUi.SetUnreadLists(null);
    }

    public async Task<bool> RegisterTokenAsync(bool force = false)
    {
        if (!auth.IsSignedIn)
        {
            LastRegisterError = "Not signed in.";
            return false;
        }

        LastRegisterError = null;
        if (force)
        {
            lastRegisteredToken = null;
            lastRegisteredUid = null;
        }

        // Keep this short — getFcmToken has its own JS timeouts. Long retry loops
        // made Account "Registering…" look stuck on Android TWA.
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                await firebase.EnsureReadyAsync();
                var tokenTask = firebase.GetFcmTokenAsync();
                var finished = await Task.WhenAny(tokenTask, Task.Delay(25000));
                if (finished != tokenTask)
                {
                    LastRegisterError = "Push token request timed out. Fully close the app and try Register again.";
                    Console.WriteLine($"[BoardOpen] RegisterTokenAsync attempt {attempt}: getFcmToken timed out");
                    continue;
                }

                var token = await tokenTask;
                if (string.IsNullOrWhiteSpace(token))
                {
                    LastRegisterError = string.IsNullOrWhiteSpace(firebase.LastFcmTokenError)
                        ? "Could not get a push token from Firebase."
                        : firebase.LastFcmTokenError;
                    Console.WriteLine($"[BoardOpen] RegisterTokenAsync attempt {attempt}: no token — {LastRegisterError}");
                    if (attempt < 2) await Task.Delay(800);
                    continue;
                }

                var uid = auth.CurrentUserId;
                if (!force
                    && string.Equals(lastRegisteredToken, token, StringComparison.Ordinal)
                    && string.Equals(lastRegisteredUid, uid, StringComparison.Ordinal))
                {
                    Console.WriteLine("[BoardOpen] RegisterTokenAsync: token unchanged");
                    LastRegisterError = null;
                    return true;
                }

                var deviceId = GetOrCreateDeviceId();
                await firebase.SetDocAsync(
                    $"{FirestorePaths.Users}/{uid}/{FirestorePaths.FcmTokens}/{deviceId}",
                    new
                    {
                        token,
                        platform = "Web",
                        updatedAt = DateTimeOffset.UtcNow.ToString("o")
                    });

                lastRegisteredToken = token;
                lastRegisteredUid = uid;
                LastRegisterError = null;
                Console.WriteLine($"[BoardOpen] RegisterTokenAsync: saved Web token for {uid} device={deviceId}");
                return true;
            }
            catch (Exception ex)
            {
                lastRegisteredToken = null;
                lastRegisteredUid = null;
                LastRegisterError = ex.Message;
                Console.WriteLine($"[BoardOpen] RegisterTokenAsync attempt {attempt} failed: {ex.Message}");
                if (attempt < 2) await Task.Delay(800);
            }
        }
        return false;
    }

    public async Task MarkAllReadAsync()
    {
        if (!auth.IsSignedIn) return;
        try
        {
            var unread = await QueryUnreadAsync();
            if (unread.Count == 0) return;
            var uid = auth.CurrentUserId;
            await Task.WhenAll(unread.Select(doc =>
                firebase.SetDocAsync(
                    $"{FirestorePaths.Users}/{uid}/{FirestorePaths.Notifications}/{doc.Id}",
                    new { read = true })));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"MarkAllReadAsync failed: {ex.Message}");
        }
    }

    public async Task MarkListReadAsync(string groupId, string listId)
    {
        if (!auth.IsSignedIn || string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(listId))
            return;

        groupId = groupId.Trim();
        listId = listId.Trim();
        // Clear badge immediately — do not wait for the Firestore unread snapshot.
        boardUi.ClearListUnread(groupId, listId);
        try
        {
            var matching = (await QueryUnreadAsync())
                .Where(d =>
                    string.Equals(d.GroupId, groupId, StringComparison.Ordinal)
                    && string.Equals(d.ListId, listId, StringComparison.Ordinal))
                .ToList();
            if (matching.Count == 0) return;

            var uid = auth.CurrentUserId;
            await Task.WhenAll(matching.Select(doc =>
                firebase.SetDocAsync(
                    $"{FirestorePaths.Users}/{uid}/{FirestorePaths.Notifications}/{doc.Id}",
                    new { read = true })));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"MarkListReadAsync failed: {ex.Message}");
        }
    }

    async Task MarkNotificationReadQuietAsync(string notifId)
    {
        if (!auth.IsSignedIn || string.IsNullOrWhiteSpace(notifId))
            return;
        try
        {
            await firebase.SetDocAsync(
                $"{FirestorePaths.Users}/{auth.CurrentUserId}/{FirestorePaths.Notifications}/{notifId.Trim()}",
                new { read = true });
        }
        catch
        {
            // best-effort cleanup of self-action leftovers
        }
    }

    [JSInvokable]
    public void OnQuerySnapshot(JsonElement[]? rows)
    {
        var docs = (rows ?? Array.Empty<JsonElement>())
            .Select(MapNotification)
            .Where(d => d != null && !d.Read && !string.IsNullOrWhiteSpace(d.Id))
            .Cast<BoardNotificationDoc>()
            .ToList();
        ApplyUnreadSnapshot(docs);
    }

    [JSInvokable]
    public void OnBoardNotificationClick(string groupId, string listId)
    {
        Console.WriteLine($"[BoardOpen] .NET OnBoardNotificationClick invoked from JS: groupId={groupId} listId={listId}");
        var gid = (groupId ?? "").Trim();
        var lid = (listId ?? "").Trim();
        if (string.IsNullOrEmpty(gid))
        {
            Console.WriteLine("[BoardOpen] OnBoardNotificationClick bailed: empty groupId");
            return;
        }

        // Open the sliding pane immediately. Do not NavigateTo here — iOS PWA
        // navigation from notification taps is unreliable, and URL sync is handled
        // by BoardPane after the board mounts (history.replaceState).
        boardUi.Open(gid, string.IsNullOrEmpty(lid) ? null : lid);
    }

    async Task OnAuthChangedAsync()
    {
        await TearDownSubscriptionAsync();
        knownUnreadIds.Clear();
        unreadSnapshotSeeded = false;

        if (!auth.IsSignedIn)
        {
            lastRegisteredToken = null;
            lastRegisteredUid = null;
            boardUi.SetUnreadLists(null);
            return;
        }

        await RegisterTokenAsync(force: true);
        await HydrateGroupMutePreferencesAsync();
        await SubscribeUnreadAsync();
    }

    [JSInvokable]
    public void OnPushResume()
    {
        if (!auth.IsSignedIn) return;
        Console.WriteLine("[BoardOpen] OnPushResume — refreshing Web FCM token");
        _ = RegisterTokenAsync(force: true);
    }

    async Task HydrateGroupMutePreferencesAsync()
    {
        var groupIds = profiles.CurrentProfile?.GroupIds;
        if (groupIds == null || groupIds.Count == 0) return;

        foreach (var groupId in groupIds.Distinct(StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(groupId)) continue;
            try
            {
                prefs.Remove(PrefKeys.GroupMutePrefix + groupId.Trim());
                await groups.IsGroupNotificationsMutedAsync(groupId);
            }
            catch
            {
                // optional
            }
        }
    }

    async Task SubscribeUnreadAsync()
    {
        try
        {
            await firebase.EnsureReadyAsync();
            selfRef ??= DotNetObjectReference.Create(this);
            var path = $"{FirestorePaths.Users}/{auth.CurrentUserId}/{FirestorePaths.Notifications}";
            notificationsSubId = await firebase.SubscribeQueryAsync(
                path, "read", "==", false, 50, selfRef);

            if (string.IsNullOrEmpty(notificationsSubId))
            {
                // Fallback: listen to whole collection, filter client-side.
                notificationsSubId = await firebase.SubscribeQueryAsync(
                    path, null, null, null, 50, selfRef);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SubscribeUnread failed: {ex.Message}");
        }
    }

    async Task TearDownSubscriptionAsync()
    {
        if (!string.IsNullOrEmpty(notificationsSubId))
        {
            await firebase.UnsubscribeAsync(notificationsSubId);
            notificationsSubId = null;
        }
    }

    void ApplyUnreadSnapshot(IReadOnlyList<BoardNotificationDoc> docs)
    {
        var byList = new Dictionary<string, BoardListUnreadInfo>(StringComparer.Ordinal);
        var newlyArrived = new List<BoardNotificationDoc>();
        var currentIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var doc in docs)
        {
            if (string.IsNullOrWhiteSpace(doc.Id)) continue;
            currentIds.Add(doc.Id);

            // Never toast/badge yourself for your own board edits (other devices of same account).
            var isOwnAction = !string.IsNullOrWhiteSpace(doc.UpdatedBy)
                && string.Equals(doc.UpdatedBy, auth.CurrentUserId, StringComparison.Ordinal);
            if (isOwnAction)
            {
                if (unreadSnapshotSeeded && !knownUnreadIds.Contains(doc.Id))
                    _ = MarkNotificationReadQuietAsync(doc.Id);
                continue;
            }

            if (unreadSnapshotSeeded && !knownUnreadIds.Contains(doc.Id))
                newlyArrived.Add(doc);

            var groupId = doc.GroupId?.Trim();
            var listId = doc.ListId?.Trim();
            if (string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(listId))
                continue;

            var key = BoardUiState.ListKey(groupId, listId);
            var created = doc.CreatedAt == default ? DateTime.UtcNow : doc.CreatedAt.UtcDateTime;
            var hymnCount = doc.NewHymnCount > 0 ? doc.NewHymnCount : 1;

            if (byList.TryGetValue(key, out var existing))
            {
                byList[key] = new BoardListUnreadInfo
                {
                    NewHymnCount = existing.NewHymnCount + hymnCount,
                    OldestCreatedAtUtc = created < existing.OldestCreatedAtUtc
                        ? created
                        : existing.OldestCreatedAtUtc
                };
            }
            else
            {
                byList[key] = new BoardListUnreadInfo
                {
                    NewHymnCount = hymnCount,
                    OldestCreatedAtUtc = created
                };
            }
        }

        knownUnreadIds.Clear();
        foreach (var id in currentIds)
            knownUnreadIds.Add(id);
        unreadSnapshotSeeded = true;

        // If the user is already viewing a setlist, keep its badge off and mark those docs read.
        var viewing = boardUi.ViewingListKey;
        if (!string.IsNullOrEmpty(viewing) && byList.ContainsKey(viewing))
        {
            byList.Remove(viewing);
            var parts = viewing.Split('|', 2);
            if (parts.Length == 2)
                _ = MarkListReadAsync(parts[0], parts[1]);
        }

        boardUi.SetUnreadLists(byList);
        _ = NotifyNewlyArrivedAsync(newlyArrived);
    }

    async Task NotifyNewlyArrivedAsync(IReadOnlyList<BoardNotificationDoc> newlyArrived)
    {
        if (newlyArrived.Count == 0) return;
        // Background tray is FCM. Only post when the tab is visible (mirrors Android foreground).
        try
        {
            foreach (var doc in newlyArrived)
            {
                if (doc.SuppressPush || ShouldSuppressPush(doc.GroupId))
                    continue;
                if (!string.IsNullOrWhiteSpace(doc.UpdatedBy)
                    && string.Equals(doc.UpdatedBy, auth.CurrentUserId, StringComparison.Ordinal))
                    continue;

                var title = string.IsNullOrWhiteSpace(doc.Title)
                    ? "Board updated"
                    : doc.Title;
                var body = string.IsNullOrWhiteSpace(doc.Body)
                    ? "Your group board was updated."
                    : doc.Body;
                var data = new Dictionary<string, string>(StringComparer.Ordinal);
                if (!string.IsNullOrWhiteSpace(doc.GroupId)) data["groupId"] = doc.GroupId;
                if (!string.IsNullOrWhiteSpace(doc.ListId))
                {
                    data["listId"] = doc.ListId;
                    data["date"] = doc.ListId;
                }
                if (!string.IsNullOrWhiteSpace(doc.GroupId) && !string.IsNullOrWhiteSpace(doc.ListId))
                    data["boardPath"] = BoardDeepLinkService.BoardPath(doc.GroupId, doc.ListId);
                if (!string.IsNullOrWhiteSpace(doc.GroupName)) data["groupName"] = doc.GroupName;
                if (!string.IsNullOrWhiteSpace(doc.UpdatedBy)) data["updatedBy"] = doc.UpdatedBy;

                await firebase.ShowLocalNotificationAsync(title, body, data);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"NotifyNewlyArrived failed: {ex.Message}");
        }
    }

    bool ShouldSuppressPush(string? groupId)
    {
        var profile = profiles.CurrentProfile;
        if (profile != null)
        {
            var globallyMuted = profile.NotificationsMuted
                || (!profile.NotificationsPreferenceSet
                    && DefaultNotificationsMuted(profile.Roles));
            if (globallyMuted) return true;
        }

        var normalized = groupId?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return false;
        return prefs.GetBool(PrefKeys.GroupMutePrefix + normalized, false);
    }

    static bool DefaultNotificationsMuted(IEnumerable<string>? roles)
    {
        if (roles == null) return true;
        return !roles.Any(r => DefaultNotifyRoles.Contains(r));
    }

    async Task<List<BoardNotificationDoc>> QueryUnreadAsync()
    {
        await firebase.EnsureReadyAsync();
        var path = $"{FirestorePaths.Users}/{auth.CurrentUserId}/{FirestorePaths.Notifications}";
        JsonElement[] rows;
        try
        {
            rows = await firebase.QueryCollectionAsync(path, "read", "==", false);
        }
        catch
        {
            rows = await firebase.QueryCollectionAsync(path);
        }

        return rows
            .Select(MapNotification)
            .Where(d => d != null && !d.Read)
            .Cast<BoardNotificationDoc>()
            .Take(50)
            .ToList();
    }

    static BoardNotificationDoc? MapNotification(JsonElement d)
    {
        if (d.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        var id = FirebaseProfileService.GetString(d, "id")
            ?? FirebaseProfileService.GetString(d, "_id")
            ?? "";
        if (string.IsNullOrWhiteSpace(id)) return null;

        var created = DateTimeOffset.UtcNow;
        if (d.TryGetProperty("createdAt", out var c) && c.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(c.GetString(), out var parsed))
            created = parsed;

        return new BoardNotificationDoc
        {
            Id = id,
            Type = FirebaseProfileService.GetString(d, "type") ?? "",
            GroupId = FirebaseProfileService.GetString(d, "groupId") ?? "",
            ListId = FirebaseProfileService.GetString(d, "listId") ?? "",
            GroupName = FirebaseProfileService.GetString(d, "groupName") ?? "",
            UpdatedBy = FirebaseProfileService.GetString(d, "updatedBy"),
            UpdatedByName = FirebaseProfileService.GetString(d, "updatedByName"),
            ChangeAction = FirebaseProfileService.GetString(d, "changeAction") ?? "",
            NewHymnCount = d.TryGetProperty("newHymnCount", out var n) && n.TryGetInt32(out var count) ? count : 1,
            Title = FirebaseProfileService.GetString(d, "title") ?? "",
            Body = FirebaseProfileService.GetString(d, "body") ?? "",
            Read = d.TryGetProperty("read", out var r) && r.ValueKind == JsonValueKind.True,
            SuppressPush = d.TryGetProperty("suppressPush", out var s) && s.ValueKind == JsonValueKind.True,
            CreatedAt = created
        };
    }

    string GetOrCreateDeviceId()
    {
        var id = prefs.Get(PrefKeys.FcmDeviceId, "");
        if (!string.IsNullOrWhiteSpace(id)) return id;
        id = Guid.NewGuid().ToString("N");
        prefs.Set(PrefKeys.FcmDeviceId, id);
        return id;
    }

    async Task WireClickHandlerAsync()
    {
        // Retry with backoff instead of giving up forever — a transient JS interop
        // failure here (e.g. a slow cold start racing Firebase init) would otherwise
        // permanently break "tap notification → open board" for the whole session.
        var delayMs = 500;
        for (var attempt = 0; attempt < 6 && !clickWired; attempt++)
        {
            try
            {
                await firebase.EnsureReadyAsync();
                selfRef ??= DotNetObjectReference.Create(this);
                await firebase.OnBoardNotificationClickAsync(selfRef);
                clickWired = true;
                Console.WriteLine($"[BoardOpen] WireClickHandlerAsync succeeded on attempt {attempt + 1}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BoardOpen] WireClickHandler failed (attempt {attempt + 1}): {ex.Message}");
                await Task.Delay(delayMs);
                delayMs = Math.Min(delayMs * 2, 8000);
            }
        }
        if (!clickWired)
            Console.WriteLine("[BoardOpen] WireClickHandlerAsync gave up after 6 attempts — notification taps will NOT open the board this session");
    }

    public async ValueTask DisposeAsync()
    {
        await TearDownSubscriptionAsync();
        selfRef?.Dispose();
        selfRef = null;
    }
}
