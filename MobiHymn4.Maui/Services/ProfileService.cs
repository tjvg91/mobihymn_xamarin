using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MobiHymn4.Models;
using MobiHymn4.Models.Firestore;
using MobiHymn4.Utils;
using Plugin.Firebase.Firestore;

namespace MobiHymn4.Services;

public sealed class ProfileService : IProfileService
{
    static readonly TimeSpan ProfileCacheTtl = TimeSpan.FromSeconds(45);

    readonly IFirebaseFirestoreAccessor firebase;
    readonly IAuthService auth;
    readonly SemaphoreSlim refreshLock = new(1, 1);
    DateTime profileLoadedAt;
    string profileLoadedUid;

    public ProfileService(IFirebaseFirestoreAccessor firebase, IAuthService auth)
    {
        this.firebase = firebase;
        this.auth = auth;
        auth.AuthStateChanged += OnAuthStateChanged;
    }

    void OnAuthStateChanged(object sender, EventArgs e) => _ = RefreshCurrentProfileSafeAsync();

    public event EventHandler ProfileChanged;

    public UserProfile CurrentProfile { get; private set; }
    public bool HasCompleteProfile => CurrentProfile?.IsComplete ?? false;

    public async Task<UserProfile> LoadProfileAsync(string uid)
    {
        if (string.IsNullOrWhiteSpace(uid))
            return null;

        try
        {
            var snapshot = await firebase.Firestore
                .GetCollection(FirestorePaths.Users)
                .GetDocument(uid)
                .GetDocumentSnapshotAsync<UserFirestoreDocument>();

            return snapshot?.Data == null ? null : FirestoreMappers.ToUserProfile(snapshot.Data);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadProfileAsync failed: {ex.Message}");
            return null;
        }
    }

    public async Task SaveProfileAsync(UserProfile profile)
    {
        if (profile == null || string.IsNullOrWhiteSpace(profile.Uid))
            throw new InvalidOperationException("Profile is missing a user id.");

        profile.ApplyDefaultNotificationPreferenceIfNeeded();

        var doc = FirestoreMappers.ToFirestore(profile);
        await firebase.Firestore
            .GetCollection(FirestorePaths.Users)
            .GetDocument(profile.Uid)
            .SetDataAsync(doc);

        if (auth.CurrentUserId == profile.Uid)
        {
            CurrentProfile = profile;
            profileLoadedUid = profile.Uid;
            profileLoadedAt = DateTime.UtcNow;
            ProfileChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task SetNotificationsMutedAsync(bool muted)
    {
        if (CurrentProfile == null)
            return;

        CurrentProfile.NotificationsMuted = muted;
        CurrentProfile.NotificationsPreferenceSet = true;
        await firebase.Firestore
            .GetCollection(FirestorePaths.Users)
            .GetDocument(CurrentProfile.Uid)
            .UpdateDataAsync(
                ("notificationsMuted", muted),
                ("notificationsPreferenceSet", true));
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RefreshCurrentProfileAsync(bool force = false)
    {
        if (!auth.IsSignedIn)
        {
            CurrentProfile = null;
            profileLoadedUid = null;
            profileLoadedAt = default;
            ProfileChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var uid = auth.CurrentUserId;
        if (!force
            && CurrentProfile != null
            && string.Equals(CurrentProfile.Uid, uid, StringComparison.Ordinal)
            && DateTime.UtcNow - profileLoadedAt < ProfileCacheTtl)
        {
            return;
        }

        await refreshLock.WaitAsync();
        try
        {
            if (!auth.IsSignedIn)
            {
                CurrentProfile = null;
                profileLoadedUid = null;
                profileLoadedAt = default;
                ProfileChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            uid = auth.CurrentUserId;
            if (!force
                && CurrentProfile != null
                && string.Equals(CurrentProfile.Uid, uid, StringComparison.Ordinal)
                && DateTime.UtcNow - profileLoadedAt < ProfileCacheTtl)
            {
                return;
            }

            CurrentProfile = await LoadProfileAsync(uid);
            profileLoadedUid = uid;
            profileLoadedAt = DateTime.UtcNow;
            await EnsureRoleBasedNotificationDefaultAsync();
            ProfileChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            refreshLock.Release();
        }
    }

    async Task EnsureRoleBasedNotificationDefaultAsync()
    {
        var profile = CurrentProfile;
        if (profile == null || profile.NotificationsPreferenceSet)
            return;

        var desired = RolePermissions.GetDefaultNotificationsMuted(profile.Roles);
        if (profile.NotificationsMuted == desired)
            return;

        profile.NotificationsMuted = desired;
        try
        {
            await firebase.Firestore
                .GetCollection(FirestorePaths.Users)
                .GetDocument(profile.Uid)
                .UpdateDataAsync(("notificationsMuted", desired));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"EnsureRoleBasedNotificationDefaultAsync failed: {ex.Message}");
        }
    }

    async Task RefreshCurrentProfileSafeAsync()
    {
        try
        {
            await RefreshCurrentProfileAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Profile refresh failed: {ex.Message}");
        }
    }
}
