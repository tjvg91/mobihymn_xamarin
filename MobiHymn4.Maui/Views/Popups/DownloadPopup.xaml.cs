using System;
using System.Threading.Tasks;
using CommunityToolkit.Maui.Views;

using Microsoft.Maui.Controls;
using MobiHymn4.Utils;
using MobiHymn4.ViewModels;

namespace MobiHymn4.Views.Popups
{	
    public partial class DownloadPopup : Popup
	{
        private Globals globalInstance = Globals.Instance;
        DownloadViewModel model;
        private bool isClosed;

        public Action Todo
        {
            get => model?.Todo;
            set
            {
                if (model != null)
                    model.Todo = value;
            }
        }

        public DownloadPopup ()
		{
			InitializeComponent ();
            var mainDispalyInfo = DeviceDisplay.MainDisplayInfo;
            var width = Math.Min((mainDispalyInfo.Width / mainDispalyInfo.Density) - (mainDispalyInfo.Width * 0.1), 400);
            var height = DeviceInfo.Idiom == DeviceIdiom.Tablet ? 320 : 280;
            Size = new Size(width, height);

            model = BindingContext as DownloadViewModel;

            globalInstance.InitFinished += GlobalInstance_InitFinished;
            Opened += DownloadPopup_Opened;
            Closed += DownloadPopup_Closed;
		}

        private void DownloadPopup_Opened(object sender, EventArgs e)
        {
            if (model?.DownloadStatus == DownloadStatus.Success)
            {
                model.Detach();
                model = new DownloadViewModel();
                BindingContext = model;
            }
        }

        private void DownloadPopup_Closed(object sender, PopupClosedEventArgs e)
        {
            globalInstance.InitFinished -= GlobalInstance_InitFinished;
            model?.Detach();
        }

        private async void GlobalInstance_InitFinished(object sender, EventArgs e)
        {
            if (isClosed) return;
            await Task.Delay(2000);
            SafeClose();
        }

        private void SafeClose()
        {
            if (isClosed) return;
            isClosed = true;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try { Close(null); } catch { }
            });
        }
    }
}
