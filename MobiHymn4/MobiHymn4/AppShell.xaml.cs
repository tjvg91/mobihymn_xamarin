using System;
using System.Collections.Generic;
using MobiHymn4.Utils;
using MobiHymn4.ViewModels;
using MobiHymn4.Views;
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
            Routing.RegisterRoute(Routes.BOOKMARKS, typeof(BookmarksPage));
            Routing.RegisterRoute(Routes.SETTINGS, typeof(SettingsPage));
            Routing.RegisterRoute(Routes.ABOUT, typeof(AboutPage));

            CurrentItem = NavHome;

            var isNew = Preferences.Get("isNew", true);
            if (!isNew) globalInstance.Init();
        }
    }
}

