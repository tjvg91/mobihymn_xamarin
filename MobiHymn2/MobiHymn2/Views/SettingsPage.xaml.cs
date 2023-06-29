using System;
using System.Collections.Generic;
using Microsoft.AppCenter.Distribute;
using MobiHymn2.Utils;
using Xamarin.CommunityToolkit.Extensions;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace MobiHymn2.Views
{
    public partial class SettingsPage : ContentPage
    {
        private Globals globalInstance = Globals.Instance;

        public SettingsPage()
        {
            InitializeComponent();

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

        void swResync_Clicked(System.Object sender, System.EventArgs e)
        {
            if (Connectivity.NetworkAccess == NetworkAccess.None)
                Globals.ShowToastPopup(this, Application.Current.UserAppTheme == OSAppTheme.Light ?
                                "no-internet-light" : "no-internet-dark", "Please connect to download resources");
            else
            {
                Popups.DownloadPopup downloadPopup = new Popups.DownloadPopup
                {
                    IsLightDismissEnabled = false
                };
                downloadPopup.Todo = SyncHymns;
                //downloadPopup.Dismissed += DownloadPopup_Dismissed;

                SyncHymns();
                Navigation.ShowPopup(downloadPopup);
            }
        }

        async void SyncHymns()
        {
            if (await globalInstance.DownloadReadHymns(true))
                globalInstance.OnInitFinished("sync");
        }

        void TapGestureRecognizer_Tapped(System.Object sender, System.EventArgs e)
        {
            Distribute.CheckForUpdate();
        }
    }
}

