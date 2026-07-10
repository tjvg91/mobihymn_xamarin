using System;
using System.Diagnostics;
using System.Threading.Tasks;
using MobiHymn4.Services;
using MobiHymn4.Views.Popups;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace MobiHymn4.Utils;

public static class CommunitySignInPresenter
{
    static bool showing;
    static bool retryActive;
    static WeakReference<Page> preferredHostRef;

    public static bool ShouldShow =>
        !ServiceHelper.Get<IAuthService>().IsSignedIn
        && !Preferences.Get(PreferencesVar.COMMUNITY_PROMPT_DISMISSED, false);

    public static void ScheduleShow(Page preferredHost = null)
    {
        if (!ShouldShow || retryActive)
            return;

        if (preferredHost != null)
            preferredHostRef = new WeakReference<Page>(preferredHost);

        retryActive = true;
        _ = RunRetryLoopAsync();
    }

    public static Task ShowIfNeededAsync(Page host = null)
    {
        ScheduleShow(host);
        return Task.CompletedTask;
    }

    static async Task RunRetryLoopAsync()
    {
        var delays = new[] { 100, 400, 900, 1500, 2500, 4000 };
        try
        {
            foreach (var delay in delays)
            {
                await Task.Delay(delay);
                if (!ShouldShow)
                    return;

                if (await TryShowAsync(ResolveHost()))
                    return;
            }
        }
        finally
        {
            retryActive = false;
        }
    }

    static Page ResolveHost()
    {
        if (preferredHostRef != null
            && preferredHostRef.TryGetTarget(out var preferred)
            && preferred.Window != null)
            return preferred;

        return Shell.Current?.CurrentPage as Page;
    }

    static async Task<bool> TryShowAsync(Page host)
    {
        if (showing || !ShouldShow || host?.Window == null || DownloadPopupPresenter.IsPopupOpen)
            return false;

        showing = true;
        try
        {
            var popup = new CommunitySignInPopup();
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            popup.Closed += (_, e) =>
                tcs.TrySetResult(e.Result as string ?? CommunitySignInPopup.ResultLater);

            await MainThread.InvokeOnMainThreadAsync(() => host.ShowPopup(popup));
            var result = await tcs.Task;

            if (result == CommunitySignInPopup.ResultLater)
                Preferences.Set(PreferencesVar.COMMUNITY_PROMPT_DISMISSED, true);

            // Navigation for ResultSignUp is handled directly inside CommunitySignInPopup
            // (btnSignUp_Clicked) on the main thread, so nothing extra to do here.

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
