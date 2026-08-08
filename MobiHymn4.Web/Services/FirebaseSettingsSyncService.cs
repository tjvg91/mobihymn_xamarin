using Microsoft.Extensions.DependencyInjection;
using MobiHymn4.Shared;
using MobiHymn4.Shared.Services;

namespace MobiHymn4.Web.Services;

public sealed class FirebaseSettingsSyncService : IUserSettingsSyncService
{
    readonly IAuthService auth;
    readonly IAppPreferences prefs;
    readonly UserSettingsSyncEngine engine;
    CancellationTokenSource? debounce;
    bool bootstrapped;
    bool wasSignedIn;

    public FirebaseSettingsSyncService(
        IUserSettingsCloudStore cloud,
        IAuthService auth,
        IAppPreferences prefs,
        IServiceProvider services)
    {
        this.auth = auth;
        this.prefs = prefs;
        engine = new UserSettingsSyncEngine(
            prefs,
            cloud,
            () => services.GetRequiredService<AppState>(),
            () => auth.IsSignedIn,
            () => auth.CurrentUserId);
        auth.AuthStateChanged += (_, _) => _ = OnAuthChangedAsync();
    }

    public event EventHandler? SyncStateChanged;
    public bool IsSyncing => engine.IsSyncing;
    public string LastSyncStatus => engine.LastSyncStatus;

    public async Task BootstrapAsync()
    {
        if (bootstrapped) return;
        bootstrapped = true;
        wasSignedIn = auth.IsSignedIn;

        if (!auth.IsSignedIn)
        {
            // Stay local — do not wipe guest/device settings on cold start.
            SyncStateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Restored session: merge by timestamp so a closed-before-upload edit isn't lost.
        await PullAndMergeAsync(preferCloud: false);
    }

    async Task OnAuthChangedAsync()
    {
        // Auth may restore before BootstrapAsync — let Bootstrap own the first merge.
        if (!bootstrapped)
        {
            wasSignedIn = auth.IsSignedIn;
            return;
        }

        var signedIn = auth.IsSignedIn;
        if (!signedIn)
        {
            // Only clear when transitioning from signed-in → signed-out (real sign-out),
            // never on every launch while already signed out.
            if (wasSignedIn)
                await engine.HandleSignedOutAsync();
            wasSignedIn = false;
            SyncStateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var justSignedIn = !wasSignedIn;
        wasSignedIn = true;
        await PullAndMergeAsync(preferCloud: justSignedIn);
    }

    public void SchedulePush()
    {
        if (!auth.IsSignedIn)
            return;

        var owner = prefs.Get(PrefKeys.CloudOwnerUid);
        if (!string.IsNullOrEmpty(owner) && owner != auth.CurrentUserId)
            return;

        prefs.SetBool(PrefKeys.CloudPending, true);
        debounce?.Cancel();
        debounce = new CancellationTokenSource();
        var token = debounce.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2500, token);
                await PushAsync(token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // Status already set inside engine for push failures; surface unexpected errors.
                SyncStateChanged?.Invoke(this, EventArgs.Empty);
                _ = ex;
            }
        });
    }

    public async Task PushAsync(CancellationToken cancellationToken = default)
    {
        await engine.PushAsync(cancellationToken);
        SyncStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task PullAndMergeAsync(CancellationToken cancellationToken = default) =>
        PullAndMergeAsync(preferCloud: false, cancellationToken);

    public async Task PullAndMergeAsync(bool preferCloud, CancellationToken cancellationToken = default)
    {
        await engine.PullAndMergeAsync(preferCloud, cancellationToken);
        SyncStateChanged?.Invoke(this, EventArgs.Empty);
    }
}
