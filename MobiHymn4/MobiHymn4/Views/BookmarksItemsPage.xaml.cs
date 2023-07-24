using System;
using System.Linq;
using System.Collections.Generic;
using MobiHymn4.Models;
using MobiHymn4.Utils;
using MobiHymn4.ViewModels;
using Xamarin.Essentials;
using Xamarin.Forms;
using MobiHymn4.Views.Popups;
using Xamarin.CommunityToolkit.Extensions;
using System.Threading.Tasks;

namespace MobiHymn4.Views
{
    [QueryProperty(nameof(Name), "name")]
	public partial class BookmarksItemsPage : ContentPage
	{
        private Globals globalInstance = Globals.Instance;
        private BookmarksViewModel model;

        private string name;
        public string Name
        {
            get => name;
            set
            {
                name = value;
                Title = value.Capitalize();
                model.BookmarksPerKey = model.BookmarksList.Where(bk => bk.Key == value).First()
                    .OrderBy(bk => bk.Line).ToObservableRangeCollection();
            }
        }

        public BookmarksItemsPage ()
		{
			InitializeComponent ();
            model = (BookmarksViewModel)BindingContext;
            model.IsBookmarkGroupsShown = false;
            model.OnBookmarksChanged += Model_OnBookmarksChanged;
        }

        private async void Model_OnBookmarksChanged(object sender, EventArgs e)
        {
            var initQuery = model.BookmarksList.Where(bk => bk.Key == Name);
            if (initQuery.Count() == 0)
            {
                await Task.Delay(500);
                await Shell.Current.GoToAsync("..");
            }
            else
                model.BookmarksPerKey = initQuery.First().OrderBy(bk => bk.Line).ToObservableRangeCollection();
        }

        async void SwipeItem_Invoked(System.Object sender, System.EventArgs e)
        {
            try
            {
                var swipeItem = (SwipeItem)sender;
                var shortHymn = (ShortHymn)swipeItem.CommandParameter;

                var answer = await DisplayAlert("Delete?", $"Are you sure you want to delete Hymn #{shortHymn.Number} as bookmark?", "Yes", "No");
                if (answer)
                {
                    globalInstance.RemoveBookmark(shortHymn);
                    Globals.ShowToastPopup(
                        "bookmark-deleted",
                        "Bookmark deleted.",
                        DeviceInfo.Platform == DevicePlatform.Android ? 100 : 0.4,
                        DeviceInfo.Platform == DevicePlatform.Android ? new Rectangle(0.5, -0.3, 1, 1) : new Rectangle(0.7, -0.5, 1, 0.8)
                    );
                }
            }
            catch (Exception ex)
            {
                Console.Write(ex.Message);
                Globals.TrackError(ex);
            }
        }

        async void MyListView_ChildAdded(System.Object sender, Xamarin.Forms.ElementEventArgs e)
        {
            StackLayout item = e.Element as StackLayout;
            if (item != null)
            {
                item.Opacity = 0;
                await item.FadeTo(1, globalInstance.Duration);
            }
        }

        async void SwipeItem_Invoked_1(System.Object sender, System.EventArgs e)
        {
            var swipeItem = (SwipeItem)sender;
            var shortHymn = (ShortHymn)swipeItem.CommandParameter;

            model.GroupKeysExceptCurrent = (from key in model.GroupKeys
                                              where key.Name != shortHymn.BookmarkGroup
                                            select key).ToObservableRangeCollection();

            if (model.GroupKeysExceptCurrent.Count > 0)
            {
                await Task.Delay(500);
                model.IsBookmarkGroupsShown = true;
            }
            else
            {
                initPopup();
            }
        }

        void TapGestureRecognizer_Tapped(System.Object sender, System.EventArgs e)
        {
            var groupName = (string)((TappedEventArgs)e).Parameter;
            SetNewGroup(groupName);
        }

        void btnAddNewGroup_Clicked(System.Object sender, System.EventArgs e)
        {
            model.IsBookmarkGroupsShown = false;

            initPopup();
        }

        private void InpPopup_OK(object sender, EventArgs e)
        {
            var groupName = (string)sender;
            SetNewGroup(groupName);
        }

        void btnGrpCancel_Clicked(System.Object sender, System.EventArgs e)
        {
            model.IsBookmarkGroupsShown = false;
        }

        void SwipeView_SwipeStarted(System.Object sender, Xamarin.Forms.SwipeStartedEventArgs e)
        {
            model.ActiveBookmark = ((SwipeView)sender).BindingContext as ShortHymn;
        }

        async void SetNewGroup(string groupName)
        {
            globalInstance.BookmarkList.Where(bk => bk.Number == model.ActiveBookmark.Number)
                .First().BookmarkGroup = groupName;

            globalInstance.ForceBookmarkChangedEvent();
            await Task.Delay(500);
            Globals.ShowToastPopup("bookmark-saved", "Bookmard moved.",
                    DeviceInfo.Platform == DevicePlatform.Android ? 120 : 0.5,
                    DeviceInfo.Platform == DevicePlatform.Android ? new Rectangle(0.5, -0.1, 2, 2) : new Rectangle(0.8, -0.8, 1, 0.9));
        }

        async void tbHome_Clicked(System.Object sender, System.EventArgs e)
        {
            await Shell.Current.GoToAsync($"//{Routes.READ}");
        }

        void initPopup()
        {
            var inpPopup = new InputPopup
            {
                Title = "Move To",
                ActionString = "Move",
                Validation = (newKey) =>
                {
                    return model.GroupKeys.Where(key => key.Name.ToLower().Equals(newKey.ToLower())).Count() > 0 ?
                            "Group already exists." : "";
                }
            };
            inpPopup.OK += InpPopup_OK; ;
            Navigation.ShowPopup(inpPopup);
        }
    }
}

