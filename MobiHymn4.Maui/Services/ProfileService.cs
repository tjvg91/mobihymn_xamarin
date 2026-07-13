using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MobiHymn4.Models;
using MobiHymn4.Models.Firestore;
using MobiHymn4.Utils;
using Plugin.Firebase.Firestore;

namespace MobiHymn4.Services;

public sealed class ProfileService : IProfileService
{
    readonly IFirebaseFirestoreAccessor firebase;
    readonly IAuthService auth;

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

    public async Task RefreshCurrentProfileAsync()
    {
        if (!auth.IsSignedIn)
        {
            CurrentProfile = null;
            ProfileChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        CurrentProfile = await LoadProfileAsync(auth.CurrentUserId);
        await EnsureRoleBasedNotificationDefaultAsync();
        ProfileChanged?.Invoke(this, EventArgs.Empty);
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
