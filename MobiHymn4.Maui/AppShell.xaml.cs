using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MobiHymn4.Models;
using MobiHymn4.Services;
using MobiHymn4.Utils;
using MobiHymn4.ViewModels;
using MobiHymn4.Views;
using MobiHymn4.Views.Popups;
using CommunityToolkit.Maui.Views;

using Microsoft.Maui.Controls;

namespace MobiHymn4
{
    public partial class AppShell : Microsoft.Maui.Controls.Shell
    {
        private Globals globalInstance = Globals.Instance;
        readonly IAuthService auth;
        readonly IProfileService profileService;

        public AppShell()
        {
            InitializeComponent();

            auth = ServiceHelper.Get<IAuthService>();
            profileService = ServiceHelper.Get<IProfileService>();

            Routing.RegisterRoute(Routes.HOME, typeof(NumSearchPage));
            Routing.RegisterRoute(Routes.READ, typeof(ReadPage));
            Routing.RegisterRoute(Routes.SEARCH, typeof(SearchPage));
            Routing.RegisterRoute(Routes.HISTORY, typeof(HistoryPage));
            Routing.RegisterRoute(Routes.BOOKMARKS_GROUP, typeof(BookmarksGroupPage));
            Routing.RegisterRoute(Routes.BOOKMARKS_LIST.Split('?')[0], typeof(BookmarksItemsPage));
            Routing.RegisterRoute(Routes.SETTINGS, typeof(SettingsPage));
            Routing.RegisterRoute(Routes.ABOUT, typeof(AboutPage));
            Routing.RegisterRoute(Routes.LOGIN, typeof(LoginPage));
            Routing.RegisterRoute(Routes.VERIFY_EMAIL, typeof(VerifyEmailPage));
            Routing.RegisterRoute(Routes.PROFILE_SETUP, typeof(ProfileSetupPage));
            Routing.RegisterRoute(Routes.ACCOUNT, typeof(AccountPage));
            Routing.RegisterRoute(Routes.GROUPS, typeof(GroupsPage));
            Routing.RegisterRoute(Routes.GROUP_MANAGE, typeof(GroupManagePage));

            CurrentItem = NavRead;

            globalInstance.ResyncDetails.CollectionChanged += ResyncDetails_CollectionChanged;
            Navigating += AppShell_Navigating;
            Navigated += AppShell_Navigated;
            Loaded += AppShell_Loaded;
            Loaded += (_, _) => UpdateFlyoutHeader();
            Loaded += (_, _) => _ = WarmFlyoutPagesAsync();
            auth.AuthStateChanged += (_, _) => MainThread.BeginInvokeOnMainThread(UpdateFlyoutHeader);
            profileService.ProfileChanged += (_, _) => MainThread.BeginInvokeOnMainThread(UpdateFlyoutHeader);
        }

        void UpdateFlyoutHeader()
        {
            var signedIn = auth.IsSignedIn;
            flyoutBrandHeader.IsVisible = !signedIn;
            flyoutUserHeader.IsVisible = signedIn;
            flyoutSignOutFooter.IsVisible = signedIn;
            NavAccount.FlyoutItemIsVisible = signedIn;

            if (!signedIn)
                return;

            var profile = profileService.CurrentProfile;
            flyoutAvatar.Roles = profile?.Roles?.ToList() ?? new List<UserRole>();
            flyoutUserName.Text = !string.IsNullOrWhiteSpace(profile?.DisplayName)
                ? profile.DisplayName
                : auth.CurrentEmail ?? "Account";
        }

        async void FlyoutUserHeader_Tapped(object sender, EventArgs e)
        {
            if (!auth.IsSignedIn)
                return;

            FlyoutIsPresented = false;
            await GoToAsync($"//{Routes.ACCOUNT}");
        }

        async void FlyoutEditProfile_Tapped(object sender, EventArgs e)
        {
            if (!auth.IsSignedIn)
                return;

            FlyoutIsPresented = false;
            await GoToAsync(Routes.PROFILE_SETUP);
        }

        async void FlyoutSignOut_Tapped(object sender, EventArgs e)
        {
            if (!auth.IsSignedIn)
                return;

            FlyoutIsPresented = false;
            try
            {
                await AuthNavigationHelper.SignOutAndNavigateAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FlyoutSignOut failed: {ex.Message}");
            }
        }

        void AppShell_Loaded(object? sender, EventArgs e)
        {
            globalInstance.RefreshIncompleteDownloadState();
            if (!DownloadPopupPresenter.IsDownloadRecoveryPending())
                return;

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(300);
                if (CurrentItem != NavRead)
                    CurrentItem = NavRead;

                DownloadPopupPresenter.ShowWithRetry(CurrentPage as Page);
                globalInstance.TryResumeInitAfterRelaunch();
            });
        }

        private async void AppShell_Navigating(object sender, ShellNavigatingEventArgs e)
        {
            if (!FlyoutIsPresented)
                return;

            var deferral = e.GetDeferral();
            try
            {
                FlyoutIsPresented = false;
                await Task.Delay(100);
            }
            finally
            {
                deferral.Complete();
            }
        }

        void AppShell_Navigated(object sender, ShellNavigatedEventArgs e)
        {
            // Relative pushes (login, bookmark items, etc.) use the platform nav stack.
            var nav = Navigation;
            if (nav?.NavigationStack?.Count > 1 || nav?.ModalStack?.Count > 0)
                return;

            var location = CurrentState?.Location?.OriginalString;
            ShellNavigationHistory.Instance.Record(location);
        }

        private async Task WarmFlyoutPagesAsync()
        {
            // Wait for startup navigation to settle before pre-building pages
            await Task.Delay(2500);

            // Walk every ShellItem (FlyoutItem, TabBar, etc.) so both flyout pages
            // and the NumSearchPage TabBar entry are all pre-built.
            foreach (var item in Items)
            {
                foreach (var section in item.Items)
                {
                    foreach (var content in section.Items)
                    {
                        if (content.Content != null || content.ContentTemplate == null)
                            continue;

                        try
                        {
                            await MainThread.InvokeOnMainThreadAsync(() =>
                            {
                                try { content.Content = (Page)content.ContentTemplate.CreateContent(); }
                                catch (Exception ex) { Debug.WriteLine($"WarmShell: {content.Route} failed: {ex.Message}"); }
                            });
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"WarmShell outer: {ex.Message}");
                        }

                        // Yield between each page so the UI stays responsive
                        await Task.Delay(300);
                    }
                }
            }
        }

        const bool ShowSyncChangesPopup = false;

        private void ResyncDetails_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (!ShowSyncChangesPopup || globalInstance.ResyncDetails.Count <= 0)
                return;

            SyncPopup syncPopup = new SyncPopup
            {
                CanBeDismissedByTappingOutsideOfPopup = true
            };
            syncPopup.Closed += SyncPopup_Dismissed;
            Navigation.ShowPopup(syncPopup);
        }

        private async void SyncPopup_Dismissed(object sender, PopupClosedEventArgs e)
        {
            if (e.Result == null)
                return;

            var downloadPopup = DownloadPopupPresenter.CreateAndTrack();
            await Task.Delay(250);
            Navigation.ShowPopup(downloadPopup);
            await Task.Yield();
            await RunSyncAsync();
        }

        async Task RunSyncAsync()
        {
            try
            {
                if (await globalInstance.ResyncHymns())
                    await globalInstance.FinishAfterDownloadAsync(isUserSync: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AppShell.SyncHymns failed: {ex.Message}");
                globalInstance.OnDownloadError(ex.Message);
            }
        }
    }
}

