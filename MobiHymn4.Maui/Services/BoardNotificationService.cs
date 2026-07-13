using System;
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

namespace MobiHymn4.Services;

public sealed class BoardNotificationService : IBoardNotificationService
{
    readonly IFirebaseFirestoreAccessor firebase;
    readonly IAuthService auth;
    readonly IProfileService profileService;
    readonly IGroupDashboardService dashboardService;
    IDisposable notificationsSubscription;
    bool started;

    public BoardNotificationService(
        IFirebaseFirestoreAccessor firebase,
        IAuthService auth,
        IProfileService profileService,
        IGroupDashboardService dashboardService)
    {
        this.firebase = firebase;
        this.auth = auth;
        this.profileService = profileService;
        this.dashboardService = dashboardService;

        auth.AuthStateChanged += (_, _) => _ = OnAuthChangedAsync();
        profileService.ProfileChanged += (_, _) => _ = RegisterTokenAsync();
        dashboardService.IsOpenChanged += (_, _) =>
        {
            if (dashboardService.IsOpen)
                _ = MarkAllReadAsync();
        };
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
                return;

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
                .GetDocument(auth.CurrentUserId)
                .GetCollection(FirestorePaths.FcmTokens)
                .GetDocument(deviceId)
                .SetDataAsync(doc);
        }
        catch (Exception ex)
        {
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
            var snapshot = await firebase.Firestore
                .GetCollection(FirestorePaths.Users)
                .GetDocument(auth.CurrentUserId)
                .GetCollection(FirestorePaths.Notifications)
                .GetDocumentsAsync<BoardNotificationFirestoreDocument>();

            var unread = snapshot?.Documents?
                .Select(d => d.Data)
                .Where(d => d != null && !d.Read && !string.IsNullOrWhiteSpace(d.Id))
                .ToList();
            if (unread == null || unread.Count == 0)
                return;

            foreach (var doc in unread)
            {
                await firebase.Firestore
                    .GetCollection(FirestorePaths.Users)
                    .GetDocument(auth.CurrentUserId)
                    .GetCollection(FirestorePaths.Notifications)
                    .GetDocument(doc.Id)
                    .UpdateDataAsync(("read", true));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MarkAllReadAsync failed: {ex.Message}");
        }
    }

    async Task OnAuthChangedAsync()
    {
        notificationsSubscription?.Dispose();
        notificationsSubscription = null;

        if (!auth.IsSignedIn)
        {
            dashboardService.SetUnreadNotifications(false);
            return;
        }

        await RegisterTokenAsync();
        SubscribeUnread();
    }

    void SubscribeUnread()
    {
        try
        {
            notificationsSubscription = firebase.Firestore
                .GetCollection(FirestorePaths.Users)
                .GetDocument(auth.CurrentUserId)
                .GetCollection(FirestorePaths.Notifications)
                .AddSnapshotListener<BoardNotificationFirestoreDocument>(
                    snapshot =>
                    {
                        var hasUnread = snapshot?.Documents?
                            .Any(d => d.Data != null && !d.Data.Read) == true;
                        MainThread.BeginInvokeOnMainThread(() =>
                            dashboardService.SetUnreadNotifications(hasUnread && !dashboardService.IsOpen));
                    },
                    ex => Debug.WriteLine($"Notifications listener failed: {ex?.Message}"));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SubscribeUnread failed: {ex.Message}");
        }
    }

#if ANDROID || IOS
    void OnTokenChanged(object sender, FCMTokenChangedEventArgs e) =>
        _ = RegisterTokenAsync();

    void OnNotificationReceived(object sender, FCMNotificationReceivedEventArgs e)
    {
        if (!dashboardService.IsOpen)
            dashboardService.SetUnreadNotifications(true);
    }

    void OnNotificationTapped(object sender, FCMNotificationTappedEventArgs e)
    {
        var data = e.Notification?.Data;
        if (data == null)
            return;

        data.TryGetValue("groupId", out var groupId);
        data.TryGetValue("listId", out var listId);
        data.TryGetValue("date", out var date);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            dashboardService.Open(
                groupId,
                !string.IsNullOrWhiteSpace(listId) ? listId : date);
            _ = MarkAllReadAsync();
        });
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
