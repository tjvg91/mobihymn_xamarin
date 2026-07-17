using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using MobiHymn4.Utils;
using MobiHymn4.ViewModels;
using MobiHymn4.Views;
using MobiHymn4.Views.Popups;
using CommunityToolkit.Maui.Views;

using Microsoft.Maui.Controls;

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
        private string badgeCatalogHash;
        private bool hamburgerBadgeAcknowledged;
        private bool settingsBadgeAcknowledged;

#if ANDROID
        private Android.Views.View hamburgerBadgeDot;
        private bool hamburgerBadgeSearchInProgress;
#endif

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

            CurrentItem = NavRead;

            globalInstance.ResyncDetails.CollectionChanged += ResyncDetails_CollectionChanged;
            globalInstance.CatalogDiffChanged += GlobalInstance_CatalogDiffChanged;
            Navigating += AppShell_Navigating;
            PropertyChanged += AppShell_PropertyChanged;
            Loaded += AppShell_Loaded;
            Loaded += (_, _) => _ = WarmFlyoutPagesAsync();
            UpdateCatalogBadges();
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
            if (e.PropertyName != nameof(FlyoutIsPresented)
                || !FlyoutIsPresented
                || hamburgerBadgeAcknowledged
                || globalInstance.PendingCatalogDiff?.ChangeCount <= 0)
                return;

            hamburgerBadgeAcknowledged = true;
            SetHamburgerBadgeVisible(false);
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
            if (hamburgerBadgeDot != null)
            {
                hamburgerBadgeDot.Visibility = show ? ViewStates.Visible : ViewStates.Gone;
                return;
            }

            if (!show || hamburgerBadgeSearchInProgress)
                return;

            hamburgerBadgeSearchInProgress = true;
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    for (var attempt = 0; attempt < 15 && hamburgerBadgeDot == null; attempt++)
                    {
                        var toolbar = FindToolbar();
                        if (toolbar != null && toolbar.Width > 0 && toolbar.Height > 0)
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

                if (hamburgerBadgeDot != null)
                    hamburgerBadgeDot.Visibility = show ? ViewStates.Visible : ViewStates.Gone;
            });
        }

        private void AttachHamburgerBadgeDot(AToolbar toolbar)
        {
            // Toolbar reserves space after the navigation icon for its own child
            // views, so a badge added directly to it can't overlap the icon.
            // Instead, anchor it to the activity's content root using the
            // Toolbar's actual on-screen position.
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity?.Window?.DecorView.FindViewById(Android.Resource.Id.Content) is not ViewGroup contentRoot)
                return;

            var toolbarLocation = new int[2];
            toolbar.GetLocationOnScreen(toolbarLocation);
            var contentLocation = new int[2];
            contentRoot.GetLocationOnScreen(contentLocation);

            var density = toolbar.Context.Resources.DisplayMetrics.Density;
            var iconStartInset = (int)(16 * density);
            var iconSize = (int)(24 * density);
            var size = (int)(9 * density);

            var dot = new Android.Views.View(toolbar.Context)
            {
                Visibility = ViewStates.Gone
            };
            using (var drawable = new GradientDrawable())
            {
                drawable.SetShape(ShapeType.Oval);
                drawable.SetColor(AColor.ParseColor("#E53935"));
                drawable.SetStroke((int)(1.5 * density), AColor.White);
                dot.Background = drawable;
            }

            var offsetX = toolbarLocation[0] - contentLocation[0];
            var offsetY = toolbarLocation[1] - contentLocation[1];

            var layoutParams = new FrameLayout.LayoutParams(size, size)
            {
                Gravity = GravityFlags.Top | GravityFlags.Left,
                LeftMargin = offsetX + iconStartInset + iconSize - size + (int)(2 * density),
                TopMargin = offsetY + (toolbar.Height - iconSize) / 2 - (int)(2 * density)
            };

            contentRoot.AddView(dot, layoutParams);
            hamburgerBadgeDot = dot;
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

