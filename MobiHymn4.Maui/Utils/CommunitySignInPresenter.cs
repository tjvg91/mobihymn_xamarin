using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Maui.Views;
using MobiHymn4.Extensions;
using MobiHymn4.Services;
using MobiHymn4.Views.Popups;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace MobiHymn4.Utils;

public static class CommunitySignInPresenter
{
    static bool showing;
    static int scheduleGeneration;
    static WeakReference<Page> preferredHostRef;

    /// <summary>
    /// Set when the reader has painted hymn lyrics (not during intro/download).
    /// Signup waits for this so it never races empty chrome or the download popup.
    /// </summary>
    public static bool ReaderContentReady { get; private set; }

    public static bool IsShowing => showing;

    public static bool ShouldShow =>
        !Preferences.Get(PreferencesVar.IS_NEW, true)
        && !ServiceHelper.Get<IAuthService>().IsSignedIn
        && !Preferences.Get(PreferencesVar.COMMUNITY_PROMPT_DISMISSED, false);

    /// <summary>
    /// Call once lyrics are visible on the reader. Safe to call repeatedly.
    /// </summary>
    public static void NotifyReaderContentReady(Page preferredHost = null)
    {
        ReaderContentReady = true;
        ScheduleShow(preferredHost);
    }

    /// <summary>
    /// Queue the community signup prompt. Safe to call repeatedly — waits out intro/download
    /// and only shows once the reader has content.
    /// </summary>
    public static void ScheduleShow(Page preferredHost = null)
    {
        if (preferredHost != null)
            preferredHostRef = new WeakReference<Page>(preferredHost);

        if (!ShouldShow || showing)
            return;

        var generation = Interlocked.Increment(ref scheduleGeneration);
        _ = RunShowWhenReadyAsync(generation);
    }

    public static Task ShowIfNeededAsync(Page host = null)
    {
        ScheduleShow(host);
        return Task.CompletedTask;
    }

    static async Task RunShowWhenReadyAsync(int generation)
    {
        for (var attempt = 0; attempt < 180; attempt++)
        {
            if (generation != Volatile.Read(ref scheduleGeneration))
                return;

            if (!ShouldShow || showing)
                return;

            if (!ReaderContentReady
                || DownloadPopupPresenter.IsPopupOpen
                || Globals.Instance.IsDownloadRecoveryPending
                || CatalogUpdatePresenter.IsShowing)
            {
                await Task.Delay(500);
                continue;
            }

            var host = ResolveHost();
            if (host?.Window == null)
            {
                await Task.Delay(400);
                continue;
            }

            if (await TryShowAsync(host))
                return;

            await Task.Delay(800);
        }

        Debug.WriteLine("CommunitySignInPresenter: gave up waiting to show signup prompt");
    }

    static Page ResolveHost()
    {
        if (preferredHostRef != null
            && preferredHostRef.TryGetTarget(out var preferred)
            && preferred?.Window != null)
            return preferred;

        var current = Shell.Current?.CurrentPage;
        if (current?.Window != null)
            return current;

        return Application.Current?.MainPage is { Window: not null } main ? main : null;
    }

    static async Task<bool> TryShowAsync(Page host)
    {
        if (showing
            || !ShouldShow
            || !ReaderContentReady
            || host?.Window == null
            || DownloadPopupPresenter.IsPopupOpen
            || Globals.Instance.IsDownloadRecoveryPending)
            return false;

        showing = true;
        try
        {
            var popup = new CommunitySignInPopup();
            var openedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var closedTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            popup.Opened += (_, _) => openedTcs.TrySetResult();
            popup.Closed += (_, e) => closedTcs.TrySetResult(e.Result as string);

            await MainThread.InvokeOnMainThreadAsync(() => host.ShowPopup(popup));

            var opened = await Task.WhenAny(openedTcs.Task, Task.Delay(2500));
            if (opened != openedTcs.Task)
            {
                Debug.WriteLine("CommunitySignInPresenter: popup did not open");
                try
                {
                    await MainThread.InvokeOnMainThreadAsync(() => popup.Close(null));
                }
                catch
                {
                }
                return false;
            }

            var result = await closedTcs.Task;
            // Only "Maybe Later" permanently dismisses — never null / failed closes.
            if (string.Equals(result, CommunitySignInPopup.ResultLater, StringComparison.Ordinal))
                Preferences.Set(PreferencesVar.COMMUNITY_PROMPT_DISMISSED, true);

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CommunitySignInPresenter failed: {ex.Message}");
            return false;
        }
        finally
        {
            showing = false;
        }
    }
}
