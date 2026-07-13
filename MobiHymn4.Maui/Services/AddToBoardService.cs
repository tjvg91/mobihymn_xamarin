using System;
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
            list = lists.FirstOrDefault(l => l.Id == boardContext.ActiveListId);

        if (list == null && lists.Count == 1)
            list = lists[0];

        if (list == null && host != null && lists.Count > 1)
        {
            var listNames = lists.Select(l => l.Name).ToArray();
            var picked = await host.DisplayActionSheet("Choose hymn list", "Cancel", null, listNames);
            if (string.IsNullOrWhiteSpace(picked) || picked == "Cancel")
                return false;
            list = lists.First(l => l.Name == picked);
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

    bool isOpen;
    bool hasUnread;

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
        HasUnreadNotifications = false;
    }

    public void Close() => IsOpen = false;

    public void Toggle() => IsOpen = !IsOpen;

    public void MarkNotificationsRead() => HasUnreadNotifications = false;

    public void SetUnreadNotifications(bool hasUnread) => HasUnreadNotifications = hasUnread;

    public void NotifyBoardUpdated() => HasUnreadNotifications = !IsOpen;
}
