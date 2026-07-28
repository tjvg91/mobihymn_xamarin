using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MobiHymn4.Models;
using MobiHymn4.Utils;
using Microsoft.Maui.Controls;

namespace MobiHymn4.Services;

public sealed class AddToBoardService : IAddToBoardService
{
    readonly IAuthService auth;
    readonly IProfileService profileService;
    readonly IGroupService groupService;
    readonly IBoardService boardService;
    readonly BoardContext boardContext;

    public AddToBoardService(
        IAuthService auth,
        IProfileService profileService,
        IGroupService groupService,
        IBoardService boardService,
        BoardContext boardContext)
    {
        this.auth = auth;
        this.profileService = profileService;
        this.groupService = groupService;
        this.boardService = boardService;
        this.boardContext = boardContext;
    }

    public bool CanAddToBoard =>
        auth.IsSignedIn
        && auth.IsEmailVerified
        && profileService.HasCompleteProfile
        && RolePermissions.HasLeadershipRole(profileService.CurrentProfile?.Roles)
        && profileService.CurrentProfile?.GroupIds?.Count > 0;

    public async Task<bool> TryAddHymnAsync(string hymnNumber, Page host = null)
    {
        if (!CanAddToBoard)
        {
            await ShowMessage(host, "Sign in with a leadership role and join a group to add hymns to a board.");
            return false;
        }

        var groups = await groupService.GetMyGroupsAsync();
        if (groups.Count == 0)
        {
            await ShowMessage(host, "Join a group before adding hymns to a board.");
            return false;
        }

        var group = groups.FirstOrDefault(g => g.Id == boardContext.ActiveGroupId) ?? groups[0];
        if (groups.Count > 1 && host != null)
        {
            var names = groups.Select(g => g.Name).ToArray();
            var picked = await host.DisplayActionSheet("Choose group", "Cancel", null, names);
            if (string.IsNullOrWhiteSpace(picked) || picked == "Cancel")
                return false;
            group = groups.First(g => g.Name == picked);
        }

        var lists = await boardService.ListHymnListsAsync(group.Id);
        GroupHymnListSummary list = null;

        if (!string.IsNullOrWhiteSpace(boardContext.ActiveListId))
            list = lists.Items.FirstOrDefault(l => l.Id == boardContext.ActiveListId);

        if (list == null && lists.Items.Count == 1)
            list = lists.Items[0];

        if (list == null && host != null && lists.Items.Count > 1)
        {
            var listNames = lists.Items.Select(l => l.Name).ToArray();
            var picked = await host.DisplayActionSheet("Choose hymn list", "Cancel", null, listNames);
            if (string.IsNullOrWhiteSpace(picked) || picked == "Cancel")
                return false;
            list = lists.Items.First(l => l.Name == picked);
        }

        if (list == null)
        {
            await ShowMessage(host, "Create a hymn list on the group board first.");
            return false;
        }

        var confirm = host == null
            || await host.DisplayAlert(
                "Add to board",
                $"Add Hymn #{hymnNumber} to {list.Name}?",
                "Add",
                "Cancel");

        if (!confirm)
            return false;

        try
        {
            await boardService.AddHymnAsync(group.Id, list.Id, hymnNumber);
            boardContext.ActiveGroupId = group.Id;
            boardContext.ActiveListId = list.Id;
            if (host != null)
                await ShowMessage(host, $"Hymn #{hymnNumber} added to {list.Name}.");
            return true;
        }
        catch (Exception ex)
        {
            await ShowMessage(host, ex.Message);
            return false;
        }
    }

    static async Task ShowMessage(Page host, string message)
    {
        if (host == null || string.IsNullOrWhiteSpace(message))
            return;

        await host.DisplayAlert("Board", message, "OK");
    }
}

public sealed class GroupDashboardService : IGroupDashboardService
{
    public event EventHandler IsOpenChanged;
    public event EventHandler UnreadListsChanged;

    bool isOpen;
    bool hasUnread;
    int unreadNotificationCount;
    Dictionary<string, BoardListUnreadInfo> unreadByList = new(StringComparer.Ordinal);

    public bool IsOpen
    {
        get => isOpen;
        private set
        {
            if (isOpen == value)
                return;
            isOpen = value;
            IsOpenChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool HasUnreadNotifications
    {
        get => hasUnread;
        private set
        {
            if (hasUnread == value)
                return;
            hasUnread = value;
            IsOpenChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public int UnreadNotificationCount
    {
        get => unreadNotificationCount;
        private set
        {
            if (unreadNotificationCount == value)
                return;
            unreadNotificationCount = value < 0 ? 0 : value;
            IsOpenChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    readonly BoardContext boardContext;

    public GroupDashboardService(BoardContext boardContext)
    {
        this.boardContext = boardContext;
    }

    public void Open(string groupId = null, string listId = null)
    {
        if (!string.IsNullOrWhiteSpace(groupId))
            boardContext.ActiveGroupId = groupId;
        if (!string.IsNullOrWhiteSpace(listId))
            boardContext.ActiveListId = listId;
        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    public void Toggle() => IsOpen = !IsOpen;

    public void MarkNotificationsRead()
    {
        HasUnreadNotifications = false;
        UnreadNotificationCount = 0;
        if (unreadByList.Count == 0)
            return;
        unreadByList = new Dictionary<string, BoardListUnreadInfo>(StringComparer.Ordinal);
        UnreadListsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetUnreadNotifications(bool hasUnread) => HasUnreadNotifications = hasUnread;

    public void SetUnreadLists(IReadOnlyDictionary<string, BoardListUnreadInfo> unreadByListKey)
    {
        unreadByList = unreadByListKey != null
            ? new Dictionary<string, BoardListUnreadInfo>(unreadByListKey, StringComparer.Ordinal)
            : new Dictionary<string, BoardListUnreadInfo>(StringComparer.Ordinal);

        var totalHymns = 0;
        foreach (var info in unreadByList.Values)
            totalHymns += Math.Max(1, info.NewHymnCount);

        UnreadNotificationCount = totalHymns;
        HasUnreadNotifications = unreadByList.Count > 0;
        UnreadListsChanged?.Invoke(this, EventArgs.Empty);
    }

    public int GetListUnreadCount(string groupId, string listId)
    {
        if (!unreadByList.TryGetValue(ListKey(groupId, listId), out var info))
            return 0;
        return Math.Max(1, info.NewHymnCount);
    }

    public DateTime GetListUnreadSinceUtc(string groupId, string listId)
    {
        if (!unreadByList.TryGetValue(ListKey(groupId, listId), out var info))
            return DateTime.MinValue;
        return info.OldestCreatedAtUtc;
    }

    public IReadOnlyList<BoardDeletedHymnInfo> GetListDeletedHymns(string groupId, string listId)
    {
        if (!unreadByList.TryGetValue(ListKey(groupId, listId), out var info)
            || info.DeletedHymns == null
            || info.DeletedHymns.Count == 0)
            return Array.Empty<BoardDeletedHymnInfo>();
        return info.DeletedHymns;
    }

    public void NotifyBoardUpdated() => HasUnreadNotifications = true;

    public static string ListKey(string groupId, string listId) =>
        $"{groupId?.Trim() ?? string.Empty}\n{listId?.Trim() ?? string.Empty}";
}
