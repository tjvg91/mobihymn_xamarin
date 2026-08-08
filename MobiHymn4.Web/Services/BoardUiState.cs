using MobiHymn4.Shared.Models;

namespace MobiHymn4.Web.Services;

/// <summary>Unread board state + open-board navigation (MAUI GroupDashboardService subset).</summary>
public sealed class BoardUiState
{
    Dictionary<string, BoardListUnreadInfo> unreadByList = new(StringComparer.Ordinal);

    public event EventHandler? UnreadChanged;
    public event EventHandler? OpenRequested;

    public bool HasUnread => unreadByList.Count > 0;
    public int UnreadNotificationCount { get; private set; }
    public string? PendingGroupId { get; private set; }
    public string? PendingListId { get; private set; }

    public static string ListKey(string groupId, string listId) =>
        $"{groupId.Trim()}|{listId.Trim()}";

    public void SetUnreadLists(IReadOnlyDictionary<string, BoardListUnreadInfo>? unreadByListKey)
    {
        unreadByList = unreadByListKey != null
            ? new Dictionary<string, BoardListUnreadInfo>(unreadByListKey, StringComparer.Ordinal)
            : new Dictionary<string, BoardListUnreadInfo>(StringComparer.Ordinal);

        UnreadNotificationCount = unreadByList.Values.Sum(v => Math.Max(1, v.NewHymnCount));
        UnreadChanged?.Invoke(this, EventArgs.Empty);
    }

    public int GetListUnreadCount(string groupId, string listId)
    {
        if (string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(listId))
            return 0;
        return unreadByList.TryGetValue(ListKey(groupId, listId), out var info)
            ? Math.Max(1, info.NewHymnCount)
            : 0;
    }

    public void Open(string? groupId = null, string? listId = null)
    {
        PendingGroupId = string.IsNullOrWhiteSpace(groupId) ? null : groupId.Trim();
        PendingListId = string.IsNullOrWhiteSpace(listId) ? null : listId.Trim();
        OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ClearPendingOpen()
    {
        PendingGroupId = null;
        PendingListId = null;
    }
}
