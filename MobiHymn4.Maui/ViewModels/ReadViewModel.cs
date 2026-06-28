
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

using MvvmHelpers;

using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Compatibility;

using MobiHymn4.Utils;
using MobiHymn4.Views;
using System.Windows.Input;
using MobiHymn4.Models;
using Microsoft.Maui.Networking;


namespace MobiHymn4.ViewModels
{
    public class ReadViewModel : BaseViewModel
    {
        private Globals globalInstance = Globals.Instance;
        private IPlayService playService;
        private string commandText = "Play";

        public event EventHandler OnBookmarked;
        public event EventHandler OnHymnChanged;
        public event EventHandler ConnectivityChanged;

        public ReadViewModel()
        {
            try
            {
                ActiveColor = globalInstance.ActiveReadTheme;
                ActiveColorText = globalInstance.ActiveThemeText ?? globalInstance.PrimaryText;
                ActiveFontSize = globalInstance.ActiveFontSize;
                ActiveFont = globalInstance.ActiveFont;
                ActiveAlignment = globalInstance.ActiveAlignment;
                HymnInputType = globalInstance.HymnInputType;

                globalInstance.ActiveReadThemeChanged += Globals_ActiveReadThemeChanged;
                globalInstance.ActiveHymnChanged += Globals_ActiveHymnChanged;
                globalInstance.InitFinished += GlobalInstance_InitFinished;
                globalInstance.DownloadStarted += GlobalInstance_DownloadStarted;
                globalInstance.DownloadProgressed += GlobalInstance_DownloadProgressed;
                globalInstance.DownloadError += GlobalInstance_DownloadError;
                globalInstance.ActiveAlignmentChanged += Globals_ActiveAlignmentChanged;
                globalInstance.ActiveFontSizeChanged += Globals_ActiveFontSizeChanged;
                globalInstance.ActiveFontChanged += Globals_ActiveFontChanged;
                globalInstance.ActiveLetterSpacingChanged += Globals_ActiveLetterSpacingChanged;
                globalInstance.ActiveLineSpacingChanged += Globals_ActiveLineSpacingChanged;
                globalInstance.HymnInputTypeChanged += GlobalInstance_HymnInputTypeChanged;
                globalInstance.BookmarksChanged += GlobalInstance_BookmarksChanged;
                globalInstance.SettingsLoaded += GlobalInstance_SettingsLoaded;

                LetterSpacing = 0;
                LineSpacing = 1;

                GroupKeys = ModifyBookmarks(globalInstance.BookmarkList ?? new ObservableRangeCollection<ShortHymn>()).Select((grp, count) => new GroupDisplay
                {
                    Name = grp.Key,
                    Count = grp.Count()
                }).ToObservableRangeCollection();

                DrawerHeight = GetDrawerHeight();

                RefreshReaderSettings();
                Title = "Hymn";
                Lyrics = string.Empty;
                BookmarkFont = "FAR";
                UpdateLoadingState();
                UpdateInternetNotice();

                Connectivity.ConnectivityChanged += Connectivity_ConnectivityChanged;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ReadViewModel initialization failed: {ex}");

                Title = "Hymn";
                Lyrics = string.Empty;
                BookmarkFont = "FAR";
                ActiveColor = Colors.White;
                ActiveColorText = globalInstance.PrimaryText;
                ActiveFontSize = 20;
                ActiveFont = DeviceInfo.Platform == DevicePlatform.Android ? "Roboto" : "SFPro";
                ActiveAlignment = TextAlignment.Start;
                HymnInputType = globalInstance.HymnInputType;
                LetterSpacing = 0;
                LineSpacing = 1;
                GroupKeys = new ObservableRangeCollection<GroupDisplay>();
                DrawerHeight = GetDrawerHeight();
            }
        }

        #region Events
        private void GlobalInstance_BookmarksChanged(object sender, EventArgs e)
        {
            BookmarkFont = globalInstance.ActiveHymn != null && globalInstance.IsBookmarked() ? "FAS" : "FAR";

            GroupKeys = ModifyBookmarks(globalInstance.BookmarkList ?? new ObservableRangeCollection<ShortHymn>()).Select((grp, count) => new GroupDisplay
            {
                Name = grp.Key,
                Count = grp.Count()
            }).ToObservableRangeCollection();
            DrawerHeight = GetDrawerHeight();
        }

        private void GlobalInstance_SettingsLoaded(object sender, EventArgs e) =>
            RefreshReaderSettings();

        private void GlobalInstance_HymnInputTypeChanged(object sender, EventArgs e)
        {
            HymnInputType = (Utils.InputType)sender;
        }

        private void Globals_ActiveFontChanged(object sender, EventArgs e)
        {
            ActiveFont = (string)sender;
        }

        private void Globals_ActiveLetterSpacingChanged(object sender, EventArgs e)
        {
            LetterSpacing = (double)sender;
        }

        private void Globals_ActiveLineSpacingChanged(object sender, EventArgs e)
        {
            LineSpacing = (double)sender;
        }

        private void Globals_ActiveFontSizeChanged(object sender, EventArgs e)
        {
            ActiveFontSize = (double)sender;
        }

        private void Globals_ActiveAlignmentChanged(object sender, EventArgs e)
        {
            ActiveAlignment = (TextAlignment)sender;
        }

        private void GlobalInstance_InitFinished(object sender, EventArgs e)
        {
            if (sender is string tag && tag == "sync")
            {
                UpdateLoadingState();
                return;
            }

            RefreshFromActiveHymn();
        }

        private void Globals_ActiveHymnChanged(object sender, EventArgs e)
        {
            var activeHymn = (Models.Hymn)sender;
            if (activeHymn == null)
                return;

            Title = "Hymn #" + activeHymn.Title;
            Lyrics = NormalizeLyrics(activeHymn.Lyrics);
            BookmarkFont = globalInstance.IsBookmarked() ? "FAS" : "FAR";

            if (OnHymnChanged != null) OnHymnChanged(activeHymn, EventArgs.Empty);
            UpdateLoadingState();
        }


        private void Globals_ActiveReadThemeChanged(object sender, EventArgs e)
        {
            ActiveColor = (Color)sender;
            ActiveColorText = globalInstance.ActiveThemeText;
        }

        void Connectivity_ConnectivityChanged(object sender, ConnectivityChangedEventArgs e)
        {
            UpdateInternetNotice();
            ConnectivityChanged?.Invoke(this, EventArgs.Empty);
        }

        public void UpdateInternetNotice()
        {
            ShowAudioInternetNotice = !HttpHelper.IsConnected();
            if (ShowAudioInternetNotice)
                ShowAudioNotFoundNotice = false;
        }

        public void SetAudioNotFound(bool notFound) =>
            ShowAudioNotFoundNotice = notFound && HttpHelper.IsConnected();

        #endregion

        #region Methods
        public void RefreshReaderSettings()
        {
            ActiveColor = globalInstance.ActiveReadTheme;
            ActiveColorText = globalInstance.ActiveThemeText ?? globalInstance.PrimaryText;
            ActiveFontSize = globalInstance.ActiveFontSize;
            ActiveFont = globalInstance.ActiveFont;
            ActiveAlignment = globalInstance.ActiveAlignment;
            HymnInputType = globalInstance.HymnInputType;
            LineSpacing = globalInstance.ActiveLineSpacing;
            LetterSpacing = globalInstance.ActiveLetterSpacing;
        }

        public void RefreshFromActiveHymn()
        {
            var activeHymn = globalInstance.ActiveHymn;
            if (activeHymn == null)
            {
                UpdateLoadingState();
                return;
            }

            if (string.IsNullOrEmpty(activeHymn.Lyrics) && globalInstance.HymnList?.Count > 0)
            {
                var match = globalInstance.HymnList.FirstOrDefault(h => h?.Number == activeHymn.Number);
                if (match != null)
                    activeHymn = match;
            }

            Title = "Hymn #" + activeHymn.Title;
            Lyrics = NormalizeLyrics(activeHymn.Lyrics);
            BookmarkFont = globalInstance.IsBookmarked() ? "FAS" : "FAR";

            if (OnHymnChanged != null)
                OnHymnChanged(activeHymn, EventArgs.Empty);

            UpdateLoadingState();
        }

        void UpdateLoadingState()
        {
            var downloadInProgress = globalInstance.IsDownloadRecoveryPending;
            var awaitingLyrics = string.IsNullOrEmpty(Lyrics);

            // During an active download, only the download popup should be visible.
            IsLoadingLyrics = awaitingLyrics && !downloadInProgress;
            OnPropertyChanged(nameof(HasLyrics));
            OnPropertyChanged(nameof(ShowLyricsContent));
            OnPropertyChanged(nameof(ShowNavBar));
            OnPropertyChanged(nameof(IsDownloadOverlayVisible));
        }

        public void RefreshLoadingState() => UpdateLoadingState();

        public bool HasLyrics => !string.IsNullOrEmpty(Lyrics);

        public bool ShowLyricsContent => HasLyrics && !globalInstance.IsDownloadRecoveryPending;

        public bool ShowNavBar => IsReadView && ShowLyricsContent && !IsSettingsOpen;

        private bool isSettingsOpen;
        public bool IsSettingsOpen
        {
            get => isSettingsOpen;
            set
            {
                if (SetProperty(ref isSettingsOpen, value, nameof(IsSettingsOpen)))
                    OnPropertyChanged(nameof(ShowNavBar));
            }
        }

        public bool IsDownloadOverlayVisible => globalInstance.IsDownloadRecoveryPending;

        private string downloadLottieIcon = "download";
        public string DownloadLottieIcon
        {
            get => downloadLottieIcon;
            set => SetProperty(ref downloadLottieIcon, value);
        }

        private string downloadMessage = "Downloading...";
        public string DownloadMessage
        {
            get => downloadMessage;
            set => SetProperty(ref downloadMessage, value);
        }

        private bool showDownloadDurationHint = true;
        public bool ShowDownloadDurationHint
        {
            get => showDownloadDurationHint;
            set => SetProperty(ref showDownloadDurationHint, value);
        }

        private void GlobalInstance_DownloadStarted(object sender, EventArgs e)
        {
            DownloadLottieIcon = "download";
            DownloadMessage = string.IsNullOrEmpty(globalInstance.LastDownloadProgressMessage)
                ? "Initializing..."
                : globalInstance.LastDownloadProgressMessage;
            ShowDownloadDurationHint = true;
            UpdateLoadingState();
        }

        private void GlobalInstance_DownloadProgressed(object sender, EventArgs e)
        {
            DownloadLottieIcon = "download";
            DownloadMessage = (string)sender;
            ShowDownloadDurationHint = true;
            UpdateLoadingState();
        }

        private void GlobalInstance_DownloadError(object sender, EventArgs e)
        {
            DownloadMessage = (string)sender;
            DownloadLottieIcon = DownloadMessage.Contains("connect")
                ? Application.Current.UserAppTheme == AppTheme.Light ? "no-internet-light" : "no-internet-dark"
                : "download";
            ShowDownloadDurationHint = false;
            UpdateLoadingState();
        }

        static string NormalizeLyrics(string value) =>
            (value ?? string.Empty).Replace('\uFFFD', '\'');

        private bool isLoadingLyrics;
        public bool IsLoadingLyrics
        {
            get => isLoadingLyrics;
            private set
            {
                if (SetProperty(ref isLoadingLyrics, value))
                    OnPropertyChanged(nameof(IsNotLoadingLyrics));
            }
        }

        public bool IsNotLoadingLyrics => !isLoadingLyrics;

        private ObservableRangeCollection<IGrouping<string, ShortHymn>> ModifyBookmarks(ObservableRangeCollection<ShortHymn> shortHymns)
        {
            globalInstance.NormalizeBookmarkGroups();
            return shortHymns.OrderBy(shortHymn => shortHymn.Line)
                .GroupBy((shortHymn) => shortHymn.BookmarkGroup)
                .OrderBy(group => group.Key).ToObservableRangeCollection();
        }

        double GetDrawerHeight()
        {
            var offset = DeviceInfo.Platform == DevicePlatform.Android ? 10 : 0;
            var multiplier = DeviceInfo.Platform == DevicePlatform.Android ? 55 : 50;
            return Math.Min(370 + offset, (GroupKeys.Count + 2) * multiplier + offset + 50 + 20);
        }
        #endregion

        #region Properties
        private ICommand _playPauseCommand;
        public ICommand PlayPauseCommand
        {
            get
            {
                return _playPauseCommand ?? (_playPauseCommand = new Command(
                  (obj) =>
                  {
                      if (commandText == "Play")
                      {
                          playService.Play();
                          commandText = "Pause";
                      }
                      else
                      {
                          playService.Pause();
                          commandText = "Play";
                      }
                  }));
            }
        }

        private string lyrics;
        public string Lyrics
        {
            get { return lyrics; }
            set
            {
                lyrics = value;
                SetProperty(ref lyrics, value, nameof(Lyrics));
                OnPropertyChanged();
                UpdateLoadingState();
            }
        }

        private Utils.InputType hymnInputType;
        public Utils.InputType HymnInputType
        {
            get => hymnInputType;
            set
            {
                hymnInputType = value;
                SetProperty(ref hymnInputType, value, nameof(HymnInputType));
                OnPropertyChanged();
            }
        }

        private TextAlignment activeAlignment;
        public TextAlignment ActiveAlignment
        {
            get => activeAlignment;
            set
            {
                activeAlignment = value;
                SetProperty(ref activeAlignment, value, nameof(ActiveAlignment));
                OnPropertyChanged();
            }
        }

        private Color activeColor;
        public Color ActiveColor
        {
            get => activeColor;
            set
            {
                activeColor = value;
                SetProperty(ref activeColor, value, nameof(ActiveColor));
                OnPropertyChanged();
            }
        }

        private Color activeColorText;
        public Color ActiveColorText
        {
            get => activeColorText;
            set
            {
                activeColorText = value;
                SetProperty(ref activeColorText, value, nameof(ActiveColorText));
                OnPropertyChanged();
            }
        }

        private double activeFontSize;
        public double ActiveFontSize
        {
            get => activeFontSize;
            set
            {
                activeFontSize = value;
                SetProperty(ref activeFontSize, value, nameof(ActiveFontSize));

                TitleContentMargin = Constraint.Constant(value + 25);
                OnPropertyChanged();
            }
        }

        private Constraint titleContentMargin;
        public Constraint TitleContentMargin
        {
            get => titleContentMargin;
            set
            {
                titleContentMargin = value;
                SetProperty(ref titleContentMargin, value, nameof(TitleContentMargin));
                OnPropertyChanged();
            }
        }

        private double letterSpacing;
        public double LetterSpacing
        {
            get => letterSpacing;
            set => SetProperty(ref letterSpacing, value, nameof(LetterSpacing));
        }

        private double lineSpacing = 1;
        public double LineSpacing
        {
            get => lineSpacing;
            set
            {
                if (SetProperty(ref lineSpacing, value, nameof(LineSpacing)))
                    OnPropertyChanged(nameof(TitleLabelMargin));
            }
        }

        public Thickness TitleLabelMargin =>
            new Thickness(0, 0, 15, Math.Max(0, (lineSpacing - 1) * 16));


        private string activeFont;
        public string ActiveFont
        {
            get => activeFont;
            set
            {
                activeFont = value;
                SetProperty(ref activeFont, value, nameof(ActiveFont));
                OnPropertyChanged();
            }
        }

        private string bookmarkFont = "FAR";
        public string BookmarkFont
        {
            get => bookmarkFont;
            set
            {
                bookmarkFont = value;
                SetProperty(ref bookmarkFont, value, nameof(BookmarkFont));
                OnPropertyChanged();

                if(OnBookmarked != null)
                    OnBookmarked(value, EventArgs.Empty);
            }
        }

        private ICommand fontSelected;
        public ICommand FontSelected
        {
            get
            {
                return fontSelected ?? (fontSelected = new Microsoft.Maui.Controls.Command<string>((fontName) =>
                {
                    globalInstance.ActiveFont = fontName;
                }));
            }
        }

        private bool isReadView = true;
        public bool IsReadView
        {
            get => isReadView;
            set
            {
                if (SetProperty(ref isReadView, value, nameof(IsReadView)))
                {
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ShowNavBar));
                }
            }
        }

        private bool isSelectable = false;
        public bool IsSelectable
        {
            get => isSelectable;
            set
            {
                isSelectable = value;
                SetProperty(ref isSelectable, value, nameof(IsSelectable));
                OnPropertyChanged();
            }
        }

        private double[] lockStates = new double[] { 0, .50, 0.75 };
        public double[] LockStates
        {
            get => lockStates;
            set => SetProperty(ref lockStates, value, nameof(LockStates));
        }

        private bool isBookmarkGroupsShown = false;
        public bool IsBookmarkGroupsShown
        {
            get => isBookmarkGroupsShown;
            set
            {
                isBookmarkGroupsShown = value;
                SetProperty(ref isBookmarkGroupsShown, value, nameof(IsBookmarkGroupsShown));
                OnPropertyChanged();
            }
        }

        private bool showAudioInternetNotice;
        public bool ShowAudioInternetNotice
        {
            get => showAudioInternetNotice;
            set => SetProperty(ref showAudioInternetNotice, value, nameof(ShowAudioInternetNotice));
        }

        private bool showAudioNotFoundNotice;
        public bool ShowAudioNotFoundNotice
        {
            get => showAudioNotFoundNotice;
            set => SetProperty(ref showAudioNotFoundNotice, value, nameof(ShowAudioNotFoundNotice));
        }

        private bool showAudioPlayLoader;
        public bool ShowAudioPlayLoader
        {
            get => showAudioPlayLoader;
            private set => SetProperty(ref showAudioPlayLoader, value, nameof(ShowAudioPlayLoader));
        }

        private bool isAudioLoading;
        private bool isAudioBuffering;

        public void SetAudioLoading(bool loading)
        {
            if (isAudioLoading == loading)
                return;

            isAudioLoading = loading;
            UpdateShowAudioPlayLoader();
        }

        public void SetAudioBuffering(bool buffering)
        {
            if (isAudioBuffering == buffering)
                return;

            isAudioBuffering = buffering;
            UpdateShowAudioPlayLoader();
        }

        void UpdateShowAudioPlayLoader() =>
            ShowAudioPlayLoader = isAudioLoading || isAudioBuffering;

        private ObservableRangeCollection<GroupDisplay> groupKeys;
        public ObservableRangeCollection<GroupDisplay> GroupKeys
        {
            get => groupKeys;
            private set
            {
                groupKeys = value;
                SetProperty(ref groupKeys, groupKeys, nameof(GroupKeys));
                OnPropertyChanged();
            }
        }

        private double drawerHeight;
        public double DrawerHeight
        {
            get => drawerHeight;
            set
            {
                drawerHeight = value;
                SetProperty(ref drawerHeight, drawerHeight, nameof(DrawerHeight));
                OnPropertyChanged();
            }
        }
        #endregion
    }
}

