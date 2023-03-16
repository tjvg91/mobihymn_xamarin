using System;

using MobiHymn2.Views;
using MobiHymn2.Utils;

using Xamarin.Forms;
using Xamarin.Essentials;

namespace MobiHymn2
{
    public partial class AppShell : Xamarin.Forms.Shell
    {
        private Globals globalInstance = Globals.Instance;
        public AppShell()
        {
            InitializeComponent();

            var isNew = (bool)Preferences.Get("isNew", true);
            if(!isNew) globalInstance.Init();

            Routing.RegisterRoute(Routes.HOME, typeof(NumSearchPage));
            Routing.RegisterRoute(Routes.READ, typeof(ReadPage));
            Routing.RegisterRoute(Routes.SEARCH, typeof(SearchPage));
            Routing.RegisterRoute(Routes.HISTORY, typeof(HistoryPage));
            Routing.RegisterRoute(Routes.BOOKMARKS, typeof(BookmarksPage));
            Routing.RegisterRoute(Routes.SETTINGS, typeof(SettingsPage));
            Routing.RegisterRoute(Routes.ABOUT, typeof(AboutPage));
        }
    }
}

