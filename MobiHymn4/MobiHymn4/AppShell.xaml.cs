using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MobiHymn4.Utils;
using MobiHymn4.ViewModels;
using MobiHymn4.Views;
using MobiHymn4.Views.Popups;
using Xamarin.CommunityToolkit.Extensions;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace MobiHymn4
{
    public partial class AppShell : Xamarin.Forms.Shell
    {
        private Globals globalInstance = Globals.Instance;

        public AppShell()
        {
            InitializeComponent();

            Routing.RegisterRoute(Routes.HOME, typeof(NumSearchPage));
            Routing.RegisterRoute(Routes.READ, typeof(ReadPage));
            Routing.RegisterRoute(Routes.SEARCH, typeof(SearchPage));
            Routing.RegisterRoute(Routes.HISTORY, typeof(HistoryPage));
            Routing.RegisterRoute(Routes.BOOKMARKS_GROUP, typeof(BookmarksGroupPage));
            Routing.RegisterRoute(Routes.BOOKMARKS_LIST.Split('?')[0], typeof(BookmarksItemsPage));
            Routing.RegisterRoute(Routes.SETTINGS, typeof(SettingsPage));
            Routing.RegisterRoute(Routes.ABOUT, typeof(AboutPage));

            CurrentItem = NavHome;

            globalInstance.ResyncDetails.CollectionChanged += ResyncDetails_CollectionChanged;
        }

        private void ResyncDetails_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if(globalInstance.ResyncDetails.Count > 0)
            {
                SyncPopup syncPopup = new SyncPopup
                {
                    IsLightDismissEnabled = true
                };
                syncPopup.Dismissed += SyncPopup_Dismissed;
                Navigation.ShowPopup(syncPopup);
            }
        }

        private async void SyncPopup_Dismissed(object sender, Xamarin.CommunityToolkit.UI.Views.PopupDismissedEventArgs e)
        {
            if (e.Result != null)
            {
                DownloadPopup downloadPopup = new()
                {
                    IsLightDismissEnabled = false,
                    Todo = SyncHymns
                };
                //downloadPopup.Dismissed += DownloadPopup_Dismissed;

                await Task.Delay(250);
                Navigation.ShowPopup(downloadPopup);
                SyncHymns();
            }
        }

        async void SyncHymns()
        {
            if (await globalInstance.ResyncHymns())
            {
                globalInstance.OnInitFinished("sync");
            }
        }
    }
}

