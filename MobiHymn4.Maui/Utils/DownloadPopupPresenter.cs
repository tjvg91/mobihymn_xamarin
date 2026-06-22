using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using MobiHymn4.Views.Popups;
using Microsoft.Maui.Controls;

namespace MobiHymn4.Utils;

/// <summary>
/// Tracks and shows the active download/sync progress popup.
/// Prefer showing on a visible ContentPage (ReadPage) — Shell is an unreliable host on Android cold start.
/// </summary>
public static class DownloadPopupPresenter
{
    static Popup? openPopup;
    static bool retryActive;
    static WeakReference<Page>? preferredHostRef;

    static DownloadPopupPresenter()
    {
        Globals.Instance.DownloadStarted += (_, _) => ShowFromEvent();
    }

    public static bool IsPopupOpen => openPopup != null;

    public static DownloadPopup CreateAndTrack()
    {
        var p = new DownloadPopup { CanBeDismissedByTappingOutsideOfPopup = false };
        openPopup = p;
        p.Closed += (_, _) => { if (openPopup == p) openPopup = null; };
        return p;
    }

    public static void ShowFromEvent()
    {
        if (!IsDownloadActive() || openPopup != null)
            return;

        ShowWithRetry();
    }

    /// <param name="preferredHost">
    /// Page that is on screen (e.g. ReadPage from OnAppearing).
    /// </param>
    public static void ShowWithRetry(Page? preferredHost = null)
    {
        if (!IsDownloadActive() || openPopup != null)
            return;

        RememberPreferredHost(preferredHost);

        if (preferredHost != null && TryShowOnPage(preferredHost))
            return;

        if (retryActive)
            return;

        retryActive = true;
        MainThread.BeginInvokeOnMainThread(async () => await RunRetryLoopAsync());
    }

    /// <summary>
    /// Show immediately on a page that is already visible. Returns true if the popup opened.
    /// </summary>
    public static bool TryShowOnPage(Page page)
    {
        if (!IsDownloadActive() || openPopup != null || page?.Window == null)
            return false;

        try
        {
            var popup = CreateAndTrack();
            var showTask = page.ShowPopupAsync(popup);
            _ = showTask.ContinueWith(t =>
            {
                if (!t.IsFaulted)
                    return;

                if (openPopup == popup)
                    openPopup = null;

                System.Diagnostics.Debug.WriteLine($"[DownloadPopup] ShowPopupAsync failed: {t.Exception?.GetBaseException().Message}");
                MainThread.BeginInvokeOnMainThread(() => ShowWithRetry(page));
            });
            return true;
        }
        catch (Exception ex)
        {
            openPopup = null;
            System.Diagnostics.Debug.WriteLine($"[DownloadPopup] TryShowOnPage failed: {ex.Message}");
            return false;
        }
    }

    static async Task RunRetryLoopAsync()
    {
        try
        {
            for (var attempt = 0; attempt < 40 && IsDownloadActive() && openPopup == null; attempt++)
            {
                var host = ResolveHost();
                if (host != null && TryShowOnPage(host))
                    return;

                await Task.Delay(250);
            }
        }
        finally
        {
            retryActive = false;

            if (IsDownloadActive() && openPopup == null && !retryActive)
                ShowWithRetry();
        }
    }

    static Page? ResolveHost()
    {
        if (preferredHostRef?.TryGetTarget(out var rememberedHost) == true &&
            rememberedHost is { Window: not null })
            return rememberedHost;

        var current = Shell.Current?.CurrentPage;
        if (current is Page currentPage && currentPage.Window != null)
            return currentPage;

        return null;
    }

    static void RememberPreferredHost(Page? preferredHost)
    {
        if (preferredHost == null)
            return;

        preferredHostRef = new WeakReference<Page>(preferredHost);
    }

    public static void ClearActivePopup() => openPopup = null;

    public static bool IsDownloadRecoveryPending() =>
        Globals.Instance.IsDownloadRecoveryPending;

    static bool IsDownloadActive() => IsDownloadRecoveryPending();
}
