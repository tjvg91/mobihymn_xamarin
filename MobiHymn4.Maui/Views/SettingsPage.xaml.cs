using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FontAwesome;
using MobiHymn4.Services;
using MobiHymn4.Utils;
using CommunityToolkit.Maui.Views;

using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;

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
            if (!await EnsureConnectedAsync())
                return;

            var sure = await DisplayAlert(
                "Resync All",
                "This will re-download all hymns and may take a while. Continue?",
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

            var label = $"Re-download lyrics for {input} from the server?";

            var sure = await DisplayAlert("Resync Custom", label, "Yes", "No");
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
            // Android crashes if a popup opens while the alert dialog is still closing.
            await Task.Delay(250);

            var downloadPopup = DownloadPopupPresenter.CreateAndTrack();
            var work = action();

            // Do not await ShowPopupAsync — on Android it completes only when the popup closes,
            // which would block the download/sync work from ever starting.
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
                var imm = (Android.Views.InputMethods.InputMethodManager?)
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
