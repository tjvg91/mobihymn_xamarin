using System;
using System.Collections.Generic;
using Xamarin.CommunityToolkit.UI.Views;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace MobiHymn4.Views.Popups
{	
	public partial class SyncPopup : Popup
    {	
		public SyncPopup ()
		{
			InitializeComponent ();

            var mainDispalyInfo = DeviceDisplay.MainDisplayInfo;
            var width = Math.Min(450, (mainDispalyInfo.Width / mainDispalyInfo.Density) - (mainDispalyInfo.Width * 0.1));
            var height = DeviceInfo.Idiom == DeviceIdiom.Tablet ? 340 : 300;
            Size = new Size(width, height);
        }

        void btnLater_Clicked(System.Object sender, System.EventArgs e)
        {
            Dismiss(null);
        }

        void btnResync_Clicked(System.Object sender, System.EventArgs e)
        {
            Dismiss("sync");
        }
    }
}

