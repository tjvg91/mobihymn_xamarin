using System;
using System.Linq;
using System.Windows.Input;

using MvvmHelpers;
using MobiHymn2.Models;
using MobiHymn2.Utils;
using Xamarin.Forms;

namespace MobiHymn2.ViewModels
{
    public class BookmarksViewModel : BaseViewModel
    {
        private Globals globalInstance = Globals.Instance;

        private ObservableRangeCollection<ShortHymn> bookmarksList;
        public ObservableRangeCollection<ShortHymn> BookmarksList
        {
            get => bookmarksList;
            set
            {
                bookmarksList = value.OrderBy(x => x.Line).ToObservableRangeCollection();
                SetProperty(ref bookmarksList, bookmarksList, nameof(BookmarksList));
                OnPropertyChanged();
            }
        }

        public BookmarksViewModel()
        {
            BookmarksList = globalInstance.BookmarkList;
            Title = "Bookmarks";

            globalInstance.BookmarksChanged += Globals_BookmarksChanged;
        }

        private ICommand bookmarkSelected;
        public ICommand BookmarkSelected
        {
            get
            {
                return bookmarkSelected ?? (bookmarkSelected = new Xamarin.Forms.Command<ShortHymn>(async (shortHymn) =>
                {
                    globalInstance.ActiveHymn = (from x in globalInstance.HymnList
                                                where x.Number == shortHymn.Number
                                                select x).First();

                    await Shell.Current.GoToAsync($"//{Routes.READ}");
                }));
            }
        }

        private void Globals_BookmarksChanged(object sender, EventArgs e)
        {
            BookmarksList = (ObservableRangeCollection<ShortHymn>)sender;
        }
    }
}

