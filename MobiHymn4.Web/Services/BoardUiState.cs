using MobiHymn4.Shared.Models;

namespace MobiHymn4.Web.Services;

/// <summary>Unread board state + open-board navigation (MAUI GroupDashboardService subset).</summary>
public sealed class BoardUiState
{
    Dictionary<string, BoardListUnreadInfo> unreadByList = new(StringComparer.Ordinal);

    public event EventHandler? OpenRequested;
    public event EventHandler? UnreadChanged;

    /// <summary>List currently open in the board pane — never show an unread badge for it.</summary>
    public string? ViewingListKey { get; private set; }

    public bool HasUnread => unreadByList.Keys.Any(k =>
        ViewingListKey == null || !string.Equals(k, ViewingListKey, StringComparison.Ordinal));
    public int UnreadNotificationCount { get; private set; }
    public string? PendingGroupId { get; private set; }
    public string? PendingListId { get; private set; }
    public bool HasPendingOpen =>
        !string.IsNullOrWhiteSpace(PendingGroupId) || !string.IsNullOrWhiteSpace(PendingListId);

    public static string ListKey(string groupId, string listId) =>
        $"{groupId.Trim()}|{listId.Trim()}";

    public void SetUnreadLists(IReadOnlyDictionary<string, BoardListUnreadInfo>? unreadByListKey)
    {
        unreadByList = unreadByListKey != null
            ? new Dictionary<string, BoardListUnreadInfo>(unreadByListKey, StringComparer.Ordinal)
            : new Dictionary<string, BoardListUnreadInfo>(StringComparer.Ordinal);

        RecomputeUnreadCount();
        UnreadChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Optimistically clear unread for a list (e.g. user opened that setlist).</summary>
    public void ClearListUnread(string groupId, string listId)
    {
        if (string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(listId))
            return;
        var key = ListKey(groupId, listId);
        if (!unreadByList.Remove(key))
            return;
        RecomputeUnreadCount();
        UnreadChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// While a setlist detail is open, suppress its badge (notification deep-link or tap).
    /// Pass null when leaving list detail.
    /// </summary>
    public void SetViewingList(string? groupId, string? listId)
    {
        string? key = null;
        if (!string.IsNullOrWhiteSpace(groupId) && !string.IsNullOrWhiteSpace(listId))
            key = ListKey(groupId, listId);

        if (string.Equals(ViewingListKey, key, StringComparison.Ordinal))
            return;

        ViewingListKey = key;
        if (key != null)
            unreadByList.Remove(key);
        RecomputeUnreadCount();
        UnreadChanged?.Invoke(this, EventArgs.Empty);
    }

    public int GetListUnreadCount(string groupId, string listId)
    {
        if (string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(listId))
            return 0;
        var key = ListKey(groupId, listId);
        if (ViewingListKey != null && string.Equals(key, ViewingListKey, StringComparison.Ordinal))
            return 0;
        return unreadByList.TryGetValue(key, out var info)
            ? Math.Max(1, info.NewHymnCount)
            : 0;
    }

    void RecomputeUnreadCount()
    {
        UnreadNotificationCount = unreadByList
            .Where(kv => ViewingListKey == null
                || !string.Equals(kv.Key, ViewingListKey, StringComparison.Ordinal))
            .Sum(kv => Math.Max(1, kv.Value.NewHymnCount));
    }

    /// <summary>Remember a board to open without raising <see cref="OpenRequested"/> yet.</summary>
    public void QueueOpen(string? groupId = null, string? listId = null)
    {
        PendingGroupId = string.IsNullOrWhiteSpace(groupId) ? null : groupId.Trim();
        PendingListId = string.IsNullOrWhiteSpace(listId) ? null : listId.Trim();
    }

    public void Open(string? groupId = null, string? listId = null)
    {
        QueueOpen(groupId, listId);
        var subscriberCount = OpenRequested?.GetInvocationList().Length ?? 0;
        Console.WriteLine($"[BoardOpen] BoardUiState.Open(groupId={groupId}, listId={listId}) — {subscriberCount} listener(s) subscribed");
        OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raise <see cref="OpenRequested"/> for an already-queued pending open.</summary>
    public void RaiseOpenRequested() => OpenRequested?.Invoke(this, EventArgs.Empty);

    public void ClearPendingOpen()
    {
        PendingGroupId = null;
        PendingListId = null;
    }

    /// <summary>Hymn number typed on /number — kept in sync by Home.razor.</summary>
    public string? NumSearchHymnNumber { get; set; }

    /// <summary>When set, BoardPane adds this hymn after a setlist is opened.</summary>
    public string? PendingAddHymnNumber { get; private set; }

    public bool HasPendingAdd => !string.IsNullOrWhiteSpace(PendingAddHymnNumber);

    /// <summary>Open the board pane and queue adding <paramref name="hymnNumber"/> to a setlist.</summary>
    public void RequestAddHymn(string? hymnNumber, string? groupId = null, string? listId = null)
    {
        var n = hymnNumber?.Trim();
        if (string.IsNullOrEmpty(n))
            return;
        PendingAddHymnNumber = n;
        Open(groupId, listId);
    }

    public string? ConsumePendingAddHymn()
    {
        var n = PendingAddHymnNumber;
        PendingAddHymnNumber = null;
        return string.IsNullOrWhiteSpace(n) ? null : n.Trim();
    }

    public void ClearPendingAdd() => PendingAddHymnNumber = null;
}
