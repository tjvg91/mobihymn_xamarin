using System;
using System.Collections.Generic;
using System.Linq;
using MobiHymn4.Models;

namespace MobiHymn4.Utils;

public static class SectionSortHelper
{
    public static string GetDisplayLabel(SectionSortMode mode) => mode switch
    {
        SectionSortMode.AddedOrder => "Order added",
        SectionSortMode.DateNewest => "Date (newest first)",
        SectionSortMode.DateOldest => "Date (oldest first)",
        SectionSortMode.NameAsc => "Name (A–Z)",
        SectionSortMode.NameDesc => "Name (Z–A)",
        _ => "Order added",
    };

    public static List<(BoardHymnEntry Section, List<BoardHymnEntry> Hymns)> SortGroups(
        IList<(BoardHymnEntry Section, List<BoardHymnEntry> Hymns)> groups,
        SectionSortMode mode)
    {
        if (groups == null || groups.Count <= 1)
            return groups?.ToList() ?? new List<(BoardHymnEntry Section, List<BoardHymnEntry> Hymns)>();

        return mode switch
        {
            SectionSortMode.DateNewest => groups
                .OrderByDescending(g => g.Section.UpdatedAt)
                .ThenBy(g => g.Section.SectionName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SectionSortMode.DateOldest => groups
                .OrderBy(g => g.Section.UpdatedAt)
                .ThenBy(g => g.Section.SectionName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SectionSortMode.NameAsc => groups
                .OrderBy(g => g.Section.SectionName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Section.SortOrder)
                .ToList(),
            SectionSortMode.NameDesc => groups
                .OrderByDescending(g => g.Section.SectionName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Section.SortOrder)
                .ToList(),
            _ => groups
                .OrderBy(g => g.Section.SortOrder)
                .ThenBy(g => g.Section.SectionName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    public static bool GroupsInSameOrder(
        IList<(BoardHymnEntry Section, List<BoardHymnEntry> Hymns)> before,
        IList<(BoardHymnEntry Section, List<BoardHymnEntry> Hymns)> after)
    {
        if (before.Count != after.Count)
            return false;

        for (var i = 0; i < before.Count; i++)
        {
            if (!string.Equals(before[i].Section.Id, after[i].Section.Id, StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
