using System;

using Xamarin.Forms;
using Xamarin.Essentials;

using MobiHymn2.Utils;

namespace MobiHymn2.ViewModels
{
	public class DownloadViewModel : MvvmHelpers.BaseViewModel
    {
        private Globals globalInstance = Globals.Instance;
        public DownloadViewModel ()
		{
			IsConnected = HttpHelper.IsConnected();
            DownloadStatus = DownloadStatus.None;
            if (!IsConnected)
                SetNoInternet();

            Connectivity.ConnectivityChanged += Connectivity_ConnectivityChanged;
            globalInstance.DownloadStarted += GlobalInstance_DownloadStarted;
            globalInstance.DownloadProgressed += GlobalInstance_DownloadProgressed;
            globalInstance.DownloadError += GlobalInstance_DownloadError;
            globalInstance.InitFinished += GlobalInstance_InitFinished;
        }

        private string lottieIcon = Application.Current.UserAppTheme == OSAppTheme.Light ?
                                "no-internet-light" : "no-internet-dark";
        public string LottieIcon
        {
            get => lottieIcon;
            set
            {
                lottieIcon = value;
                SetProperty(ref lottieIcon, value, nameof(LottieIcon));
                OnPropertyChanged();
            }
        }

        private string message;
        public string Message
        {
            get => message;
            set
            {
                message = value;
                SetProperty(ref message, value, nameof(Message));
                OnPropertyChanged();
            }
        }

        private bool isConnected = true;
		public bool IsConnected
		{
			get => isConnected;
			set => isConnected = value;
		}

        private DownloadStatus downloadStatus = DownloadStatus.Started;
        public DownloadStatus DownloadStatus
        {
            get => downloadStatus;
            set => downloadStatus = value;
        }

        private Action todo;
        public Action Todo
        {
            get => todo;
            set => todo = value;
        }

        private void GlobalInstance_DownloadStarted(object sender, EventArgs e)
        {
            LottieIcon = "download";
            Message = "Initializing...";
        }

        private void Connectivity_ConnectivityChanged(object sender, ConnectivityChangedEventArgs e)
        {
            bool prevState = IsConnected;
            IsConnected = e.NetworkAccess == NetworkAccess.Internet;
            if (IsConnected && !prevState) Todo();
            else if(!IsConnected)
            {
                SetNoInternet();
                globalInstance.CTS.Cancel();
            }
        }

        private void GlobalInstance_InitFinished(object sender, EventArgs e)
        {
            DownloadStatus = DownloadStatus.Success;
            LottieIcon = "done";
            Message = (string)sender == "sync" ? "Re-sync successful." : "Resources downloaded successfully.";
        }

        private void GlobalInstance_DownloadError(object sender, EventArgs e)
        {
            DownloadStatus = DownloadStatus.Error;
            Message = (string)sender;
            if (Message.Contains("connect")) LottieIcon = "no-internet";
        }

        private void GlobalInstance_DownloadProgressed(object sender, EventArgs e)
        {
            DownloadStatus = DownloadStatus.Ongoing;
            LottieIcon = "download";
            Message = (string)sender;
        }

        private void SetNoInternet()
        {
            LottieIcon = Application.Current.UserAppTheme == OSAppTheme.Light ?
                                "no-internet-light" : "no-internet-dark";
            Message = "Please connect to download resources.";
        }
    }
}


