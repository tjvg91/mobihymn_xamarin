using System;
using System.Linq;
using System.Windows.Input;

using MvvmHelpers;
using MobiHymn4.Models;
using MobiHymn4.Utils;
using Xamarin.Forms;
using Xamarin.Essentials;

namespace MobiHymn4.ViewModels
{
    public class BookmarksViewModel : BaseViewModel
    {
        private Globals globalInstance = Globals.Instance;

        public event EventHandler OnBookmarksChanged;

        private ObservableRangeCollection<IGrouping<string, ShortHymn>> bookmarksList;
        public ObservableRangeCollection<IGrouping<string, ShortHymn>> BookmarksList
        {
            get => bookmarksList;
            set
            {
                bookmarksList = value;
                SetProperty(ref bookmarksList, bookmarksList, nameof(BookmarksList));
                GroupKeys = value.Select((grp, count) => new GroupDisplay
                {
                    Name = grp.Key,
                    Count = grp.Count()
                }).ToObservableRangeCollection();
                OnPropertyChanged();
            }
        }

        private ObservableRangeCollection<ShortHymn> bookmarksPerKey;
        public ObservableRangeCollection<ShortHymn> BookmarksPerKey
        {
            get => bookmarksPerKey;
            set
            {
                bookmarksPerKey = value;
                SetProperty(ref bookmarksPerKey, bookmarksPerKey, nameof(BookmarksPerKey));
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

        private ObservableRangeCollection<GroupDisplay> groupKeysExceptCurrent;
        public ObservableRangeCollection<GroupDisplay> GroupKeysExceptCurrent
        {
            get => groupKeysExceptCurrent;
            set
            {
                groupKeysExceptCurrent = value;
                SetProperty(ref groupKeysExceptCurrent, groupKeysExceptCurrent, nameof(GroupKeysExceptCurrent));

                DrawerHeight = GetDrawerHeight();
                CollectionHeight = GetCollectionHeight();
                HasCollection = value.Count > 0;
                OnPropertyChanged();
            }
        }

        private ShortHymn activeBookmark;
        public ShortHymn ActiveBookmark
        {
            get => activeBookmark;
            set
            {
                activeBookmark = value;
                SetProperty(ref activeBookmark, value, nameof(ActiveBookmark));
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

        private double collectionHeight;
        public double CollectionHeight
        {
            get => collectionHeight;
            set
            {
                collectionHeight = value;
                SetProperty(ref collectionHeight, collectionHeight, nameof(CollectionHeight));
                OnPropertyChanged();
            }
        }

        private bool hasCollection;
        public bool HasCollection
        {
            get => hasCollection;
            private set
            {
                hasCollection = value;
                SetProperty(ref hasCollection, value, nameof(HasCollection));
                OnPropertyChanged();
            }
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

        public BookmarksViewModel()
        {
            BookmarksList = ModifyBookmarks(globalInstance.BookmarkList);
            Title = "Bookmarks";

            globalInstance.BookmarksChanged += Globals_BookmarksChanged;
        }

        private ICommand bookmarkGroupSelected;
        public ICommand BookmarkGroupSelected
        {
            get
            {
                return bookmarkGroupSelected ?? (bookmarkGroupSelected = new Xamarin.Forms.Command<string>(async (key) =>
                {
                    await Shell.Current.GoToAsync($"//{Routes.BOOKMARKS_LIST.Replace("{group}", key)}");
                }));
            }
        }

        private ICommand bookmarkSelected;
        public ICommand BookmarkSelected
        {
            get
            {
                return bookmarkSelected ?? (bookmarkSelected = new Xamarin.Forms.Command<ShortHymn>(async (shortHymn) =>
                {
                    globalInstance.ActiveHymn = globalInstance.HymnList[shortHymn.Number];

                    await Shell.Current.GoToAsync($"//{Routes.READ}");
                }));
            }
        }

        private void Globals_BookmarksChanged(object sender, EventArgs e)
        {
            BookmarksList = ModifyBookmarks((ObservableRangeCollection<ShortHymn>)sender);

            DrawerHeight = GetDrawerHeight();
            CollectionHeight = GetCollectionHeight();

            OnBookmarksChanged?.Invoke(BookmarksList, EventArgs.Empty);
        }

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
            return Math.Min(370 + offset, (GroupKeys.Count + 2) * multiplier + 20 + offset);
        }

        double GetCollectionHeight()
        {
            var offset = DeviceInfo.Platform == DevicePlatform.Android ? 10 : 0;
            var multiplier = DeviceInfo.Platform == DevicePlatform.Android ? 55 : 50;
            return Math.Min(150 + offset, GroupKeys.Count * multiplier + offset);
        }
    }
}

