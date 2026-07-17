using System;
using System.Threading.Tasks;
using FontAwesome;
using MobiHymn4.Utils;
using MobiHymn4.ViewModels;
using MobiHymn4.Views.Popups;
using CommunityToolkit.Maui.Views;

using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MobiHymn4.Views
{
    public partial class SettingsPage : ContentPage
    {
        private Globals globalInstance = Globals.Instance;

        public SettingsPage()
        {
            InitializeComponent();
            UpdateResyncIcons();
        }

        private void UpdateResyncIcons()
        {
            var iconColor = Application.Current?.RequestedTheme == AppTheme.Dark
                ? (Color)Application.Current.Resources["Primary"]
                : (Color)Application.Current.Resources["PrimaryText"];

            btnResyncAll.Source = CreateSyncIcon(iconColor);
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
            if (!globalInstance.IsFetchingSyncDetails)
                _ = globalInstance.RefreshCatalogDiffAsync();
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
                // Let the loader paint before building the diff list.
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
    }
}
