using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MobiHymn4.Models.Firestore;
using MobiHymn4.Utils;
using Plugin.Firebase.Firestore;
#if ANDROID || IOS
using Plugin.Firebase.CloudMessaging;
using Plugin.Firebase.CloudMessaging.EventArgs;
#endif
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace MobiHymn4.Services;

public sealed class BoardNotificationService : IBoardNotificationService
{
    readonly IFirebaseFirestoreAccessor firebase;
    readonly IAuthService auth;
    readonly IProfileService profileService;
    readonly IGroupService groupService;
    readonly IGroupDashboardService dashboardService;
    IDisposable notificationsSubscription;
    bool started;
    string lastRegisteredToken;
    string lastRegisteredUid;
    readonly HashSet<string> knownUnreadNotificationIds = new(StringComparer.Ordinal);
    bool unreadSnapshotSeeded;

    public BoardNotificationService(
        IFirebaseFirestoreAccessor firebase,
        IAuthService auth,
        IProfileService profileService,
        IGroupService groupService,
        IGroupDashboardService dashboardService)
    {
        this.firebase = firebase;
        this.auth = auth;
        this.profileService = profileService;
        this.groupService = groupService;
        this.dashboardService = dashboardService;

        auth.AuthStateChanged += (_, _) => _ = OnAuthChangedAsync();
        profileService.ProfileChanged += OnProfileChanged;
    }

    void OnProfileChanged(object sender, EventArgs e)
    {
        // Token write only needs auth + device — skip on every profile tick.
        if (!auth.IsSignedIn || string.IsNullOrWhiteSpace(lastRegisteredToken))
            _ = RegisterTokenAsync();
    }

    public async Task StartAsync()
    {
        if (started)
            return;

        started = true;
#if ANDROID || IOS
        try
        {
            CrossFirebaseCloudMessaging.Current.NotificationTapped += OnNotificationTapped;
            CrossFirebaseCloudMessaging.Current.NotificationReceived += OnNotificationReceived;
            CrossFirebaseCloudMessaging.Current.TokenChanged += OnTokenChanged;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FCM event wiring failed: {ex.Message}");
        }
#endif
        await OnAuthChangedAsync();
    }

    public Task StopAsync()
    {
        notificationsSubscription?.Dispose();
        notificationsSubscription = null;
        started = false;
        lastRegisteredToken = null;
        lastRegisteredUid = null;
        return Task.CompletedTask;
    }

    public async Task RegisterTokenAsync()
    {
        if (!auth.IsSignedIn)
            return;

#if ANDROID || IOS
        try
        {
            await CrossFirebaseCloudMessaging.Current.CheckIfValidAsync();
            var token = await CrossFirebaseCloudMessaging.Current.GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
            {
                Debug.WriteLine("RegisterTokenAsync: empty FCM token");
                return;
            }

            var uid = auth.CurrentUserId;
            if (string.Equals(lastRegisteredToken, token, StringComparison.Ordinal)
                && string.Equals(lastRegisteredUid, uid, StringComparison.Ordinal))
            {
                return;
            }

            var deviceId = GetDeviceId();
            var doc = new FcmTokenFirestoreDocument
            {
                Id = deviceId,
                Token = token,
                Platform = DeviceInfo.Platform.ToString(),
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            await firebase.Firestore
                .GetCollection(FirestorePaths.Users)
                .GetDocument(uid)
                .GetCollection(FirestorePaths.FcmTokens)
                .GetDocument(deviceId)
                .SetDataAsync(doc);

            lastRegisteredToken = token;
            lastRegisteredUid = uid;
            Debug.WriteLine($"RegisterTokenAsync: saved FCM token for {uid}");
        }
        catch (Exception ex)
        {
            lastRegisteredToken = null;
            lastRegisteredUid = null;
            Debug.WriteLine($"RegisterTokenAsync failed: {ex.Message}");
        }
#endif
    }

    public async Task MarkAllReadAsync()
    {
        dashboardService.MarkNotificationsRead();
        if (!auth.IsSignedIn)
            return;

        try
        {
            var unread = await QueryUnreadAsync(50);
            if (unread.Count == 0)
                return;

            var uid = auth.CurrentUserId;
            await Task.WhenAll(unread.Select(doc =>
                firebase.Firestore
                    .GetCollection(FirestorePaths.Users)
                    .GetDocument(uid)
                    .GetCollection(FirestorePaths.Notifications)
                    .GetDocument(doc.Id)
                    .UpdateDataAsync(("read", true))));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MarkAllReadAsync failed: {ex.Message}");
        }
    }

    public async Task MarkListReadAsync(string groupId, string listId)
    {
        if (string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(listId))
            return;

        groupId = groupId.Trim();
        listId = listId.Trim();

        if (!auth.IsSignedIn)
            return;

        try
        {
            var unread = await QueryUnreadAsync(50);
            var matching = unread
                .Where(d =>
                    string.Equals(d.GroupId?.Trim(), groupId, StringComparison.Ordinal)
                    && string.Equals(d.ListId?.Trim(), listId, StringComparison.Ordinal))
                .ToList();

            if (matching.Count == 0)
                return;

            var uid = auth.CurrentUserId;
            await Task.WhenAll(matching.Select(doc =>
                firebase.Firestore
                    .GetCollection(FirestorePaths.Users)
                    .GetDocument(uid)
                    .GetCollection(FirestorePaths.Notifications)
                    .GetDocument(doc.Id)
                    .UpdateDataAsync(("read", true))));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MarkListReadAsync failed: {ex.Message}");
        }
    }

    async Task<List<BoardNotificationFirestoreDocument>> QueryUnreadAsync(int limit)
    {
        var snapshot = await firebase.Firestore
            .GetCollection(FirestorePaths.Users)
            .GetDocument(auth.CurrentUserId)
            .GetCollection(FirestorePaths.Notifications)
            .WhereEqualsTo("read", false)
            .LimitedTo(limit)
            .GetDocumentsAsync<BoardNotificationFirestoreDocument>();

        return snapshot?.Documents?
            .Select(d => d.Data)
            .Where(d => d != null && !string.IsNullOrWhiteSpace(d.Id))
            .ToList()
            ?? new List<BoardNotificationFirestoreDocument>();
    }

    async Task OnAuthChangedAsync()
    {
        notificationsSubscription?.Dispose();
        notificationsSubscription = null;

        if (!auth.IsSignedIn)
        {
            lastRegisteredToken = null;
            lastRegisteredUid = null;
            knownUnreadNotificationIds.Clear();
            unreadSnapshotSeeded = false;
            dashboardService.SetUnreadLists(null);
            return;
        }

        await RegisterTokenAsync();
        await HydrateGroupMutePreferencesAsync();
        SubscribeUnread();
    }

    async Task HydrateGroupMutePreferencesAsync()
    {
        var groupIds = profileService.CurrentProfile?.GroupIds;
        if (groupIds == null || groupIds.Count == 0)
            return;

        foreach (var groupId in groupIds.Distinct(StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(groupId))
                continue;
            try
            {
                // Force-refresh from Firestore so ShouldSuppressPush sees group mutes
                // even when local prefs were never written (reinstall / other device).
                var prefKey = PreferencesVar.GROUP_NOTIFICATIONS_MUTED_PREFIX + groupId.Trim();
                Preferences.Default.Remove(prefKey);
                await groupService.IsGroupNotificationsMutedAsync(groupId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HydrateGroupMutePreferencesAsync failed for {groupId}: {ex.Message}");
            }
        }
    }

    void SubscribeUnread()
    {
        knownUnreadNotificationIds.Clear();
        unreadSnapshotSeeded = false;
        try
        {
            notificationsSubscription = firebase.Firestore
                .GetCollection(FirestorePaths.Users)
                .GetDocument(auth.CurrentUserId)
                .GetCollection(FirestorePaths.Notifications)
                .WhereEqualsTo("read", false)
                .LimitedTo(50)
                .AddSnapshotListener<BoardNotificationFirestoreDocument>(
                    snapshot =>
                    {
                        var docs = snapshot?.Documents?
                            .Select(d => d.Data)
                            .Where(d => d != null && !d.Read)
                            .ToList()
                            ?? new List<BoardNotificationFirestoreDocument>();
                        ApplyUnreadSnapshot(docs);
                    },
                    ex => Debug.WriteLine($"Notifications listener failed: {ex?.Message}"));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SubscribeUnread failed: {ex.Message}");
            try
            {
                notificationsSubscription = firebase.Firestore
                    .GetCollection(FirestorePaths.Users)
                    .GetDocument(auth.CurrentUserId)
                    .GetCollection(FirestorePaths.Notifications)
                    .AddSnapshotListener<BoardNotificationFirestoreDocument>(
                        snapshot =>
                        {
                            var docs = snapshot?.Documents?
                                .Select(d => d.Data)
                                .Where(d => d != null && !d.Read)
                                .ToList()
                                ?? new List<BoardNotificationFirestoreDocument>();
                            ApplyUnreadSnapshot(docs);
                        },
                        err => Debug.WriteLine($"Notifications fallback listener failed: {err?.Message}"));
            }
            catch (Exception fallbackEx)
            {
                Debug.WriteLine($"SubscribeUnread fallback failed: {fallbackEx.Message}");
            }
        }
    }

    void ApplyUnreadSnapshot(IReadOnlyList<BoardNotificationFirestoreDocument> docs)
    {
        var byList = new Dictionary<string, BoardListUnreadInfo>(StringComparer.Ordinal);
        var currentIds = new HashSet<string>(StringComparer.Ordinal);
        var newlyArrived = new List<BoardNotificationFirestoreDocument>();

        foreach (var doc in docs)
        {
            if (doc == null || string.IsNullOrWhiteSpace(doc.Id))
                continue;

            currentIds.Add(doc.Id);
            if (unreadSnapshotSeeded && !knownUnreadNotificationIds.Contains(doc.Id))
                newlyArrived.Add(doc);

            var groupId = doc.GroupId?.Trim();
            var listId = doc.ListId?.Trim();
            if (string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(listId))
                continue;

            var key = GroupDashboardService.ListKey(groupId, listId);
            var created = doc.CreatedAt == default
                ? DateTime.UtcNow
                : doc.CreatedAt.UtcDateTime;
            var hymnCount = doc.NewHymnCount > 0 ? (int)doc.NewHymnCount : 1;
            var deleted = ExtractDeletedHymns(doc, created);

            if (byList.TryGetValue(key, out var existing))
            {
                byList[key] = new BoardListUnreadInfo
                {
                    NewHymnCount = existing.NewHymnCount + hymnCount,
                    OldestCreatedAtUtc = created < existing.OldestCreatedAtUtc
                        ? created
                        : existing.OldestCreatedAtUtc,
                    DeletedHymns = MergeDeletedHymns(existing.DeletedHymns, deleted),
                };
            }
            else
            {
                byList[key] = new BoardListUnreadInfo
                {
                    NewHymnCount = hymnCount,
                    OldestCreatedAtUtc = created,
                    DeletedHymns = deleted,
                };
            }
        }

        knownUnreadNotificationIds.Clear();
        foreach (var id in currentIds)
            knownUnreadNotificationIds.Add(id);
        unreadSnapshotSeeded = true;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            dashboardService.SetUnreadLists(byList);
            NotifyNewlyArrived(newlyArrived);
        });
    }

    static IReadOnlyList<BoardDeletedHymnInfo> ExtractDeletedHymns(
        BoardNotificationFirestoreDocument doc,
        DateTime createdUtc)
    {
        if (doc?.DeletedHymns == null || doc.DeletedHymns.Count == 0)
            return Array.Empty<BoardDeletedHymnInfo>();

        var who = string.IsNullOrWhiteSpace(doc.UpdatedByName)
            ? "Someone"
            : doc.UpdatedByName.Trim();
        var list = new List<BoardDeletedHymnInfo>();
        foreach (var h in doc.DeletedHymns)
        {
            if (h == null || string.IsNullOrWhiteSpace(h.Id))
                continue;
            list.Add(new BoardDeletedHymnInfo
            {
                Id = h.Id.Trim(),
                HymnNumber = h.HymnNumber?.Trim() ?? string.Empty,
                Notes = h.Notes ?? string.Empty,
                SortOrder = (int)h.SortOrder,
                DeletedByName = who,
                DeletedAtUtc = createdUtc,
            });
        }

        return list;
    }

    static IReadOnlyList<BoardDeletedHymnInfo> MergeDeletedHymns(
        IReadOnlyList<BoardDeletedHymnInfo> existing,
        IReadOnlyList<BoardDeletedHymnInfo> incoming)
    {
        if ((existing == null || existing.Count == 0)
            && (incoming == null || incoming.Count == 0))
            return Array.Empty<BoardDeletedHymnInfo>();

        var byId = new Dictionary<string, BoardDeletedHymnInfo>(StringComparer.Ordinal);
        if (existing != null)
        {
            foreach (var item in existing)
            {
                if (item != null && !string.IsNullOrWhiteSpace(item.Id))
                    byId[item.Id] = item;
            }
        }

        if (incoming != null)
        {
            foreach (var item in incoming)
            {
                if (item != null && !string.IsNullOrWhiteSpace(item.Id))
                    byId[item.Id] = item;
            }
        }

        return byId.Values
            .OrderBy(h => h.SortOrder)
            .ThenBy(h => h.DeletedAtUtc)
            .ToList();
    }

    void NotifyNewlyArrived(IReadOnlyList<BoardNotificationFirestoreDocument> newlyArrived)
    {
        if (newlyArrived == null || newlyArrived.Count == 0)
            return;

#if ANDROID
        // Background tray is handled by FCM. Posting again here duplicates it.
        if (!IsAndroidAppForeground())
            return;

        foreach (var doc in newlyArrived)
        {
            if (doc.SuppressPush || ShouldSuppressPush(doc.GroupId))
                continue;

            var who = string.IsNullOrWhiteSpace(doc.UpdatedByName) ? "Someone" : doc.UpdatedByName.Trim();
            var action = (doc.ChangeAction ?? "updated").Trim().ToLowerInvariant();
            if (action is not ("added" or "updated" or "deleted"))
                action = "updated";
            var count = doc.NewHymnCount > 0 ? (int)doc.NewHymnCount : 1;
            var title = string.IsNullOrWhiteSpace(doc.Title)
                ? $"{who} {action} {(count == 1 ? "a hymn" : $"{count} hymns")}"
                : doc.Title;
            var body = doc.Body;
            if (string.IsNullOrWhiteSpace(body))
            {
                var group = string.IsNullOrWhiteSpace(doc.GroupName) ? null : doc.GroupName.Trim();
                var list = string.IsNullOrWhiteSpace(doc.ListId) ? null : doc.ListId.Trim();
                body = string.Join(" · ", new[] { group, list }.Where(s => !string.IsNullOrWhiteSpace(s)));
                if (string.IsNullOrWhiteSpace(body))
                    body = "Your group board was updated.";
            }

            var data = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(doc.GroupId))
                data["groupId"] = doc.GroupId;
            if (!string.IsNullOrWhiteSpace(doc.ListId))
            {
                data["listId"] = doc.ListId;
                data["date"] = doc.ListId;
            }
            if (!string.IsNullOrWhiteSpace(doc.GroupName))
                data["groupName"] = doc.GroupName;
            if (!string.IsNullOrWhiteSpace(doc.UpdatedByName))
                data["updatedByName"] = doc.UpdatedByName;

            Platforms.Android.BoardLocalNotifier.Show(title, body, data);
        }
#endif
    }

    bool ShouldSuppressPush(string groupId)
    {
        var profile = profileService.CurrentProfile;
        if (profile != null)
        {
            // Match Cloud Function: explicit mute wins; else role default.
            var globallyMuted = profile.NotificationsMuted
                || (!profile.NotificationsPreferenceSet
                    && RolePermissions.GetDefaultNotificationsMuted(profile.Roles));
            if (globallyMuted)
                return true;
        }

        var normalized = groupId?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        return Preferences.Default.Get(
            PreferencesVar.GROUP_NOTIFICATIONS_MUTED_PREFIX + normalized,
            false);
    }

#if ANDROID
    static bool IsAndroidAppForeground()
    {
        try
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity == null || activity.IsFinishing)
                return false;

            if (activity is AndroidX.AppCompat.App.AppCompatActivity compat
                && compat.Lifecycle != null)
            {
                return compat.Lifecycle.CurrentState.IsAtLeast(AndroidX.Lifecycle.Lifecycle.State.Resumed);
            }

            return true;
        }
        catch
        {
            return true;
        }
    }
#endif

#if ANDROID || IOS
    void OnTokenChanged(object sender, FCMTokenChangedEventArgs e) =>
        _ = RegisterTokenAsync();

    void OnNotificationReceived(object sender, FCMNotificationReceivedEventArgs e)
    {
        // Do not post a local tray here. When the app is alive, Firestore unread docs
        // already drive BoardLocalNotifier; posting again duplicates the FCM tray item.
    }

    void OnNotificationTapped(object sender, FCMNotificationTappedEventArgs e)
    {
        var data = e.Notification?.Data;
        if (data == null)
            return;

        data.TryGetValue("groupId", out var groupId);
        data.TryGetValue("listId", out var listId);
        data.TryGetValue("date", out var date);
        var resolvedListId = !string.IsNullOrWhiteSpace(listId) ? listId : date;

        MainThread.BeginInvokeOnMainThread(() =>
            dashboardService.Open(groupId, resolvedListId));
    }
#endif

    static string GetDeviceId()
    {
        try
        {
            var id = Preferences.Default.Get("fcm_device_id", string.Empty);
            if (!string.IsNullOrWhiteSpace(id))
                return id;

            id = Guid.NewGuid().ToString("N");
            Preferences.Default.Set("fcm_device_id", id);
            return id;
        }
        catch
        {
            return Guid.NewGuid().ToString("N");
        }
    }
}
