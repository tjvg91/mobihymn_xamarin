using System;
using Microsoft.Maui.Storage;

namespace MobiHymn4.Utils;

/// <summary>
/// Device-local last-viewed timestamps for board hymn lists (per user).
/// </summary>
public static class BoardListReadStore
{
    public static DateTime GetLastReadUtc(string userId, string groupId, string listId)
    {
        if (string.IsNullOrWhiteSpace(userId)
            || string.IsNullOrWhiteSpace(groupId)
            || string.IsNullOrWhiteSpace(listId))
            return DateTime.MinValue;

        var ticks = Preferences.Default.Get(Key(userId, groupId, listId), 0L);
        return ticks <= 0 ? DateTime.MinValue : new DateTime(ticks, DateTimeKind.Utc);
    }

    public static void SetLastReadUtc(string userId, string groupId, string listId, DateTime utc)
    {
        if (string.IsNullOrWhiteSpace(userId)
            || string.IsNullOrWhiteSpace(groupId)
            || string.IsNullOrWhiteSpace(listId))
            return;

        var value = utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime();
        if (value < DateTime.UtcNow.AddYears(-20))
            value = DateTime.UtcNow;

        Preferences.Default.Set(Key(userId, groupId, listId), value.Ticks);
    }

    static string Key(string userId, string groupId, string listId) =>
        $"board_list_read_{userId.Trim()}_{groupId.Trim()}_{listId.Trim()}";
}
