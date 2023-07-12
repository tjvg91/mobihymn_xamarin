using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MobiHymn4.Models;
using MobiHymn4.Services;
using MobiHymn4.Utils;
using MobiHymn4.ViewModels;
using Xamarin.CommunityToolkit.Extensions;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace MobiHymn4.Views
{
    public partial class SettingsPage : ContentPage
    {
        private Globals globalInstance = Globals.Instance;

        CancellationTokenSource source = new CancellationTokenSource();

        public SettingsPage()
        {
            InitializeComponent();
            if (globalInstance.IsFetchingSyncDetails) RotateBusy();
            globalInstance.IsFetchingSyncDetailsChanged += GlobalInstance_IsFetchingSyncDetailsChanged;
        }

        private void GlobalInstance_IsFetchingSyncDetailsChanged(object sender, EventArgs e)
        {
            if (!(bool)sender)
                source.Cancel();
        }

        void swDarkMode_Toggled(System.Object sender, Xamarin.Forms.ToggledEventArgs e)
        {
            globalInstance.DarkMode = e.Value;
        }

        void swKeepAwake_Toggled(System.Object sender, Xamarin.Forms.ToggledEventArgs e)
        {
            globalInstance.KeepAwake = e.Value;
        }

        void swOrientationLock_Toggled(System.Object sender, Xamarin.Forms.ToggledEventArgs e)
        {
            globalInstance.IsOrientationLocked = e.Value;
        }

        async void swResync_Clicked(System.Object sender, System.EventArgs e)
        {
            if (Connectivity.NetworkAccess == NetworkAccess.None)
                Globals.ShowToastPopup(Application.Current.UserAppTheme == OSAppTheme.Light ?
                                "no-internet-light" : "no-internet-dark", "Please connect to download resources");
            else
            {
                var sure = await DisplayAlert("Sync?", "This will sync all hymns and take time. Are you sure you want to continue?",
                                "Yes", "No");

                if (sure)
                {
                    Popups.DownloadPopup downloadPopup = new Popups.DownloadPopup
                    {
                        IsLightDismissEnabled = false
                    };
                    downloadPopup.Todo = ForceSyncHymns;
                    //downloadPopup.Dismissed += DownloadPopup_Dismissed;

                    ForceSyncHymns();
                    Navigation.ShowPopup(downloadPopup);
                }
            }
        }

        async void btnResync_Clicked(System.Object sender, System.EventArgs e)
        {
            if (Connectivity.NetworkAccess == NetworkAccess.None)
                Globals.ShowToastPopup(Application.Current.UserAppTheme == OSAppTheme.Light ?
                                "no-internet-light" : "no-internet-dark", "Please connect to download resources");
            else
            {
                var sure = await DisplayAlert("Sync?", "Are you sure you want to sync?",
                                "Yes", "No");

                if(sure)
                {
                    Popups.DownloadPopup downloadPopup = new Popups.DownloadPopup
                    {
                        IsLightDismissEnabled = false
                    };
                    downloadPopup.Todo = ForceSyncHymns;
                    //downloadPopup.Dismissed += DownloadPopup_Dismissed;

                    SyncHymns();
                    Navigation.ShowPopup(downloadPopup);
                }
            }
        }

        async void ForceSyncHymns()
        {
            if (await globalInstance.DownloadReadHymns(true))
                globalInstance.OnInitFinished("sync");
        }

        async void SyncHymns()
        {
            if (await globalInstance.ResyncHymns())
            {
                globalInstance.OnInitFinished("sync");
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

