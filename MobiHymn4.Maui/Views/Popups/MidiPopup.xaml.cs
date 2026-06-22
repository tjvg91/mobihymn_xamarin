using System;
using System.Collections.Generic;
using CommunityToolkit.Maui.Views;

using Microsoft.Maui.Controls;

namespace MobiHymn4.Views.Popups
{	
	public partial class MidiPopup : Popup
	{	
		public MidiPopup ()
		{
			InitializeComponent ();

            var mainDispalyInfo = DeviceDisplay.MainDisplayInfo;
            var width = (mainDispalyInfo.Width / mainDispalyInfo.Density) - (mainDispalyInfo.Width * 0.1);
            var height = 190;
            this.Size = new Size(width, height);
        }
	}
}

