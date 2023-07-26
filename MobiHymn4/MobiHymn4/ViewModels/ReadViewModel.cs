
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

using MvvmHelpers;

using Xamarin.Forms;

using MobiHymn4.Utils;
using MobiHymn4.Views;
using System.Windows.Input;
using MobiHymn4.Models;
using Plugin.AudioRecorder;
using LiteDB;
using Xamarin.Essentials;

namespace MobiHymn4.ViewModels
{
    public class ReadViewModel : BaseViewModel
    {
        private Globals globalInstance = Globals.Instance;
        private IPlayService playService;
        private string commandText = "Play";

        public event EventHandler OnBookmarked;
        public event EventHandler OnHymnChanged;

        public ReadViewModel()
        {
            Title = "Hymn #" + globalInstance.ActiveHymn?.Title;
            Lyrics = globalInstance.ActiveHymn?.Lyrics;
            BookmarkFont = globalInstance.IsBookmarked() ? "FAS" : "FAR";

            ActiveColor = globalInstance.ActiveReadTheme;
            ActiveColorText = globalInstance.ActiveThemeText;
            ActiveFontSize = globalInstance.ActiveFontSize;
            ActiveFont = globalInstance.ActiveFont;
            ActiveAlignment = globalInstance.ActiveAlignment;
            HymnInputType = globalInstance.HymnInputType;

            globalInstance.ActiveReadThemeChanged += Globals_ActiveReadThemeChanged;
            globalInstance.ActiveHymnChanged += Globals_ActiveHymnChanged;
            globalInstance.ActiveAlignmentChanged += Globals_ActiveAlignmentChanged;
            globalInstance.ActiveFontSizeChanged += Globals_ActiveFontSizeChanged;
            globalInstance.ActiveFontChanged += Globals_ActiveFontChanged;
            globalInstance.HymnInputTypeChanged += GlobalInstance_HymnInputTypeChanged;
            globalInstance.BookmarksChanged += GlobalInstance_BookmarksChanged;

            LetterSpacing = globalInstance.FontList.Find(f => f.Name == activeFont).CharacterSpacing;

            GroupKeys = ModifyBookmarks(globalInstance.BookmarkList).Select((grp, count) => new GroupDisplay
            {
                Name = grp.Key,
                Count = grp.Count()
            }).ToObservableRangeCollection();

            DrawerHeight = GetDrawerHeight();
        }

        #region Events
        private void GlobalInstance_BookmarksChanged(object sender, EventArgs e)
        {
            BookmarkFont = globalInstance.IsBookmarked() ? "FAS" : "FAR";

            GroupKeys = ModifyBookmarks(globalInstance.BookmarkList).Select((grp, count) => new GroupDisplay
            {
                Name = grp.Key,
                Count = grp.Count()
            }).ToObservableRangeCollection();
            DrawerHeight = GetDrawerHeight();
        }

        private void GlobalInstance_HymnInputTypeChanged(object sender, EventArgs e)
        {
            HymnInputType = (Utils.InputType)sender;
        }

        private void Globals_ActiveFontChanged(object sender, EventArgs e)
        {
            ActiveFont = (string)sender;
            LetterSpacing = globalInstance.FontList.Find(f => f.Name == ActiveFont).CharacterSpacing;
        }

        private void Globals_ActiveFontSizeChanged(object sender, EventArgs e)
        {
            ActiveFontSize = (double)sender;
        }

        private void Globals_ActiveAlignmentChanged(object sender, EventArgs e)
        {
            ActiveAlignment = (TextAlignment)sender;
        }

        private void Globals_ActiveHymnChanged(object sender, EventArgs e)
        {
            var activeHymn = (Models.Hymn)sender;
            Title = "Hymn #" + activeHymn.Title;
            Lyrics = activeHymn.Lyrics;
            BookmarkFont = globalInstance.IsBookmarked() ? "FAS" : "FAR";

            if (OnHymnChanged != null) OnHymnChanged(activeHymn, EventArgs.Empty);
        }


        private void Globals_ActiveReadThemeChanged(object sender, EventArgs e)
        {
            ActiveColor = (Color)sender;
            ActiveColorText = globalInstance.ActiveThemeText;
        }
        #endregion

        #region Methods
        private ObservableRangeCollection<IGrouping<string, ShortHymn>> ModifyBookmarks(ObservableRangeCollection<ShortHymn> shortHymns)
        {
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
            set
            {
                letterSpacing = value;
                SetProperty(ref letterSpacing, value, nameof(LetterSpacing));
                OnPropertyChanged();
            }
        }


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
                return fontSelected ?? (fontSelected = new Xamarin.Forms.Command<string>((fontName) =>
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
                isReadView = value;
                SetProperty(ref isReadView, value, nameof(IsReadView));
                OnPropertyChanged();
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

