using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.AppCompat.App;
using MobiHymn4.Utils;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace MobiHymn4;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "mobihymn",
    DataHost = "hymn")]
public class MainActivity : MauiAppCompatActivity
{
    public static string PendingHymnNumber { get; private set; }

    public static void ConsumePendingHymnNumber() => PendingHymnNumber = null;

    protected override void OnCreate(Bundle savedInstanceState)
    {
        AppCompatDelegate.DefaultNightMode = AppCompatDelegate.ModeNightNo;
        base.OnCreate(savedInstanceState);

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            _ = RequestNotificationPermissionAsync();

        HandleNotificationIntent(Intent);
        HandleDeepLinkIntent(Intent, alreadyLoaded: false);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        HandleNotificationIntent(intent);
        HandleDeepLinkIntent(intent, alreadyLoaded: true);
    }

    static void HandleDeepLinkIntent(Intent? intent, bool alreadyLoaded)
    {
        var uri = intent?.Data;
        if (uri?.Scheme != "mobihymn" || uri.Host != "hymn")
            return;

        var number = uri.LastPathSegment;
        if (string.IsNullOrEmpty(number))
            return;

        if (alreadyLoaded)
            App.NavigateToHymn(number);
        else
            PendingHymnNumber = number;
    }

    static void HandleNotificationIntent(Intent? intent)
    {
        if (intent?.GetBooleanExtra(DownloadForegroundService.ExtraShowDownloadPopup, false) != true)
            return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var globals = Globals.Instance;
            globals.RefreshIncompleteDownloadState();

            for (var i = 0; i < 20; i++)
            {
                await Task.Delay(250);
                await EnsureReaderVisibleAsync();

                var page = Shell.Current?.CurrentPage as Page;
                if (page != null && DownloadPopupPresenter.TryShowOnPage(page))
                    break;

                if (DownloadPopupPresenter.IsPopupOpen)
                    break;
            }

            if (!DownloadPopupPresenter.IsPopupOpen)
                DownloadPopupPresenter.ShowWithRetry(Shell.Current?.CurrentPage as Page);

            globals.TryResumeInitAfterRelaunch();
        });
    }

    static async Task EnsureReaderVisibleAsync()
    {
        try
        {
            if (Shell.Current == null)
                return;

            var currentRoute = Shell.Current.CurrentState?.Location?.OriginalString ?? string.Empty;
            if (currentRoute.Contains(Routes.READ))
                return;

            await Shell.Current.GoToAsync($"//{Routes.READ}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Notification navigation failed: {ex.Message}");
        }
    }

    static async Task RequestNotificationPermissionAsync()
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
            if (status != PermissionStatus.Granted)
                await Permissions.RequestAsync<Permissions.PostNotifications>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Notification permission request failed: {ex.Message}");
        }
    }
}
