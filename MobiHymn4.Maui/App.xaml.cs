using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using MobiHymn4.Models;
using MobiHymn4.Services;
using MobiHymn4.Utils;
using MobiHymn4.ViewModels;
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
            DeviceDisplay.KeepScreenOn = globalInstance.KeepAwake;

            if (!fromFirebaseNotif)
                QueueFirebaseInit();

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
        });
    }

    async Task InitFirebaseAsync()
    {
        globalInstance.IsFetchingSyncDetails = true;
        await fbHelper.LoginWithEmailPassword("tim.gandionco@gmail.com", "TLmSIsnw231");

        var deviceVersion = int.Parse(Preferences.Get(PreferencesVar.RESYNC_VERSION, "0"));
        globalInstance.ResyncDetails.AddRange(
            await fbInstance.RetrieveSyncChangesFrom(deviceVersion));

        globalInstance.IsFetchingSyncDetails = false;
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

        await NavigateToStartupReaderAsync();
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
