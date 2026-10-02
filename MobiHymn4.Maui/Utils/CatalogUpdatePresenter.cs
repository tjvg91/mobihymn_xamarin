using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MobiHymn4.Models;
using MobiHymn4.Views;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace MobiHymn4.Utils;

/// <summary>
/// Alerts the user once per server catalog version that hymn updates are available,
/// and offers to open the change list (Settings → View changes → Sync).
/// </summary>
public static class CatalogUpdatePresenter
{
    static bool showing;
    static int scheduleGeneration;

    public static bool IsShowing => showing;

    public static void ScheduleShow(CatalogDiff diff)
    {
        if (!ShouldShow(diff) || showing)
            return;

        var generation = Interlocked.Increment(ref scheduleGeneration);
        _ = RunShowWhenReadyAsync(diff.CatalogHash, generation);
    }

    static bool ShouldShow(CatalogDiff diff) =>
        diff != null
        && diff.ChangeCount > 0
        && !string.IsNullOrWhiteSpace(diff.CatalogHash)
        && !string.Equals(
            Preferences.Get(PreferencesVar.CATALOG_UPDATE_PROMPTED_HASH, string.Empty),
            diff.CatalogHash,
            StringComparison.Ordinal);

    static bool IsPending(string hash)
    {
        var diff = Globals.Instance.PendingCatalogDiff;
        return ShouldShow(diff) && string.Equals(diff.CatalogHash, hash, StringComparison.Ordinal);
    }

    static async Task RunShowWhenReadyAsync(string hash, int generation)
    {
        // Wait out the intro, library download and the community signup prompt.
        for (var attempt = 0; attempt < 240; attempt++)
        {
            if (generation != Volatile.Read(ref scheduleGeneration) || showing || !IsPending(hash))
                return;

            var host = Shell.Current?.CurrentPage;
            if (!CommunitySignInPresenter.ReaderContentReady
                || CommunitySignInPresenter.IsShowing
                || DownloadPopupPresenter.IsPopupOpen
                || Globals.Instance.IsDownloadRecoveryPending
                || host?.Window == null)
            {
                await Task.Delay(500);
                continue;
            }

            await ShowAsync(host, hash);
            return;
        }

        Debug.WriteLine("CatalogUpdatePresenter: gave up waiting to show update prompt");
    }

    static async Task ShowAsync(Page host, string hash)
    {
        showing = true;
        try
        {
            var count = Globals.Instance.PendingCatalogDiff?.ChangeCount ?? 0;
            var viewChanges = await MainThread.InvokeOnMainThreadAsync(() => host.DisplayAlert(
                "Hymn updates available",
                $"{count} hymn{(count == 1 ? string.Empty : "s")} changed on the server. "
                    + "Sync now to get the latest lyrics and numbering.",
                "View changes",
                "Later"));

            Preferences.Set(PreferencesVar.CATALOG_UPDATE_PROMPTED_HASH, hash);
            if (!viewChanges)
                return;

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (Shell.Current?.CurrentPage is SettingsPage settings)
                {
                    settings.OpenPendingChanges();
                    return;
                }

                SettingsPage.OpenChangesOnAppear = true;
                await Shell.Current.GoToAsync($"//{Routes.SETTINGS}");
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CatalogUpdatePresenter failed: {ex.Message}");
        }
        finally
        {
            showing = false;
        }
    }
}
