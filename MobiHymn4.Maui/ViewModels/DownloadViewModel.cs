using System;

using Microsoft.Maui.Controls;


using MobiHymn4.Utils;

namespace MobiHymn4.ViewModels
{
	public class DownloadViewModel : MvvmHelpers.BaseViewModel
    {
        private Globals globalInstance = Globals.Instance;
        public DownloadViewModel ()
		{
			IsConnected = HttpHelper.IsConnected();
            if (IsConnected)
            {
                LottieIcon = "download";
                DownloadStatus = DownloadStatus.Started;
                Message = "Initializing…";
            }
            else
            {
                DownloadStatus = DownloadStatus.Error;
                SetNoInternet();
            }

            Connectivity.ConnectivityChanged += Connectivity_ConnectivityChanged;
            globalInstance.DownloadStarted += GlobalInstance_DownloadStarted;
            globalInstance.DownloadProgressed += GlobalInstance_DownloadProgressed;
            globalInstance.DownloadError += GlobalInstance_DownloadError;
            globalInstance.InitFinished += GlobalInstance_InitFinished;

            if (globalInstance.IsDownloadUiActive)
            {
                LottieIcon = "download";
                DownloadStatus = string.IsNullOrEmpty(globalInstance.LastDownloadProgressMessage)
                    ? DownloadStatus.Started
                    : DownloadStatus.Ongoing;
                ApplyProgress(globalInstance.LastDownloadProgressMessage ?? "Downloading…");
            }
            else if (globalInstance.HasIncompleteDownloadOnDisk)
            {
                LottieIcon = "download";
                DownloadStatus = DownloadStatus.Started;
                Message = "Resuming download…";
            }
        }

        public void Detach()
        {
            Connectivity.ConnectivityChanged -= Connectivity_ConnectivityChanged;
            globalInstance.DownloadStarted -= GlobalInstance_DownloadStarted;
            globalInstance.DownloadProgressed -= GlobalInstance_DownloadProgressed;
            globalInstance.DownloadError -= GlobalInstance_DownloadError;
            globalInstance.InitFinished -= GlobalInstance_InitFinished;
        }

        private string lottieIcon = "download";
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

        private double progress;
        public double Progress
        {
            get => progress;
            set => SetProperty(ref progress, value);
        }

        private string progressText = string.Empty;
        public string ProgressText
        {
            get => progressText;
            set => SetProperty(ref progressText, value);
        }

        private bool showProgress;
        public bool ShowProgress
        {
            get => showProgress;
            set => SetProperty(ref showProgress, value);
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
            set
            {
                if (SetProperty(ref downloadStatus, value))
                    OnPropertyChanged(nameof(ShowDurationHint));
            }
        }

        public bool ShowDurationHint =>
            downloadStatus != DownloadStatus.Success && downloadStatus != DownloadStatus.Error;

        private Action todo;
        public Action Todo
        {
            get => todo;
            set => todo = value;
        }

        private void GlobalInstance_DownloadStarted(object sender, EventArgs e)
        {
            LottieIcon = "download";
            DownloadStatus = DownloadStatus.Started;
            ClearProgress();
            if (string.IsNullOrWhiteSpace(Message) ||
                Message.Equals("Initializing…", StringComparison.Ordinal) ||
                Message.Equals("Initializing...", StringComparison.Ordinal))
            {
                Message = "Initializing…";
            }
        }

        private void Connectivity_ConnectivityChanged(object sender, ConnectivityChangedEventArgs e)
        {
            var wasConnected = IsConnected;
            IsConnected = HttpHelper.IsConnected();

            if (IsConnected && !wasConnected && Todo != null && !globalInstance.InitInProgress)
                Todo();
            else if (!IsConnected && e.NetworkAccess == NetworkAccess.None)
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
            ClearProgress();
        }

        private void GlobalInstance_DownloadError(object sender, EventArgs e)
        {
            DownloadStatus = DownloadStatus.Error;
            Message = (string)sender;
            ClearProgress();
            if (Message.Contains("connect"))
                LottieIcon = Application.Current.UserAppTheme == AppTheme.Light ?
                    "no-internet-light" : "no-internet-dark";
        }

        private void GlobalInstance_DownloadProgressed(object sender, EventArgs e)
        {
            DownloadStatus = DownloadStatus.Ongoing;
            LottieIcon = "download";
            ApplyProgress((string)sender);
        }

        void ApplyProgress(string report)
        {
            var current = globalInstance.DownloadProgressCurrent;
            var total = globalInstance.DownloadProgressTotal;
            if (current >= 0 && total > 0)
            {
                Progress = Math.Clamp((double)current / total, 0, 1);
                ProgressText = $"{current} / {total}";
                ShowProgress = true;

                var text = report ?? string.Empty;
                if (text.StartsWith("Saving", StringComparison.OrdinalIgnoreCase)
                    || text.StartsWith("Saved", StringComparison.OrdinalIgnoreCase)
                    || text.StartsWith("Finishing", StringComparison.OrdinalIgnoreCase))
                    Message = text.StartsWith("Saved", StringComparison.OrdinalIgnoreCase) ? "Saved" : "Saving…";
                else if (text.StartsWith("Syncing", StringComparison.OrdinalIgnoreCase))
                    Message = "Syncing…";
                else
                    Message = "Downloading…";
            }
            else
            {
                ClearProgress();
                Message = report;
            }
        }

        void ClearProgress()
        {
            Progress = 0;
            ProgressText = string.Empty;
            ShowProgress = false;
        }

        private void SetNoInternet()
        {
            LottieIcon = Application.Current.UserAppTheme == AppTheme.Light ?
                                "no-internet-light" : "no-internet-dark";
            Message = "Please connect to download resources.";
            ClearProgress();
        }
    }
}
