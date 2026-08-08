using System.Text.Json;
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
    static readonly HashSet<string> LeadershipRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "pastor", "worshipLeader", "projector", "accompaniment", "preacher"
    };

    readonly FirebaseJs firebase;
    readonly IAuthService auth;
    readonly IProfileService profiles;
    readonly IGroupService groups;
    readonly IAppPreferences prefs;
    readonly BoardUiState boardUi;

    string? notificationsSubId;
    string? lastRegisteredToken;
    string? lastRegisteredUid;
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
        BoardUiState boardUi)
    {
        this.firebase = firebase;
        this.auth = auth;
        this.profiles = profiles;
        this.groups = groups;
        this.prefs = prefs;
        this.boardUi = boardUi;
        auth.AuthStateChanged += (_, _) => _ = OnAuthChangedAsync();
        profiles.ProfileChanged += (_, _) =>
        {
            if (!auth.IsSignedIn || string.IsNullOrWhiteSpace(lastRegisteredToken))
                _ = RegisterTokenAsync();
        };
    }

    public async Task StartAsync()
    {
        if (started) return;
        started = true;
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

    public async Task RegisterTokenAsync()
    {
        if (!auth.IsSignedIn) return;

        try
        {
            await firebase.EnsureReadyAsync();
            var token = await firebase.GetFcmTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
                return;

            var uid = auth.CurrentUserId;
            if (string.Equals(lastRegisteredToken, token, StringComparison.Ordinal)
                && string.Equals(lastRegisteredUid, uid, StringComparison.Ordinal))
                return;

            var deviceId = GetOrCreateDeviceId();
            await firebase.SetDocAsync(
                $"{FirestorePaths.Users}/{uid}/{FirestorePaths.FcmTokens}/{deviceId}",
                new
                {
                    token,
                    platform = "Web",
                    updatedAt = DateTimeOffset.UtcNow
                });

            lastRegisteredToken = token;
            lastRegisteredUid = uid;
        }
        catch (Exception ex)
        {
            lastRegisteredToken = null;
            lastRegisteredUid = null;
            Console.WriteLine($"RegisterTokenAsync failed: {ex.Message}");
        }
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
        boardUi.Open(groupId, listId);
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

        await RegisterTokenAsync();
        await HydrateGroupMutePreferencesAsync();
        await SubscribeUnreadAsync();
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
                if (!string.IsNullOrWhiteSpace(doc.GroupName)) data["groupName"] = doc.GroupName;

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
        return !roles.Any(r => LeadershipRoles.Contains(r));
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
        if (clickWired) return;
        try
        {
            await firebase.EnsureReadyAsync();
            selfRef ??= DotNetObjectReference.Create(this);
            await firebase.OnBoardNotificationClickAsync(selfRef);
            clickWired = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WireClickHandler failed: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await TearDownSubscriptionAsync();
        selfRef?.Dispose();
        selfRef = null;
    }
}
