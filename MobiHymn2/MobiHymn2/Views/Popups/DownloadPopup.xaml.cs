using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xamarin.CommunityToolkit.UI.Views;
using Xamarin.Essentials;
using Xamarin.Forms;
using MobiHymn2.Utils;
using MobiHymn2.ViewModels;

namespace MobiHymn2.Views.Popups
{	
	public partial class DownloadPopup : Popup
	{
        private Globals globalInstance = Globals.Instance;
        DownloadViewModel model;

        public Action Todo
        {
            get => model.Todo;
            set => model.Todo = value;
        }

        public DownloadPopup ()
		{
			InitializeComponent ();
            var mainDispalyInfo = DeviceDisplay.MainDisplayInfo;
            var width = (mainDispalyInfo.Width / mainDispalyInfo.Density) - (mainDispalyInfo.Width * 0.1);
            var height = 280;
            Size = new Size(width, height);

            model = this.BindingContext as DownloadViewModel;

            globalInstance.InitFinished += GlobalInstance_InitFinished;
            this.Opened += DownloadPopup_Opened;
		}

        private void DownloadPopup_Opened(object sender, PopupOpenedEventArgs e)
        {
            if(model.DownloadStatus == DownloadStatus.Success)
            {
                model = new DownloadViewModel();
            }
        }

        private async void GlobalInstance_InitFinished(object sender, EventArgs e)
        {
            await Task.Delay(2000);
            Dismiss(DownloadStatus.Success);
        }
    }
}

