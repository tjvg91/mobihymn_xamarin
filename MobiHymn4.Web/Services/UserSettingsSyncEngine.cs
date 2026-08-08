using MobiHymn4.Shared;
using MobiHymn4.Shared.Models;
using MobiHymn4.Shared.Services;

namespace MobiHymn4.Web.Services;

/// <summary>
/// Account-aware settings sync rules (sign-out wipe, account switch, sign-up seed, same-account merge).
/// Firebase / UI wrappers call this; unit tests use in-memory stores.
/// </summary>
public sealed class UserSettingsSyncEngine
{
    readonly IAppPreferences prefs;
    readonly IUserSettingsCloudStore cloud;
    readonly Func<AppState> appState;
    readonly Func<bool> isSignedIn;
    readonly Func<string> currentUserId;
    readonly SemaphoreSlim syncLock = new(1, 1);

    public UserSettingsSyncEngine(
        IAppPreferences prefs,
        IUserSettingsCloudStore cloud,
        Func<AppState> appState,
        Func<bool> isSignedIn,
        Func<string> currentUserId)
    {
        this.prefs = prefs;
        this.cloud = cloud;
        this.appState = appState;
        this.isSignedIn = isSignedIn;
        this.currentUserId = currentUserId;
    }

    public bool IsSyncing { get; private set; }
    public string LastSyncStatus { get; private set; } = "Sign in to sync settings.";

    public async Task HandleSignedOutAsync()
    {
        // Drop previous account's local settings immediately so they cannot leak
        // into the next sign-in. Keep CloudOwnerUid for switch detection.
        try
        {
            await appState().ClearAccountLocalStorageAndResetAsync();
        }
        catch
        {
            // AppState may not be ready during very early startup.
        }

        prefs.SetBool(PrefKeys.CloudPending, false);
        LastSyncStatus = "Sign in to sync settings.";
    }

    public async Task PullAndMergeAsync(bool preferCloud, CancellationToken cancellationToken = default)
    {
        if (!isSignedIn() || string.IsNullOrEmpty(currentUserId()))
        {
            LastSyncStatus = "Sign in to sync settings.";
            return;
        }

        await syncLock.WaitAsync(cancellationToken);
        try
        {
            IsSyncing = true;
            LastSyncStatus = "Syncing settings…";
            await cloud.EnsureReadyAsync();

            var state = appState();
            var uid = currentUserId();
            var owner = prefs.Get(PrefKeys.CloudOwnerUid);
            var switched = !string.IsNullOrEmpty(owner) && owner != uid;

            var seedLocal = prefs.GetBool(PrefKeys.SeedLocalSettingsOnNextSync, false);
            if (seedLocal)
                prefs.SetBool(PrefKeys.SeedLocalSettingsOnNextSync, false);

            if (switched || seedLocal || (preferCloud && string.IsNullOrEmpty(owner)))
            {
                prefs.SetBool(PrefKeys.CloudPending, false);

                if (switched && !seedLocal)
                {
                    prefs.Remove(PrefKeys.CloudUpdatedAt);
                    await state.ClearAccountLocalStorageAndResetAsync();
                }

                var cloudSwitch = await cloud.GetSettingsAsync(uid);

                if (cloudSwitch == null || seedLocal)
                {
                    var created = state.BuildCloudDocument();
                    await cloud.SetSettingsAsync(uid, created);
                    prefs.Set(PrefKeys.CloudOwnerUid, uid);
                    prefs.Set(PrefKeys.CloudUpdatedAt, created.UpdatedAt.ToString("O"));
                    LastSyncStatus = seedLocal
                        ? "Local settings saved to new account."
                        : switched
                            ? "New account — defaults ready."
                            : "Local settings uploaded to cloud.";
                }
                else
                {
                    await state.ApplyCloudSettingsAsync(
                        cloudSwitch,
                        replaceAccountData: true,
                        preferCloudReaderPrefs: true);
                    prefs.Set(PrefKeys.CloudOwnerUid, uid);
                    var stamp = cloudSwitch.UpdatedAt == default ? DateTimeOffset.UtcNow : cloudSwitch.UpdatedAt;
                    prefs.Set(PrefKeys.CloudUpdatedAt, stamp.ToString("O"));
                    var merged = state.BuildCloudDocument();
                    merged.UpdatedAt = stamp;
                    await cloud.SetSettingsAsync(uid, merged);
                    LastSyncStatus = "Settings synced from cloud.";
                }

                return;
            }

            var cloudDoc = await cloud.GetSettingsAsync(uid);

            if (cloudDoc == null)
            {
                var created = state.BuildCloudDocument();
                await cloud.SetSettingsAsync(uid, created);
                prefs.Set(PrefKeys.CloudOwnerUid, uid);
                prefs.Set(PrefKeys.CloudUpdatedAt, created.UpdatedAt.ToString("O"));
                prefs.SetBool(PrefKeys.CloudPending, false);
                LastSyncStatus = "Local settings uploaded to cloud.";
                return;
            }

            if (!preferCloud && prefs.GetBool(PrefKeys.CloudPending, false))
            {
                var localFirst = state.BuildCloudDocument();
                await cloud.SetSettingsAsync(uid, localFirst);
                prefs.Set(PrefKeys.CloudUpdatedAt, localFirst.UpdatedAt.ToString("O"));
                cloudDoc = await cloud.GetSettingsAsync(uid) ?? cloudDoc;
            }

            await state.ApplyCloudSettingsAsync(
                cloudDoc,
                replaceAccountData: false,
                preferCloudReaderPrefs: preferCloud);

            var mergedDoc = state.BuildCloudDocument();
            if (preferCloud && cloudDoc.UpdatedAt != default)
                mergedDoc.UpdatedAt = cloudDoc.UpdatedAt;
            await cloud.SetSettingsAsync(uid, mergedDoc);
            prefs.Set(PrefKeys.CloudOwnerUid, uid);
            prefs.Set(PrefKeys.CloudUpdatedAt, mergedDoc.UpdatedAt.ToString("O"));
            prefs.SetBool(PrefKeys.CloudPending, false);
            LastSyncStatus = "Settings synced.";
        }
        catch (Exception ex)
        {
            prefs.SetBool(PrefKeys.CloudPending, true);
            LastSyncStatus = "Could not sync settings. " + ex.Message;
        }
        finally
        {
            IsSyncing = false;
            syncLock.Release();
        }
    }

    public async Task PushAsync(CancellationToken cancellationToken = default)
    {
        if (!isSignedIn() || string.IsNullOrEmpty(currentUserId()))
            return;

        var uid = currentUserId();
        var owner = prefs.Get(PrefKeys.CloudOwnerUid);
        if (!string.IsNullOrEmpty(owner) && owner != uid)
        {
            prefs.SetBool(PrefKeys.CloudPending, false);
            return;
        }

        await syncLock.WaitAsync(cancellationToken);
        try
        {
            IsSyncing = true;
            LastSyncStatus = "Uploading settings…";
            await cloud.EnsureReadyAsync();
            var doc = appState().BuildCloudDocument();
            await cloud.SetSettingsAsync(uid, doc);
            prefs.Set(PrefKeys.CloudOwnerUid, uid);
            prefs.Set(PrefKeys.CloudUpdatedAt, doc.UpdatedAt.ToString("O"));
            prefs.SetBool(PrefKeys.CloudPending, false);
            LastSyncStatus = "Local settings uploaded to cloud.";
        }
        catch (Exception ex)
        {
            prefs.SetBool(PrefKeys.CloudPending, true);
            LastSyncStatus = "Could not upload settings. " + ex.Message;
        }
        finally
        {
            IsSyncing = false;
            syncLock.Release();
        }
    }
}
