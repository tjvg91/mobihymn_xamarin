using System;
using System.Collections.Generic;
using System.Linq;
using MobiHymn4.Models;
using MobiHymn4.Views.Popups;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;

namespace MobiHymn4.Utils;

public static class BookmarkSaveHelper
{
    /// <summary>
    /// Shows the Save-to-group popup and bookmarks <paramref name="hymn"/> into the chosen group.
    /// Group name is required (existing picker selection or a new name).
    /// </summary>
    public static void ShowSavePopup(Page page, ShortHymn hymn)
    {
        if (hymn == null)
            return;
        ShowSavePopup(page, new[] { hymn });
    }

    /// <summary>
    /// Shows the Save-to-group popup and bookmarks all non-bookmarked hymns into the chosen group.
    /// </summary>
    public static void ShowSavePopup(Page page, IEnumerable<ShortHymn> hymns)
    {
        if (page == null || hymns == null)
            return;

        var globals = Globals.Instance;
        var pending = hymns
            .Where(h => h != null && !string.IsNullOrWhiteSpace(h.Number) && !globals.IsBookmarked(h.Number))
            .GroupBy(h => h.Number.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (pending.Count == 0)
        {
            Globals.ShowToastPopup(
                "bookmark-saved",
                "All already bookmarked.",
                DeviceInfo.Platform == DevicePlatform.Android ? 120 : 0.5);
            return;
        }

        var groups = (globals.BookmarkList ?? new MvvmHelpers.ObservableRangeCollection<ShortHymn>())
            .Select(bk => string.IsNullOrWhiteSpace(bk.BookmarkGroup) ? "General" : bk.BookmarkGroup.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var popup = new InputPopup
        {
            Title = "Save to",
            ActionString = "Save",
            Validation = newKey =>
                groups.Any(g => g.Equals(newKey, StringComparison.OrdinalIgnoreCase))
                    ? "Group already exists."
                    : string.Empty
        };
        popup.SetGroups(groups);
        popup.OK += (sender, _) =>
        {
            var groupName = sender as string;
            if (string.IsNullOrWhiteSpace(groupName))
                return;

            var added = 0;
            foreach (var hymn in pending)
            {
                if (globals.AddBookmark(hymn.Number, groupName, hymn.Line))
                    added++;
            }

            var msg = added == 0
                ? (pending.Count == 1 ? "Already bookmarked." : "All already bookmarked.")
                : added == 1
                    ? "Bookmark added."
                    : $"{added} bookmarks added.";
            Globals.ShowToastPopup(
                "bookmark-saved",
                msg,
                DeviceInfo.Platform == DevicePlatform.Android ? 120 : 0.5);
        };

        page.ShowPopup(popup);
    }
}
