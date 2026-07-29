using System;
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
        if (page == null || hymn == null || string.IsNullOrWhiteSpace(hymn.Number))
            return;

        var globals = Globals.Instance;
        if (globals.IsBookmarked(hymn.Number))
        {
            Globals.ShowToastPopup(
                "bookmark-saved",
                "Already bookmarked.",
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

            if (globals.AddBookmark(hymn.Number, groupName, hymn.Line))
            {
                Globals.ShowToastPopup(
                    "bookmark-saved",
                    "Bookmark added.",
                    DeviceInfo.Platform == DevicePlatform.Android ? 120 : 0.5);
            }
            else if (globals.IsBookmarked(hymn.Number))
            {
                Globals.ShowToastPopup(
                    "bookmark-saved",
                    "Already bookmarked.",
                    DeviceInfo.Platform == DevicePlatform.Android ? 120 : 0.5);
            }
        };

        page.ShowPopup(popup);
    }
}
