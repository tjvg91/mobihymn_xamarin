using System;
using System.Collections.Generic;

using MobiHymn2.Utils;
using Xamarin.CommunityToolkit.Extensions;
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
            Popups.DownloadPopup downloadPopup = new Popups.DownloadPopup
            {
                IsLightDismissEnabled = false
            };
            downloadPopup.Todo = SyncHymns;
            //downloadPopup.Dismissed += DownloadPopup_Dismissed;
            
            SyncHymns();
            Navigation.ShowPopup(downloadPopup);
        }

        async void SyncHymns()
        {
            if (await globalInstance.DownloadHymns(true))
                globalInstance.OnInitFinished("sync");
        }
    }
}

