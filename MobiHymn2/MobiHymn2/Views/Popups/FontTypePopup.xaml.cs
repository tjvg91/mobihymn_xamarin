using System;
using System.Collections.Generic;

using Xamarin.CommunityToolkit.UI.Views;
using Xamarin.Essentials;
using Xamarin.Forms;

using MobiHymn2.Utils;

namespace MobiHymn2.Views.Popups
{	
	public partial class FontTypePopup : Popup
    {
        private Globals globalInstance = Globals.Instance;
        public FontTypePopup ()
		{
			InitializeComponent ();

            var mainDispalyInfo = DeviceDisplay.MainDisplayInfo;
            var width = (mainDispalyInfo.Width / mainDispalyInfo.Density) - (mainDispalyInfo.Width * 0.1);
            var height = 280;
            Size = new Size(width, height);
        }

        void Button_Clicked(System.Object sender, System.EventArgs e)
        {
            Dismiss(null);
        }

        void ListView_ItemTapped(System.Object sender, Xamarin.Forms.ItemTappedEventArgs e)
        {
            globalInstance.ActiveFont = (string)e.Item;
        }
    }
}

