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
using Microsoft.Maui.Networking;

#if ANDROID
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using AColor = Android.Graphics.Color;
using AToolbar = AndroidX.AppCompat.Widget.Toolbar;
#endif

namespace MobiHymn4
{
    public partial class AppShell : Microsoft.Maui.Controls.Shell
    {
        private Globals globalInstance = Globals.Instance;
        readonly IAuthService auth;
        readonly IProfileService profileService;
        private string badgeCatalogHash;
        private bool hamburgerBadgeAcknowledged;
        private bool settingsBadgeAcknowledged;

#if ANDROID
        private Android.Views.View hamburgerBadgeDot;
        private AToolbar hamburgerBadgeToolbar;
        private ViewGroup hamburgerBadgeContentRoot;
        private bool hamburgerBadgeSearchInProgress;
        private bool hamburgerBadgeWanted;
        private bool hamburgerBadgeRepositioning;
#endif

        public AppShell()
        {
            InitializeComponent();

            auth = ServiceHelper.Get<IAuthService>();
            profileService = ServiceHelper.Get<IProfileService>();

            Routing.RegisterRoute(Routes.HOME, typeof(NumSearchPage));
            Routing.RegisterRoute(Routes.READ, typeof(ReadPage));
            Routing.RegisterRoute(Routes.SEARCH, typeof(SearchPage));
            Routing.RegisterRoute(Routes.AGENT_CHAT, typeof(AgentChatPage));
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
            globalInstance.CatalogDiffChanged += GlobalInstance_CatalogDiffChanged;
            Navigating += AppShell_Navigating;
            Navigated += AppShell_Navigated;
            PropertyChanged += AppShell_PropertyChanged;
            Loaded += AppShell_Loaded;
            Loaded += (_, _) => UpdateFlyoutHeader();
            Loaded += (_, _) => _ = WarmFlyoutPagesAsync();
            auth.AuthStateChanged += (_, _) => MainThread.BeginInvokeOnMainThread(UpdateFlyoutHeader);
            profileService.ProfileChanged += (_, _) => MainThread.BeginInvokeOnMainThread(UpdateFlyoutHeader);
            Connectivity.ConnectivityChanged += Connectivity_ConnectivityChanged;
            UpdateAgentFlyoutVisibility(HttpHelper.IsConnected());
            UpdateCatalogBadges();

            ServiceHelper.Get<IGroupDashboardService>().IsOpenChanged +=
                (_, _) => MainThread.BeginInvokeOnMainThread(OnBoardOpenChanged);
        }

        void OnBoardOpenChanged()
        {
#if ANDROID
            if (ServiceHelper.Get<IGroupDashboardService>().IsOpen)
            {
                TrySetBadgeDotVisibility(ViewStates.Gone);
                return;
            }
#endif
            UpdateCatalogBadges();
        }

        void UpdateFlyoutHeader()
        {
            var signedIn = auth.IsSignedIn;
            flyoutBrandHeader.IsVisible = !signedIn;
            flyoutUserHeader.IsVisible = signedIn;
            NavAccount.FlyoutItemIsVisible = signedIn;

            if (flyoutAuthFooter != null)
            {
                flyoutAuthFooter.IsVisible = true;
                if (flyoutAuthLabel != null)
                    flyoutAuthLabel.Text = signedIn ? "Sign out" : "Log In";
                if (flyoutAuthIcon != null)
                {
                    flyoutAuthIcon.Text = signedIn
                        ? FontAwesome.FontAwesomeIcons.RightFromBracket
                        : FontAwesome.FontAwesomeIcons.RightToBracket;
                }
            }

            if (!signedIn)
                return;

            var profile = profileService.CurrentProfile;
            flyoutAvatar.Roles = profile?.Roles?.ToList() ?? new List<UserRole>();
            flyoutUserName.Text = !string.IsNullOrWhiteSpace(profile?.DisplayName)
                ? profile.DisplayName
                : auth.CurrentEmail ?? "Account";
        }

        async void FlyoutAuthFooter_Tapped(object sender, EventArgs e)
        {
            FlyoutIsPresented = false;

            if (auth.IsSignedIn)
            {
                try
                {
                    await AuthNavigationHelper.SignOutAndNavigateAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"FlyoutSignOut failed: {ex.Message}");
                }
                return;
            }

            AuthNavigationHelper.SetPendingSignUpMode(false);
            try
            {
                var nav = Navigation ?? Shell.Current?.Navigation;
                if (nav != null)
                    await nav.PushModalAsync(new LoginPage());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FlyoutLogIn failed: {ex.Message}");
            }
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

        void Connectivity_ConnectivityChanged(object sender, ConnectivityChangedEventArgs e) =>
            MainThread.BeginInvokeOnMainThread(() =>
                UpdateAgentFlyoutVisibility(e.NetworkAccess == NetworkAccess.Internet));

        void UpdateAgentFlyoutVisibility(bool online)
        {
            if (NavAgent == null)
                return;

            NavAgent.IsVisible = online;
            if (!online && CurrentItem == NavAgent)
                CurrentItem = NavRead;
        }

        private void GlobalInstance_CatalogDiffChanged(object sender, EventArgs e) =>
            UpdateCatalogBadges();

        private void UpdateCatalogBadges()
        {
            var diff = globalInstance.PendingCatalogDiff;
            if (diff == null || diff.ChangeCount == 0)
            {
                badgeCatalogHash = null;
                hamburgerBadgeAcknowledged = false;
                settingsBadgeAcknowledged = false;
                CatalogBadgeState.Instance.ShowSettingsBadge = false;
                SetHamburgerBadgeVisible(false);
                return;
            }

            if (!string.Equals(badgeCatalogHash, diff.CatalogHash, StringComparison.Ordinal))
            {
                badgeCatalogHash = diff.CatalogHash;
                hamburgerBadgeAcknowledged = false;
                settingsBadgeAcknowledged = false;
            }

            CatalogBadgeState.Instance.ShowSettingsBadge = !settingsBadgeAcknowledged;
            SetHamburgerBadgeVisible(!hamburgerBadgeAcknowledged);
        }

        private void AppShell_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(FlyoutIsPresented))
                return;

            if (FlyoutIsPresented)
            {
                // Native overlay sits on the activity content root and otherwise
                // floats over the drawer (often top-left) while the flyout is open.
                if (globalInstance.PendingCatalogDiff?.ChangeCount > 0
                    && !hamburgerBadgeAcknowledged)
                    hamburgerBadgeAcknowledged = true;

                SetHamburgerBadgeVisible(false);
                return;
            }

            UpdateCatalogBadges();
        }

        // The Android Toolbar force-tints any custom FlyoutIcon to a single flat
        // color (a long-standing MAUI limitation, see dotnet/maui#32495), which
        // makes a multi-color hamburger+badge icon render as solid white. So the
        // default hamburger icon (correctly tinted black via Shell.ForegroundColor)
        // is left untouched, and the badge dot is drawn as a small native overlay
        // View glued onto the Toolbar instead.
        partial void SetHamburgerBadgeVisible(bool show);

#if ANDROID
        partial void SetHamburgerBadgeVisible(bool show)
        {
            // Never draw the toolbar badge while the flyout covers the hamburger.
            if (show && FlyoutIsPresented)
                show = false;

            hamburgerBadgeWanted = show;

            if (!show)
            {
                TrySetBadgeDotVisibility(ViewStates.Gone);
                return;
            }

            if (hamburgerBadgeDot != null)
            {
                RepositionHamburgerBadgeDot();
                if (hamburgerBadgeWanted && hamburgerBadgeDot != null)
                    TrySetBadgeDotVisibility(ViewStates.Visible);
                return;
            }

            if (hamburgerBadgeSearchInProgress)
                return;

            hamburgerBadgeSearchInProgress = true;
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    for (var attempt = 0; attempt < 15 && hamburgerBadgeDot == null && hamburgerBadgeWanted; attempt++)
                    {
                        var toolbar = FindToolbar();
                        if (toolbar != null && IsAndroidViewAlive(toolbar)
                            && toolbar.Width > 0 && toolbar.Height > 0)
                        {
                            AttachHamburgerBadgeDot(toolbar);
                            break;
                        }

                        await Task.Delay(200);
                    }
                }
                finally
                {
                    hamburgerBadgeSearchInProgress = false;
                }

                if (hamburgerBadgeDot == null)
                    return;

                if (hamburgerBadgeWanted)
                {
                    RepositionHamburgerBadgeDot();
                    TrySetBadgeDotVisibility(ViewStates.Visible);
                }
                else
                {
                    TrySetBadgeDotVisibility(ViewStates.Gone);
                }
            });
        }

        private void AttachHamburgerBadgeDot(AToolbar toolbar)
        {
            // Toolbar reserves space after the navigation icon for its own child
            // views, so a badge added directly to it can't overlap the icon.
            // Instead, overlay on the activity content root and keep margins in
            // sync with the Toolbar's on-screen bounds (layout changes otherwise
            // leave a floating red dot over page content).
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity?.Window?.DecorView.FindViewById(Android.Resource.Id.Content) is not ViewGroup contentRoot)
                return;

            if (!IsAndroidViewAlive(toolbar) || !IsAndroidViewAlive(contentRoot))
                return;

            DetachHamburgerBadgeDot();

            var density = toolbar.Context.Resources.DisplayMetrics.Density;
            var size = (int)(9 * density);

            var dot = new Android.Views.View(toolbar.Context)
            {
                Visibility = ViewStates.Gone,
                Elevation = 24f,
            };
            using (var drawable = new GradientDrawable())
            {
                drawable.SetShape(ShapeType.Oval);
                drawable.SetColor(AColor.ParseColor("#E53935"));
                drawable.SetStroke((int)(1.5 * density), AColor.White);
                dot.Background = drawable;
            }

            var layoutParams = new FrameLayout.LayoutParams(size, size)
            {
                Gravity = GravityFlags.Top | GravityFlags.Left,
            };

            contentRoot.AddView(dot, layoutParams);
            hamburgerBadgeDot = dot;
            hamburgerBadgeToolbar = toolbar;
            hamburgerBadgeContentRoot = contentRoot;

            toolbar.LayoutChange += OnHamburgerBadgeToolbarLayoutChange;
            contentRoot.LayoutChange += OnHamburgerBadgeToolbarLayoutChange;

            RepositionHamburgerBadgeDot();
            try
            {
                contentRoot.BringChildToFront(dot);
            }
            catch (ObjectDisposedException)
            {
                DetachHamburgerBadgeDot();
            }
        }

        private void DetachHamburgerBadgeDot()
        {
            try
            {
                if (hamburgerBadgeToolbar != null)
                {
                    try
                    {
                        hamburgerBadgeToolbar.LayoutChange -= OnHamburgerBadgeToolbarLayoutChange;
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                    hamburgerBadgeToolbar = null;
                }

                if (hamburgerBadgeContentRoot != null)
                {
                    try
                    {
                        hamburgerBadgeContentRoot.LayoutChange -= OnHamburgerBadgeToolbarLayoutChange;
                        if (hamburgerBadgeDot != null
                            && IsAndroidViewAlive(hamburgerBadgeDot)
                            && hamburgerBadgeDot.Parent == hamburgerBadgeContentRoot)
                            hamburgerBadgeContentRoot.RemoveView(hamburgerBadgeDot);
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                    hamburgerBadgeContentRoot = null;
                }
                else if (hamburgerBadgeDot != null
                    && IsAndroidViewAlive(hamburgerBadgeDot)
                    && hamburgerBadgeDot.Parent is ViewGroup parent)
                {
                    try
                    {
                        parent.RemoveView(hamburgerBadgeDot);
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }
            }
            finally
            {
                hamburgerBadgeDot = null;
                hamburgerBadgeToolbar = null;
                hamburgerBadgeContentRoot = null;
            }
        }

        private void OnHamburgerBadgeToolbarLayoutChange(
            object sender,
            Android.Views.View.LayoutChangeEventArgs e) =>
            RepositionHamburgerBadgeDot();

        private void RepositionHamburgerBadgeDot()
        {
            if (hamburgerBadgeRepositioning
                || hamburgerBadgeDot == null
                || hamburgerBadgeToolbar == null
                || hamburgerBadgeContentRoot == null)
                return;

            hamburgerBadgeRepositioning = true;
            try
            {
                if (!IsAndroidViewAlive(hamburgerBadgeDot)
                    || !IsAndroidViewAlive(hamburgerBadgeToolbar)
                    || !IsAndroidViewAlive(hamburgerBadgeContentRoot))
                {
                    DetachHamburgerBadgeDot();
                    // Toolbar was recreated during Shell navigation — reattach if still needed.
                    if (hamburgerBadgeWanted)
                        SetHamburgerBadgeVisible(true);
                    return;
                }

                if (FlyoutIsPresented || !hamburgerBadgeWanted)
                {
                    hamburgerBadgeDot.Visibility = ViewStates.Gone;
                    return;
                }

                var toolbar = hamburgerBadgeToolbar;
                if (toolbar.Visibility != ViewStates.Visible
                    || toolbar.Width <= 0
                    || toolbar.Height <= 0
                    || !toolbar.IsShown)
                {
                    hamburgerBadgeDot.Visibility = ViewStates.Gone;
                    return;
                }

                var toolbarLocation = new int[2];
                toolbar.GetLocationOnScreen(toolbarLocation);
                var contentLocation = new int[2];
                hamburgerBadgeContentRoot.GetLocationOnScreen(contentLocation);

                var offsetX = toolbarLocation[0] - contentLocation[0];
                var offsetY = toolbarLocation[1] - contentLocation[1];

                // Before the toolbar is on-screen, GetLocationOnScreen can return
                // zeros and the badge ends up floating over hymn text.
                if (toolbarLocation[1] <= 0 || offsetY < 0 || offsetX < 0)
                {
                    hamburgerBadgeDot.Visibility = ViewStates.Gone;
                    return;
                }

                // Board pane covers the reader; keep the catalog badge off so it
                // can't float over hymn text while the Shell toolbar is obscured.
                try
                {
                    if (ServiceHelper.Get<IGroupDashboardService>().IsOpen)
                    {
                        hamburgerBadgeDot.Visibility = ViewStates.Gone;
                        return;
                    }
                }
                catch
                {
                    // Service may not be ready during early shell init.
                }

                var density = toolbar.Context.Resources.DisplayMetrics.Density;
                var iconStartInset = (int)(16 * density);
                var iconSize = (int)(24 * density);
                var size = hamburgerBadgeDot.LayoutParameters?.Width > 0
                    ? hamburgerBadgeDot.LayoutParameters.Width
                    : (int)(9 * density);

                var left = offsetX + iconStartInset + iconSize - size + (int)(2 * density);
                var top = offsetY + (toolbar.Height - iconSize) / 2 - (int)(2 * density);

                if (hamburgerBadgeDot.LayoutParameters is not FrameLayout.LayoutParams lp)
                {
                    lp = new FrameLayout.LayoutParams(size, size)
                    {
                        Gravity = GravityFlags.Top | GravityFlags.Left,
                    };
                    hamburgerBadgeDot.LayoutParameters = lp;
                }

                if (lp.LeftMargin != left || lp.TopMargin != top)
                {
                    lp.LeftMargin = Math.Max(0, left);
                    lp.TopMargin = Math.Max(0, top);
                    hamburgerBadgeDot.LayoutParameters = lp;
                }

                hamburgerBadgeContentRoot.BringChildToFront(hamburgerBadgeDot);

                if (hamburgerBadgeWanted)
                    hamburgerBadgeDot.Visibility = ViewStates.Visible;
            }
            catch (ObjectDisposedException)
            {
                DetachHamburgerBadgeDot();
                if (hamburgerBadgeWanted)
                    SetHamburgerBadgeVisible(true);
            }
            catch (Java.Lang.IllegalStateException)
            {
                DetachHamburgerBadgeDot();
                if (hamburgerBadgeWanted)
                    SetHamburgerBadgeVisible(true);
            }
            finally
            {
                hamburgerBadgeRepositioning = false;
            }
        }

        static bool IsAndroidViewAlive(Android.Views.View view)
        {
            if (view == null)
                return false;

            try
            {
                return view.Handle != IntPtr.Zero;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        void TrySetBadgeDotVisibility(ViewStates state)
        {
            if (hamburgerBadgeDot == null || !IsAndroidViewAlive(hamburgerBadgeDot))
            {
                if (hamburgerBadgeDot != null)
                    DetachHamburgerBadgeDot();
                return;
            }

            try
            {
                hamburgerBadgeDot.Visibility = state;
            }
            catch (ObjectDisposedException)
            {
                DetachHamburgerBadgeDot();
            }
        }

        private static AToolbar FindToolbar()
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            return activity?.Window?.DecorView is ViewGroup decor ? FindToolbarRecursive(decor) : null;
        }

        private static AToolbar FindToolbarRecursive(ViewGroup group)
        {
            for (var i = 0; i < group.ChildCount; i++)
            {
                var child = group.GetChildAt(i);
                if (child is AToolbar toolbar)
                    return toolbar;

                if (child is ViewGroup childGroup)
                {
                    var found = FindToolbarRecursive(childGroup);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }
#endif

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
            if ((e.Target?.Location?.OriginalString ?? string.Empty)
                    .Contains(Routes.SETTINGS, StringComparison.OrdinalIgnoreCase)
                && !settingsBadgeAcknowledged)
            {
                settingsBadgeAcknowledged = true;
                CatalogBadgeState.Instance.ShowSettingsBadge = false;
            }

#if ANDROID
            // Shell swaps toolbars during tab switches; drop the overlay so layout
            // callbacks can't touch disposed Android views (FAB → HOME crash).
            TrySetBadgeDotVisibility(ViewStates.Gone);
            DetachHamburgerBadgeDot();
#endif

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

#if ANDROID
            // Re-attach catalog badge against the new page's toolbar if still needed.
            MainThread.BeginInvokeOnMainThread(UpdateCatalogBadges);
#endif
        }

        private async Task WarmFlyoutPagesAsync()
        {
            // Wait until hymns are ready so warm-up doesn't hitch the logo pulse / first paint.
            for (var i = 0; i < 40 && !globalInstance.InitComplete; i++)
                await Task.Delay(250);

            await Task.Delay(1500);

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
                        await Task.Delay(400);
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

