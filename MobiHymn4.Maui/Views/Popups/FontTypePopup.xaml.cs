using System;
using System.Collections.Generic;

using CommunityToolkit.Maui.Views;

using Microsoft.Maui.Controls;

using MobiHymn4.Utils;

namespace MobiHymn4.Views.Popups
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
            Close(null);
        }

        void ListView_ItemTapped(System.Object sender, Microsoft.Maui.Controls.ItemTappedEventArgs e)
        {
            globalInstance.ActiveFont = (string)e.Item;
        }
    }
}

