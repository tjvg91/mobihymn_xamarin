using System;
using System.Linq;
using System.Threading.Tasks;
using FontAwesome;
using MobiHymn4.Utils;
using MobiHymn4.ViewModels;
using MobiHymn4.Views.Popups;
using CommunityToolkit.Maui.Views;

using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
#if ANDROID
using Microsoft.Maui.Platform;
#endif

namespace MobiHymn4.Views
{
    public partial class SettingsPage : ContentPage
    {
        private Globals globalInstance = Globals.Instance;

        public SettingsPage()
        {
            InitializeComponent();
            entResyncHymn.HandlerChanged += (_, _) => ApplyResyncInputAccent();
            UpdateResyncIcons();
        }

        private void UpdateResyncIcons()
        {
            var iconColor = Application.Current?.RequestedTheme == AppTheme.Dark
                ? (Color)Application.Current.Resources["Primary"]
                : (Color)Application.Current.Resources["PrimaryText"];

            btnResyncAll.Source = CreateSyncIcon(iconColor);
            btnResyncMissing.Source = CreateSyncIcon(iconColor);
            btnResyncCustom.Source = CreateSyncIcon(iconColor);
        }

        private static FontImageSource CreateSyncIcon(Color color) => new()
        {
            FontFamily = "FAS",
            Glyph = FontAwesomeIcons.Sync,
            Size = 18,
            Color = color,
        };

        protected override void OnAppearing()
        {
            base.OnAppearing();
            UpdateResyncIcons();
            ApplyResyncInputAccent();
            if (!globalInstance.IsFetchingSyncDetails)
                _ = globalInstance.RefreshCatalogDiffAsync();
            _ = globalInstance.RefreshMissingHymnCountAsync();
        }

        private void ApplyResyncInputAccent()
        {
#if ANDROID
            if (entResyncHymn?.Handler?.PlatformView is not Android.Widget.EditText editText)
                return;

            editText.Background = null;
            editText.SetBackgroundColor(Android.Graphics.Color.Transparent);

            var lineColor = Application.Current?.RequestedTheme == AppTheme.Dark
                ? ((Color)Application.Current.Resources["GrayLight"]).ToPlatform()
                : ((Color)Application.Current.Resources["Gray"]).ToPlatform();
            editText.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(lineColor);
#endif
        }

        async void btnViewChanges_Clicked(object sender, EventArgs e)
        {
            AcknowledgeResyncBadge();
            var diff = globalInstance.PendingCatalogDiff;
            if (diff == null || diff.ChangeCount == 0)
                return;

            if (BindingContext is not SettingsViewModel model)
                return;

            model.IsOpeningChangesView = true;
            try
            {
                await Task.Yield();

                var popup = new CatalogDiffPopup();
                await popup.BindAsync(diff);
                model.IsOpeningChangesView = false;

                var result = await this.ShowPopupAsync(popup);
                if (result is string action
                    && string.Equals(action, CatalogDiffPopup.ResultSync, StringComparison.Ordinal))
                {
                    await SyncPendingChangesAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"View changes failed: {ex}");
                await DisplayAlert(
                    "View Changes",
                    "Could not open the change list. Try again later.",
                    "OK");
            }
            finally
            {
                model.IsOpeningChangesView = false;
            }
        }

        async Task SyncPendingChangesAsync()
        {
            var diff = globalInstance.PendingCatalogDiff;
            if (diff == null || diff.ChangeCount == 0 || !await EnsureConnectedAsync())
                return;

            var sure = await DisplayAlert(
                "Sync Changes",
                $"Apply changes to {diff.ChangeCount} hymn{(diff.ChangeCount == 1 ? string.Empty : "s")}?",
                "Sync",
                "Cancel");
            if (!sure)
                return;

            await ShowDownloadPopupAndRun(
                async () => await globalInstance.ApplyPendingCatalogChangesAsync());
        }

        void ResyncSection_Tapped(object sender, TappedEventArgs e) =>
            AcknowledgeResyncBadge();

        void AcknowledgeResyncBadge()
        {
            if (BindingContext is SettingsViewModel model)
                model.AcknowledgeResyncBadge();
        }

        void swDarkMode_Toggled(System.Object sender, Microsoft.Maui.Controls.ToggledEventArgs e)
        {
            if (globalInstance.DarkMode != e.Value)
            {
                globalInstance.DarkMode = e.Value;
                globalInstance.SaveSettings();
            }
        }

        void swKeepAwake_Toggled(System.Object sender, Microsoft.Maui.Controls.ToggledEventArgs e)
        {
            if (globalInstance.KeepAwake != e.Value)
            {
                globalInstance.KeepAwake = e.Value;
                globalInstance.SaveSettings();
            }
        }

        void swOrientationLock_Toggled(System.Object sender, Microsoft.Maui.Controls.ToggledEventArgs e)
        {
            globalInstance.IsOrientationLocked = e.Value;
        }

        async void btnResyncAll_Clicked(object sender, EventArgs e)
        {
            AcknowledgeResyncBadge();
            if (!await EnsureConnectedAsync())
                return;

            var sure = await DisplayAlert(
                "Resync All",
                "This will re-download all hymns from the server. Continue?",
                "Yes",
                "No");
            if (!sure)
                return;

            await ShowDownloadPopupAndRun(RunForceSyncAsync);
        }

        async void btnResyncMissing_Clicked(object sender, EventArgs e)
        {
            if (!await EnsureConnectedAsync())
                return;

            if (globalInstance.IsFetchingSyncDetails || globalInstance.MissingHymnCount == 0)
                await globalInstance.RefreshMissingHymnCountAsync();

            if (globalInstance.MissingHymnCount == 0)
            {
                await DisplayAlert("Resync Missing", "All available hymns are already downloaded.", "OK");
                return;
            }

            var count = globalInstance.MissingHymnCount;
            var numbers = Globals.FormatMissingHymnNumberList(globalInstance.MissingHymnNumbers, maxListed: 16);
            var sure = await DisplayAlert(
                "Resync Missing",
                count == 1
                    ? $"Download hymn {numbers} that is available on the server but not on this device?"
                    : $"Download {count} hymns ({numbers}) that are available on the server but not on this device?",
                "Yes",
                "No");
            if (!sure)
                return;

            await ShowDownloadPopupAndRun(RunSyncMissingAsync);
        }

        async void btnResyncCustom_Clicked(object sender, EventArgs e)
        {
            var input = entResyncHymn?.Text?.Trim();
            if (string.IsNullOrEmpty(input))
            {
                await DisplayAlert("Resync Custom", "Enter one or more hymn numbers to re-sync.", "OK");
                return;
            }

            var numbers = Globals.ParseHymnNumberList(input).ToList();
            if (numbers.Count == 0)
            {
                await DisplayAlert(
                    "Resync Custom",
                    "Enter valid hymn numbers (e.g. 123, 77, 55-100).",
                    "OK");
                return;
            }

            if (!await EnsureConnectedAsync())
                return;

            var sure = await DisplayAlert(
                "Resync Custom",
                $"Re-download lyrics for {input} from the server?",
                "Yes",
                "No");
            if (!sure)
                return;

            var hymnInput = input;
            await ShowDownloadPopupAndRun(() => RunResyncCustomAsync(hymnInput));
        }

        async Task<bool> EnsureConnectedAsync()
        {
            if (HttpHelper.IsConnected())
                return true;

            Globals.ShowToastPopup(
                Application.Current.UserAppTheme == AppTheme.Light ? "no-internet-light" : "no-internet-dark",
                "Please connect to download resources");
            return false;
        }

        async Task ShowDownloadPopupAndRun(Func<Task> action)
        {
            DismissResyncKeyboard();
            await Task.Delay(250);

            var downloadPopup = DownloadPopupPresenter.CreateAndTrack();
            var work = action();

            if (Window != null)
            {
                _ = this.ShowPopupAsync(downloadPopup).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        if (DownloadPopupPresenter.IsPopupOpen)
                            DownloadPopupPresenter.ClearActivePopup();
                        System.Diagnostics.Debug.WriteLine(
                            $"ShowDownloadPopup failed: {t.Exception?.GetBaseException().Message}");
                    }
                }, TaskScheduler.Default);
            }
            else if (!DownloadPopupPresenter.TryShowOnPage(this))
            {
                DownloadPopupPresenter.ShowWithRetry(this);
            }

            await Task.Yield();

            try
            {
                await work;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Download action failed: {ex.Message}");
                globalInstance.OnDownloadError(ex.Message);
            }
        }

        void DismissResyncKeyboard()
        {
            entResyncHymn?.Unfocus();
#if ANDROID
            if (Platform.CurrentActivity?.CurrentFocus is Android.Views.View focusedView)
            {
                var imm = (Android.Views.InputMethods.InputMethodManager)
                    Platform.CurrentActivity.GetSystemService(Android.Content.Context.InputMethodService);
                imm?.HideSoftInputFromWindow(focusedView.WindowToken, 0);
                focusedView.ClearFocus();
            }
#endif
        }

        async Task RunResyncCustomAsync(string hymnNumbersInput)
        {
            if (!globalInstance.TryBeginDownloadOperation())
            {
                globalInstance.OnDownloadError("Another download is already in progress. Please wait and try again.");
                return;
            }

            try
            {
                if (await globalInstance.ResyncSelectedHymns(hymnNumbersInput))
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        if (entResyncHymn != null)
                            entResyncHymn.Text = string.Empty;
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ResyncSelectedHymns failed: {ex.Message}");
                globalInstance.OnDownloadError(ex.Message);
            }
            finally
            {
                globalInstance.EndDownloadOperation();
            }
        }

        async Task RunForceSyncAsync()
        {
            if (!globalInstance.TryBeginDownloadOperation())
            {
                globalInstance.OnDownloadError("Another download is already in progress. Please wait and try again.");
                return;
            }

            try
            {
                if (await globalInstance.DownloadReadHymns(true))
                    await globalInstance.FinishAfterDownloadAsync(isUserSync: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ForceSyncHymns failed: {ex.Message}");
                globalInstance.OnDownloadError(ex.Message);
            }
            finally
            {
                globalInstance.EndDownloadOperation();
            }
        }

        async Task RunSyncMissingAsync()
        {
            if (!globalInstance.TryBeginDownloadOperation())
            {
                globalInstance.OnDownloadError("Another download is already in progress. Please wait and try again.");
                return;
            }

            try
            {
                var countBefore = globalInstance.HymnList?.Count ?? 0;
                if (await globalInstance.DownloadMissingReadHymns())
                {
                    var added = (globalInstance.HymnList?.Count ?? 0) - countBefore;
                    if (added == 0)
                    {
                        globalInstance.OnDownloadError("All available hymns are already downloaded.");
                        _ = globalInstance.RefreshMissingHymnCountAsync();
                        return;
                    }

                    await globalInstance.FinishAfterDownloadAsync(isUserSync: true);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DownloadMissingReadHymns failed: {ex.Message}");
                globalInstance.OnDownloadError(ex.Message);
            }
            finally
            {
                globalInstance.EndDownloadOperation();
            }
        }
    }
}
