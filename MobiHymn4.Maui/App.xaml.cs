using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using MobiHymn4.Extensions;
using MobiHymn4.Models;
using MobiHymn4.Services;
using MobiHymn4.Utils;
using MobiHymn4.ViewModels;
using MobiHymn4.Views.Popups;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace MobiHymn4;

public partial class App : Application
{
    private readonly Globals globalInstance = Globals.Instance;
    private readonly IFirebaseHelper fbHelper;
    private readonly FirebaseHelper fbInstance;
    private readonly bool fromFirebaseNotif;

    public App(bool hasNotification = false, IDictionary<string, object> notificationData = null)
    {
        InitializeComponent();

        MainPage = new AppShell();

        globalInstance.DarkMode = Preferences.Get(PreferencesVar.DARK_MODE, false);
        globalInstance.KeepAwake = Preferences.Get(PreferencesVar.KEEP_AWAKE, true);

        fbHelper = ServiceHelper.Get<IFirebaseHelper>();
        fbInstance = FirebaseHelper.Instance;
        fromFirebaseNotif = hasNotification;

        globalInstance.InitFinished += GlobalInstance_InitFinished;

        globalInstance.RefreshIncompleteDownloadState();

        // TODO: Wire Plugin.Firebase.CloudMessaging token and notification handlers.
    }

    protected override void OnStart()
    {
        try
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await globalInstance.LoadSettings();
                DeviceDisplay.KeepScreenOn = globalInstance.KeepAwake;
            });

            if (!fromFirebaseNotif)
                QueueFirebaseInit();
            else
                QueueUpdateCheck();

            QueueInterruptedDownloadRecovery();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
    }

    void QueueInterruptedDownloadRecovery()
    {
        globalInstance.RefreshIncompleteDownloadState();

        if (!DownloadPopupPresenter.IsDownloadRecoveryPending())
            return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var delays = new[] { 100, 400, 900, 1500, 2500 };
            foreach (var delay in delays)
            {
                await Task.Delay(delay);
                if (!DownloadPopupPresenter.IsDownloadRecoveryPending())
                    break;

                DownloadPopupPresenter.ShowWithRetry();
            }

            globalInstance.TryResumeInitAfterRelaunch();
        });
    }

    protected override void OnSleep()
    {
        DeviceDisplay.KeepScreenOn = false;
        if (!globalInstance.InitInProgress)
            globalInstance.SaveSettings();
    }

    void QueueFirebaseInit()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(1500);
            await InitFirebaseAsync();
            await CheckForAppUpdateAsync();
        });
    }

    void QueueUpdateCheck()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(1500);
            await CheckForAppUpdateAsync();
        });
    }

    async Task InitFirebaseAsync()
    {
        await fbHelper.LoginWithEmailPassword("tim.gandionco@gmail.com", "TLmSIsnw231");
        await globalInstance.RefreshMissingHymnCountAsync();
    }

    async Task CheckForAppUpdateAsync()
    {
        try
        {
            var release = await fbInstance.RetrieveLatestRelease();
            if (release == null || string.IsNullOrWhiteSpace(release.Version))
                return;

            var current = AppInfo.VersionString;
            if (!IsNewerVersion(release.Version, current))
                return;

            await MainThread.InvokeOnMainThreadAsync(async () =>
                await PromptUpdateAsync(release));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update check failed: {ex.Message}");
        }
    }

    async Task PromptUpdateAsync(LatestRelease release)
    {
        if (release.Mandatory)
        {
            await ShowUpdatePopupAsync(
                "Update Required",
                "A new version is available and this update is required. Please download and install it to continue using MobiHymn.",
                mandatory: true,
                downloadText: "Download Now");

            if (!string.IsNullOrWhiteSpace(release.DownloadUrl))
                await Launcher.OpenAsync(release.DownloadUrl);

            await PromptUpdateAsync(release);
        }
        else
        {
            var result = await ShowUpdatePopupAsync(
                "Update Available",
                "A new update is available.",
                mandatory: false);

            if (result == UpdatePopup.ResultDownload && !string.IsNullOrWhiteSpace(release.DownloadUrl))
                await Launcher.OpenAsync(release.DownloadUrl);
        }
    }

    async Task<string> ShowUpdatePopupAsync(string title, string message, bool mandatory, string downloadText = "Download")
    {
        var page = Shell.Current?.CurrentPage as Page ?? MainPage as Page;
        if (page == null)
            return UpdatePopup.ResultLater;

        var popup = new UpdatePopup();
        popup.Configure(title, message, mandatory, downloadText);

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        popup.Closed += (_, e) =>
            tcs.TrySetResult(e.Result as string ?? UpdatePopup.ResultLater);

        await MainThread.InvokeOnMainThreadAsync(() => page.ShowPopup(popup));
        return await tcs.Task;
    }

    static bool IsNewerVersion(string remote, string local)
    {
        if (Version.TryParse(remote, out var r) && Version.TryParse(local, out var l))
            return r > l;
        return string.Compare(remote, local, StringComparison.OrdinalIgnoreCase) > 0;
    }

    protected override void OnResume()
    {
        DeviceDisplay.KeepScreenOn = globalInstance.KeepAwake;
        globalInstance.RefreshIncompleteDownloadState();
        if (DownloadPopupPresenter.IsDownloadRecoveryPending())
            DownloadPopupPresenter.ShowWithRetry();
        MainThread.BeginInvokeOnMainThread(async () => await RecoverUiAfterBackgroundAsync());
    }

    async Task RecoverUiAfterBackgroundAsync()
    {
        var homeModel = Shell.Current?.CurrentPage?.BindingContext as NumSearchViewModel;
        var wasStuckLoading = homeModel?.IsBusy == true;
        homeModel?.ApplyInitComplete();

        if (!globalInstance.InitComplete ||
            globalInstance.ActiveHymn == null ||
            globalInstance.HymnList == null ||
            globalInstance.HymnList.Count == 0)
            return;

        var currentRoute = Shell.Current?.CurrentState?.Location?.OriginalString ?? string.Empty;
        if (currentRoute.Contains(Routes.READ))
            return;

        if (wasStuckLoading)
            await NavigateToStartupReaderAsync();
    }

    private async void GlobalInstance_InitFinished(object sender, EventArgs e)
    {
        if (sender is string tag && tag == "sync")
            return;

        if (globalInstance.ActiveHymn == null || globalInstance.HymnList == null || globalInstance.HymnList.Count == 0)
            return;

#if ANDROID
        var pending = MobiHymn4.MainActivity.PendingHymnNumber;
        if (!string.IsNullOrEmpty(pending))
        {
            MobiHymn4.MainActivity.ConsumePendingHymnNumber();
            NavigateToHymn(pending);
            return;
        }
#endif

        await NavigateToStartupReaderAsync();
    }

    public static void NavigateToHymn(string number)
    {
        var globals = Globals.Instance;
        var hymn = globals.HymnList?[number];
        if (hymn == null)
            return;

        globals.ActiveHymn = hymn;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await Shell.Current.GoToAsync($"//{Routes.READ}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Deep link navigation failed: {ex.Message}");
            }
        });
    }

    async Task NavigateToStartupReaderAsync()
    {
        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var currentRoute = Shell.Current?.CurrentState?.Location?.OriginalString ?? string.Empty;
                if (currentRoute.Contains(Routes.READ))
                    return;

                if (DeviceInfo.Platform == DevicePlatform.iOS)
                    await Task.Delay(500);

                await Shell.Current.GoToAsync($"//{Routes.READ}");
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Startup navigation to reader failed: {ex.Message}");
        }
    }
}
