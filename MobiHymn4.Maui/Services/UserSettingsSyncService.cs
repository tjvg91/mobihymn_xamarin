using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MobiHymn4.Models.Firestore;
using MobiHymn4.Utils;
using Microsoft.Maui.Networking;
using Plugin.Firebase.Firestore;

namespace MobiHymn4.Services;

public sealed class UserSettingsSyncService : IUserSettingsSyncService
{
    static readonly TimeSpan PushDebounce = TimeSpan.FromSeconds(2.5);

    readonly IFirebaseFirestoreAccessor firebase;
    readonly IAuthService auth;
    readonly SemaphoreSlim syncLock = new(1, 1);
    CancellationTokenSource debounceCts;
    int syncGeneration;
    int connectivityFlushInFlight;
    int appOpenSyncInFlight;
    DateTimeOffset lastAppOpenSyncStarted;

    public UserSettingsSyncService(IFirebaseFirestoreAccessor firebase, IAuthService auth)
    {
        this.firebase = firebase;
        this.auth = auth;
        auth.AuthStateChanged += (_, _) => _ = OnAuthChangedAsync();
        Connectivity.ConnectivityChanged += Connectivity_ConnectivityChanged;

        if (Preferences.Get(PreferencesVar.CLOUD_SETTINGS_PENDING, false))
            LastSyncStatus = "Saved offline — will sync when online.";
    }

    public event EventHandler SyncStateChanged;

    public bool IsSyncing { get; private set; }
    public string LastSyncStatus { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }

    public void SchedulePush()
    {
        if (!auth.IsSignedIn || string.IsNullOrWhiteSpace(auth.CurrentUserId))
            return;

        // Never queue uploads while local data still belongs to a different account.
        if (IsForeignAccountLocalData(auth.CurrentUserId))
            return;

        // Local settings.json is already updated by SaveSettings.
        // Mark dirty so we retry when connectivity returns.
        MarkPending(true);

        if (!IsOnline())
        {
            SetSyncing(false, "Saved offline — will sync when online.");
            return;
        }

        debounceCts?.Cancel();
        debounceCts?.Dispose();
        debounceCts = new CancellationTokenSource();
        var token = debounceCts.Token;
        var generation = Interlocked.Increment(ref syncGeneration);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(PushDebounce, token);
                if (token.IsCancellationRequested || generation != Volatile.Read(ref syncGeneration))
                    return;

                await PushAsync(token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SchedulePush failed: {ex.Message}");
                MarkPending(true);
                SetSyncing(false, "Saved offline — will sync when online.");
            }
        });
    }

    public Task SyncNowAsync(CancellationToken cancellationToken = default) =>
        PullAndMergeAsync(cancellationToken);

    public async Task SyncOnAppOpenAsync(CancellationToken cancellationToken = default)
    {
        if (!auth.IsSignedIn
            || string.IsNullOrWhiteSpace(auth.CurrentUserId)
            || !IsOnline())
            return;

        var now = DateTimeOffset.UtcNow;
        if (now - lastAppOpenSyncStarted < TimeSpan.FromSeconds(10)
            || Interlocked.CompareExchange(ref appOpenSyncInFlight, 1, 0) != 0)
            return;

        lastAppOpenSyncStarted = now;
        try
        {
            // OnStart can run before settings.json hydration completes.
            for (var i = 0; i < 20 && !Globals.Instance.SettingsHydrated; i++)
                await Task.Delay(250, cancellationToken);

            await PullAndMergeAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            Interlocked.Exchange(ref appOpenSyncInFlight, 0);
        }
    }

    public async Task PushAsync(CancellationToken cancellationToken = default)
    {
        if (!auth.IsSignedIn || string.IsNullOrWhiteSpace(auth.CurrentUserId))
            return;

        if (IsForeignAccountLocalData(auth.CurrentUserId))
        {
            MarkPending(false);
            return;
        }

        if (!IsOnline())
        {
            MarkPending(true);
            SetSyncing(false, "Saved offline — will sync when online.");
            return;
        }

        await syncLock.WaitAsync(cancellationToken);
        try
        {
            if (IsForeignAccountLocalData(auth.CurrentUserId))
            {
                MarkPending(false);
                return;
            }

            SetSyncing(true, "Uploading settings…");
            var doc = Globals.Instance.BuildCloudSettingsDocument();
            await SettingsDoc(auth.CurrentUserId).SetDataAsync(doc);
            RememberSynced(doc.UpdatedAt, auth.CurrentUserId);
            MarkPending(false);
            SetSyncing(false, "Settings backed up to cloud.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UserSettings PushAsync failed: {ex}");
            MarkPending(true);
            var detail = TruncateStatus(ex.Message);
            SetSyncing(false, IsOnline()
                ? $"Could not upload settings. {detail}"
                : "Saved offline — will sync when online.");
            // Don't throw — automatic sync should fail quietly.
        }
        finally
        {
            syncLock.Release();
        }
    }

    public async Task PullAndMergeAsync(CancellationToken cancellationToken = default)
    {
        if (!auth.IsSignedIn || string.IsNullOrWhiteSpace(auth.CurrentUserId))
        {
            SetSyncing(false, "Sign in to sync settings.");
            return;
        }

        if (!IsOnline())
        {
            // Pending local edits for a *different* account must never flush into this one.
            if (IsForeignAccountLocalData(auth.CurrentUserId))
            {
                MarkPending(false);
                SetSyncing(false, "Offline — will load this account's cloud settings when online.");
                return;
            }

            MarkPending(true);
            SetSyncing(false, "Offline — local changes will sync when online.");
            return;
        }

        await syncLock.WaitAsync(cancellationToken);
        try
        {
            var uid = auth.CurrentUserId;
            if (IsAccountSwitch(uid))
            {
                await AdoptAccountSettingsLockedAsync(uid, cancellationToken);
                return;
            }

            SetSyncing(true, "Syncing settings…");

            var snapshot = await SettingsDoc(uid)
                .GetDocumentSnapshotAsync<UserSettingsFirestoreDocument>();

            var cloud = snapshot?.Data;
            if (cloud == null)
            {
                var created = Globals.Instance.BuildCloudSettingsDocument();
                await SettingsDoc(uid).SetDataAsync(created);
                RememberSynced(created.UpdatedAt, uid);
                MarkPending(false);
                SetSyncing(false, "Local settings uploaded to cloud.");
                return;
            }

            // Prefer uploading pending local edits before merging cloud → device.
            var hadPendingLocal = Preferences.Get(PreferencesVar.CLOUD_SETTINGS_PENDING, false);
            if (hadPendingLocal)
            {
                var localFirst = Globals.Instance.BuildCloudSettingsDocument();
                await SettingsDoc(uid).SetDataAsync(localFirst);
                RememberSynced(localFirst.UpdatedAt, uid);
            }

            snapshot = await SettingsDoc(uid)
                .GetDocumentSnapshotAsync<UserSettingsFirestoreDocument>();
            cloud = snapshot?.Data;
            if (cloud != null)
            {
                Globals.Instance.MergeCloudSettings(cloud);
                Globals.Instance.SaveSettingsWithoutCloudPush();
            }

            var merged = Globals.Instance.BuildCloudSettingsDocument();
            await SettingsDoc(uid).SetDataAsync(merged);
            RememberSynced(merged.UpdatedAt, uid);
            MarkPending(false);
            SetSyncing(false, "Settings synced.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UserSettings PullAndMergeAsync failed: {ex}");
            MarkPending(true);
            var detail = TruncateStatus(ex.Message);
            SetSyncing(false, IsOnline()
                ? $"Could not sync settings. {detail}"
                : "Offline — local changes will sync when online.");
        }
        finally
        {
            syncLock.Release();
        }
    }

    /// <summary>
    /// Replace local account-scoped settings with this uid's cloud backup.
    /// Never uploads the previous account's bookmarks/history into the new account.
    /// </summary>
    async Task AdoptAccountSettingsLockedAsync(string uid, CancellationToken cancellationToken)
    {
        CancelDebounce();
        MarkPending(false);
        Preferences.Remove(PreferencesVar.CLOUD_SETTINGS_UPDATED_AT);

        SetSyncing(true, "Loading account settings…");

        var snapshot = await SettingsDoc(uid)
            .GetDocumentSnapshotAsync<UserSettingsFirestoreDocument>();
        var cloud = snapshot?.Data;

        // Replace (do not union) so the previous signed-in account cannot leak in.
        Globals.Instance.AdoptCloudSettings(cloud);
        Globals.Instance.SaveSettingsWithoutCloudPush();

        if (cloud == null)
        {
            // Fresh account: upload this device's reader prefs + empty account lists.
            var created = Globals.Instance.BuildCloudSettingsDocument();
            await SettingsDoc(uid).SetDataAsync(created);
            RememberSynced(created.UpdatedAt, uid);
            SetSyncing(false, "Account settings ready.");
            return;
        }

        RememberSynced(cloud.UpdatedAt == default ? DateTimeOffset.UtcNow : cloud.UpdatedAt, uid);
        MarkPending(false);
        SetSyncing(false, "Account settings loaded.");
    }

    void Connectivity_ConnectivityChanged(object sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess != NetworkAccess.Internet)
            return;

        if (!auth.IsSignedIn || string.IsNullOrWhiteSpace(auth.CurrentUserId))
            return;

        if (IsForeignAccountLocalData(auth.CurrentUserId))
        {
            if (Interlocked.CompareExchange(ref connectivityFlushInFlight, 1, 0) != 0)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(800);
                    await PullAndMergeAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Connectivity account adopt failed: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref connectivityFlushInFlight, 0);
                }
            });
            return;
        }

        if (!Preferences.Get(PreferencesVar.CLOUD_SETTINGS_PENDING, false))
            return;

        if (Interlocked.CompareExchange(ref connectivityFlushInFlight, 1, 0) != 0)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                // Brief delay so the radio is fully up.
                await Task.Delay(800);
                await PushAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Connectivity flush failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref connectivityFlushInFlight, 0);
            }
        });
    }

    async Task OnAuthChangedAsync()
    {
        if (!auth.IsSignedIn || string.IsNullOrWhiteSpace(auth.CurrentUserId))
        {
            CancelDebounce();
            // Drop pending so a later sign-in cannot flush the previous account's edits.
            MarkPending(false);
            SetSyncing(false, "Sign in to sync settings.");
            return;
        }

        try
        {
            var uid = auth.CurrentUserId;
            if (IsAccountSwitch(uid))
            {
                // Immediately block SchedulePush/connectivity flush from uploading foreign data.
                CancelDebounce();
                MarkPending(false);
            }

            // Wait for local settings to finish loading on cold start.
            for (var i = 0; i < 20 && !Globals.Instance.SettingsHydrated; i++)
                await Task.Delay(250);

            // Best-effort wait for hymn catalog so last-hymn restore can rebind.
            for (var i = 0; i < 40
                 && (Globals.Instance.HymnList == null || Globals.Instance.HymnList.Count == 0); i++)
                await Task.Delay(250);

            if (!IsOnline())
            {
                if (IsAccountSwitch(uid))
                {
                    SetSyncing(false, "Offline — will load this account's cloud settings when online.");
                    return;
                }

                if (Preferences.Get(PreferencesVar.CLOUD_SETTINGS_PENDING, false))
                    SetSyncing(false, "Offline — local changes will sync when online.");
                return;
            }

            await PullAndMergeAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UserSettings auth sync failed: {ex.Message}");
        }
    }

    IDocumentReference SettingsDoc(string uid) =>
        firebase.Firestore
            .GetCollection(FirestorePaths.Users)
            .GetDocument(uid)
            .GetCollection(FirestorePaths.AppData)
            .GetDocument(FirestorePaths.SettingsDoc);

    static bool IsOnline() =>
        Connectivity.NetworkAccess == NetworkAccess.Internet;

    static bool IsAccountSwitch(string uid)
    {
        var owner = Preferences.Get(PreferencesVar.CLOUD_SETTINGS_OWNER_UID, string.Empty);
        return !string.IsNullOrWhiteSpace(owner)
            && !string.Equals(owner, uid, StringComparison.Ordinal);
    }

    static bool IsForeignAccountLocalData(string uid) => IsAccountSwitch(uid);

    static void MarkPending(bool pending) =>
        Preferences.Set(PreferencesVar.CLOUD_SETTINGS_PENDING, pending);

    void RememberSynced(DateTimeOffset updatedAt, string uid)
    {
        LastSyncedAt = updatedAt;
        Preferences.Set(PreferencesVar.CLOUD_SETTINGS_UPDATED_AT, updatedAt.UtcDateTime.ToString("O"));
        if (!string.IsNullOrWhiteSpace(uid))
            Preferences.Set(PreferencesVar.CLOUD_SETTINGS_OWNER_UID, uid);
    }

    void CancelDebounce()
    {
        Interlocked.Increment(ref syncGeneration);
        debounceCts?.Cancel();
        debounceCts?.Dispose();
        debounceCts = null;
    }

    void SetSyncing(bool syncing, string status)
    {
        IsSyncing = syncing;
        if (status != null)
            LastSyncStatus = status;
        MainThread.BeginInvokeOnMainThread(() => SyncStateChanged?.Invoke(this, EventArgs.Empty));
    }

    static string TruncateStatus(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Will retry.";
        message = message.Trim();
        return message.Length <= 120 ? message : message[..117] + "...";
    }
}
