using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MobiHymn4.Models;
using MobiHymn4.Views.Popups;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Newtonsoft.Json;

namespace MobiHymn4.Utils;

public static class GroupJoinWelcomePresenter
{
    static bool showing;

    public static Task ShowIfNeededAsync(Page host, WorshipGroup group, bool isCreator = false)
    {
        if (group == null || string.IsNullOrWhiteSpace(group.Id) || HasSeenWelcome(group.Id))
            return Task.CompletedTask;

        return ShowAsync(host, group, isCreator);
    }

    public static void ScheduleShow(WorshipGroup group, bool isCreator = false, Page preferredHost = null)
    {
        if (group == null || string.IsNullOrWhiteSpace(group.Id) || HasSeenWelcome(group.Id))
            return;

        _ = RunShowAsync(group, isCreator, preferredHost);
    }

    static async Task RunShowAsync(WorshipGroup group, bool isCreator, Page preferredHost)
    {
        var delays = new[] { 100, 400, 900, 1500 };
        foreach (var delay in delays)
        {
            await Task.Delay(delay);
            var host = ResolveHost(preferredHost);
            if (host?.Window == null || DownloadPopupPresenter.IsPopupOpen)
                continue;

            if (await ShowAsync(host, group, isCreator))
                return;
        }
    }

    static async Task<bool> ShowAsync(Page host, WorshipGroup group, bool isCreator)
    {
        if (showing || host?.Window == null || group == null || string.IsNullOrWhiteSpace(group.Id))
            return false;

        if (HasSeenWelcome(group.Id))
            return false;

        showing = true;
        try
        {
            var popup = new GroupJoinWelcomePopup(group, isCreator);
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            popup.Closed += (_, _) =>
            {
                MarkWelcomeSeen(group.Id);
                tcs.TrySetResult(true);
            };

            await MainThread.InvokeOnMainThreadAsync(() => host.ShowPopup(popup));
            await tcs.Task;
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GroupJoinWelcomePresenter failed: {ex.Message}");
            return false;
        }
        finally
        {
            showing = false;
        }
    }

    static Page ResolveHost(Page preferredHost)
    {
        if (preferredHost?.Window != null)
            return preferredHost;

        return Shell.Current?.CurrentPage as Page;
    }

    static bool HasSeenWelcome(string groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId))
            return true;

        return GetSeenGroupIds().Contains(groupId);
    }

    static void MarkWelcomeSeen(string groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId))
            return;

        var seen = GetSeenGroupIds();
        if (!seen.Add(groupId))
            return;

        Preferences.Set(PreferencesVar.GROUP_WELCOME_SEEN_IDS, JsonConvert.SerializeObject(seen.ToArray()));
    }

    static HashSet<string> GetSeenGroupIds()
    {
        var json = Preferences.Get(PreferencesVar.GROUP_WELCOME_SEEN_IDS, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
            return new HashSet<string>(StringComparer.Ordinal);

        try
        {
            var ids = JsonConvert.DeserializeObject<string[]>(json);
            return ids?.Length > 0
                ? new HashSet<string>(ids.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
        }
        catch
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }
}
