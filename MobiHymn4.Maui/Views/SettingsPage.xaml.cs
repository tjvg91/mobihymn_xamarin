using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FontAwesome;
using MobiHymn4.Elements;
using MobiHymn4.Models;
using MobiHymn4.Services;
using MobiHymn4.Utils;
using MobiHymn4.ViewModels;
using CommunityToolkit.Maui.Views;

using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;

namespace MobiHymn4.Views
{
    public partial class SettingsPage : ContentPage
    {
        private Globals globalInstance = Globals.Instance;
        private SettingsViewModel model;
        private bool syncDetailsBuildQueued;
        private bool syncDetailsBuilt;

        CancellationTokenSource source = new CancellationTokenSource();

        public SettingsPage()
        {
            InitializeComponent();
            model = BindingContext as SettingsViewModel;
            entResyncHymn.HandlerChanged += (_, _) => ApplyResyncInputAccent();
            UpdateResyncIcon();
            if (globalInstance.IsFetchingSyncDetails) RotateBusy();
            globalInstance.IsFetchingSyncDetailsChanged += GlobalInstance_IsFetchingSyncDetailsChanged;
            if (model != null)
                model.PropertyChanged += Model_PropertyChanged;
        }

        private void UpdateResyncIcon()
        {
            var iconColor = Application.Current?.RequestedTheme == AppTheme.Dark
                ? (Color)Application.Current.Resources["Primary"]
                : (Color)Application.Current.Resources["PrimaryText"];

            if (swResync != null)
                swResync.Source = CreateSyncIcon(iconColor);

            if (btnResyncSingle != null)
                btnResyncSingle.Source = CreateSyncIcon(iconColor);
        }

        private static FontImageSource CreateSyncIcon(Color color) => new()
        {
            FontFamily = "FAS",
            Glyph = FontAwesomeIcons.Sync,
            Size = 18,
            Color = color,
        };

        private void GlobalInstance_IsFetchingSyncDetailsChanged(object sender, EventArgs e)
        {
            if (!(bool)sender)
                source.Cancel();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            UpdateResyncIcon();
            ApplyResyncInputAccent();
            QueueSyncDetailsBuild();
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

        private void Model_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsViewModel.ShowSyncs))
            {
                syncDetailsBuilt = false;
                syncDetailsHost.Children.Clear();
                QueueSyncDetailsBuild();
            }
        }

        private void QueueSyncDetailsBuild()
        {
            if (syncDetailsBuilt || syncDetailsBuildQueued || model?.ShowSyncs != true)
                return;

            syncDetailsBuildQueued = true;
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(200), BuildSyncDetails);
        }

        private void BuildSyncDetails()
        {
            syncDetailsBuildQueued = false;

            if (syncDetailsBuilt || model?.ShowSyncs != true)
                return;

            model.EnsureResyncInitialized();
            syncDetailsBuilt = true;
            syncDetailsHost.IsVisible = true;
            syncDetailsHost.Children.Clear();

            syncDetailsHost.Children.Add(new Label
            {
                Text = "Hymn Updates:",
                TextColor = Application.Current.UserAppTheme == AppTheme.Dark
                    ? (Color)Application.Current.Resources["White"]
                    : (Color)Application.Current.Resources["PrimaryText"]
            });

            AddTimelineIfVisible(model.ShowCreate, model.ResyncCreateList);
            AddTimelineIfVisible(model.ShowUpdate, model.ResyncUpdateList);
            AddTimelineIfVisible(model.ShowDelete, model.ResyncDeleteList);

            var footer = new HorizontalStackLayout { HorizontalOptions = LayoutOptions.End };
            footer.Children.Add(new Button
            {
                Text = "Sync",
                Padding = new Thickness(30, 10),
                Command = new Command(() => btnResync_Clicked(this, EventArgs.Empty))
            });
            syncDetailsHost.Children.Add(footer);
        }

        private void AddTimelineIfVisible(bool isVisible, Timeline item)
        {
            if (!isVisible || item == null)
                return;

            syncDetailsHost.Children.Add(new TimelineItem
            {
                Item = item,
                Margin = 10
            });
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

        async void btnResyncSingle_Clicked(object sender, EventArgs e)
        {
            var input = entResyncHymn?.Text?.Trim();
            if (string.IsNullOrEmpty(input))
            {
                await DisplayAlert("Resync Hymn", "Enter one or more hymn numbers to re-sync.", "OK");
                return;
            }

            var numbers = Globals.ParseHymnNumberList(input).ToList();
            if (numbers.Count == 0)
            {
                await DisplayAlert(
                    "Resync Hymn",
                    "Enter valid hymn numbers separated by commas (e.g. 132, 77s, 801).",
                    "OK");
                return;
            }

            if (Connectivity.NetworkAccess == NetworkAccess.None)
            {
                Globals.ShowToastPopup(Application.Current.UserAppTheme == AppTheme.Light ?
                    "no-internet-light" : "no-internet-dark", "Please connect to download resources");
                return;
            }

            var label = numbers.Count == 1
                ? $"Re-download lyrics for hymn #{numbers[0]} from the server?"
                : $"Re-download lyrics for {numbers.Count} hymns ({string.Join(", ", numbers)}) from the server?";

            var sure = await DisplayAlert("Re-sync hymn(s)?", label, "Yes", "No");
            if (!sure)
                return;

            var downloadPopup = DownloadPopupPresenter.CreateAndTrack();
            downloadPopup.Todo = () => _ = RunResyncSelectedAsync(input);
            Navigation.ShowPopup(downloadPopup);
            await Task.Yield();
            await RunResyncSelectedAsync(input);
        }

        async Task RunResyncSelectedAsync(string hymnNumbersInput)
        {
            try
            {
                await globalInstance.ResyncSelectedHymns(hymnNumbersInput);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ResyncSelectedHymns failed: {ex.Message}");
                globalInstance.OnDownloadError(ex.Message);
            }
        }

        async void swResync_Clicked(System.Object sender, System.EventArgs e)
        {
            if (Connectivity.NetworkAccess == NetworkAccess.None)
                Globals.ShowToastPopup(Application.Current.UserAppTheme == AppTheme.Light ?
                                "no-internet-light" : "no-internet-dark", "Please connect to download resources");
            else
            {
                var sure = await DisplayAlert("Sync?", "This will sync all hymns and take time. Are you sure you want to continue?",
                                "Yes", "No");

                if (sure)
                {
                    var downloadPopup = DownloadPopupPresenter.CreateAndTrack();
                    downloadPopup.Todo = () => _ = RunForceSyncAsync();
                    Navigation.ShowPopup(downloadPopup);
                    await Task.Yield();
                    await RunForceSyncAsync();
                }
            }
        }

        async void btnResync_Clicked(System.Object sender, System.EventArgs e)
        {
            if (Connectivity.NetworkAccess == NetworkAccess.None)
                Globals.ShowToastPopup(Application.Current.UserAppTheme == AppTheme.Light ?
                                "no-internet-light" : "no-internet-dark", "Please connect to download resources");
            else
            {
                var sure = await DisplayAlert("Sync?", "Are you sure you want to sync?",
                                "Yes", "No");

                if(sure)
                {
                    var downloadPopup = DownloadPopupPresenter.CreateAndTrack();
                    downloadPopup.Todo = () => _ = RunSyncAsync();
                    Navigation.ShowPopup(downloadPopup);
                    await Task.Yield();
                    await RunSyncAsync();
                }
            }
        }

        async Task RunForceSyncAsync()
        {
            if (!globalInstance.TryBeginDownloadOperation())
                return;

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

        async Task RunSyncAsync()
        {
            try
            {
                if (await globalInstance.ResyncHymns())
                    await globalInstance.FinishAfterDownloadAsync(isUserSync: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SyncHymns failed: {ex.Message}");
                globalInstance.OnDownloadError(ex.Message);
            }
        }

        async void RotateBusy()
        {
            while (!source.IsCancellationRequested)
            {
                for (int i = 1; i < 7; i++)
                {
                    if (swResync.Rotation >= 360f) swResync.Rotation = 0;
                    await swResync.RotateTo(i * (360 / 6), 500, Easing.Linear);
                }
            }
        }
    }
}

