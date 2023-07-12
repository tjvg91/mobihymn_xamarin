using System;
using System.Collections.Generic;
using System.Linq;

using MobiHymn4.Models;
using MobiHymn4.Utils;

using Xamarin.Forms;
using Xamarin.Essentials;
using System.Threading.Tasks;

namespace MobiHymn4.Views
{
    public partial class BookmarksPage : ContentPage
    {
        private Globals globalInstance = Globals.Instance;
        public BookmarksPage()
        {
            InitializeComponent();
        }

        async void SwipeItem_Invoked(System.Object sender, System.EventArgs e)
        {
            try
            {
                var swipeItem = (SwipeItem)sender;
                var shortHymn = (ShortHymn)swipeItem.CommandParameter;

                var answer = await DisplayAlert("Delete?", $"Are you sure you want to delete Hymn #{shortHymn.Number} as bookmark?", "Yes", "No");
                if(answer)
                {
                    globalInstance.RemoveBookmark(shortHymn);
                    Globals.ShowToastPopup(
                        "bookmark-deleted",
                        "Bookmark deleted.",
                        DeviceInfo.Platform == DevicePlatform.Android ? 100 : 0.4,
                        DeviceInfo.Platform == DevicePlatform.Android ? new Rectangle(0.5, -0.2, 1, 1) : new Rectangle(0.8, 0.8, 1, 0.9)
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
            var item = e.Element as StackLayout;
            if(item != null)
            {
                item.Opacity = 0;
                await item.FadeTo(1, globalInstance.Duration);
            }
        }
    }
}

