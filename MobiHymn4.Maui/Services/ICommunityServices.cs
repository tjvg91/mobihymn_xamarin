using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MobiHymn4.Models;
using MobiHymn4.Utils;
using Microsoft.Maui.Controls;

namespace MobiHymn4.Services;

public interface IProfileService
{
    event EventHandler ProfileChanged;

    UserProfile CurrentProfile { get; }
    bool HasCompleteProfile { get; }

    Task<UserProfile> LoadProfileAsync(string uid);
    Task SaveProfileAsync(UserProfile profile);
    Task SetNotificationsMutedAsync(bool muted);
    Task RefreshCurrentProfileAsync(bool force = false);
}

public interface IGroupService
{
    Task<IReadOnlyList<WorshipGroup>> GetMyGroupsAsync();
    Task<WorshipGroup> CreateGroupAsync(string name);
    Task<WorshipGroup> JoinGroupByCodeAsync(string joinCode);
    Task<WorshipGroup> JoinGroupByIdAsync(string groupId);
    Task InviteByEmailAsync(string groupId, string email);
    Task<IReadOnlyList<GroupMember>> GetMembersAsync(string groupId);
    Task SetMemberAdminAsync(string groupId, string memberId, bool isAdmin);
    Task RemoveMemberAsync(string groupId, string memberId);
    Task<bool> IsGroupNotificationsMutedAsync(string groupId);
    Task SetGroupNotificationsMutedAsync(string groupId, bool muted);
    Task LeaveGroupAsync(string groupId);
    Task<IReadOnlyList<WorshipGroup>> AcceptPendingInvitesAsync();
}

public interface IBoardService
{
    event EventHandler<GroupHymnList> HymnListChanged;

    Task<BoardListsPage> ListHymnListsAsync(
        string groupId,
        GroupHymnListSummary startAfter = null,
        int pageSize = BoardListsPage.DefaultPageSize);
    Task<GroupHymnList> CreateHymnListAsync(string groupId, DateTime date);
    Task<bool> HymnListExistsForDateAsync(string groupId, DateTime date, string excludeListId = null);
    Task<GroupHymnList> UpdateHymnListDateAsync(string groupId, string listId, DateTime date);
    Task DeleteHymnListAsync(string groupId, string listId);
    Task<GroupHymnList> GetHymnListAsync(string groupId, string listId);
    IDisposable SubscribeHymnList(string groupId, string listId, Action<GroupHymnList> onChanged);
    Task<(IDisposable Subscription, GroupHymnList Initial)> SubscribeHymnListWithInitialAsync(
        string groupId,
        string listId,
        Action<GroupHymnList> onChanged,
        TimeSpan? initialTimeout = null);
    Task AddHymnAsync(string groupId, string listId, string hymnNumber, string notes = null);
    Task AddSectionAsync(string groupId, string listId, string sectionName);
    Task RemoveHymnAsync(string groupId, string listId, string entryId);
    Task RemoveSectionAsync(string groupId, string listId, string sectionId, bool deleteHymnsInSection);
    Task UpdateHymnAsync(string groupId, string listId, BoardHymnEntry entry);
    Task ReorderHymnsAsync(string groupId, string listId, IList<BoardHymnEntry> hymns);
    Task ClearAllSectionsInListAsync(string groupId, string listId);
    Task ClearAllSectionsAsync(string groupId, string listId);
    Task ClearAllHymnsAsync(string groupId, string listId);
    Task<BoardSectionTemplate> GetSectionTemplateAsync(string groupId);
    Task<BoardSectionTemplate> SaveSectionToTemplateAsync(string groupId, string listId, string sectionName);
    Task RemoveSectionFromTemplateAsync(string groupId, string sectionName);
    Task DisableSectionAutoApplyAsync(string groupId);
    Task<GroupHymnList> EnsureSavedSectionsOnListAsync(
        string groupId,
        string listId,
        bool persistIfChanged,
        BoardSectionTemplate template = null,
        GroupHymnList list = null);
    Task<BoardSectionTemplate> SetSectionSortAsync(string groupId, string listId, SectionSortMode sort);
    Task<BoardSectionTemplate> SetSectionSortPreferenceAsync(string groupId, SectionSortMode sort);
    GroupHymnList MergeSavedSections(GroupHymnList list, BoardSectionTemplate template);
    GroupHymnList ApplySectionSort(GroupHymnList list, SectionSortMode sort);
}

public interface IAddToBoardService
{
    bool CanAddToBoard { get; }
    Task<bool> TryAddHymnAsync(string hymnNumber, Page host = null);
}

public interface IGroupDashboardService
{
    event EventHandler IsOpenChanged;
    event EventHandler UnreadListsChanged;

    bool IsOpen { get; }
    bool HasUnreadNotifications { get; }
    int UnreadNotificationCount { get; }

    void Open(string groupId = null, string listId = null);
    void Close();
    void Toggle();
    void MarkNotificationsRead();
    void SetUnreadNotifications(bool hasUnread);
    void SetUnreadLists(IReadOnlyDictionary<string, BoardListUnreadInfo> unreadByListKey);
    int GetListUnreadCount(string groupId, string listId);
    DateTime GetListUnreadSinceUtc(string groupId, string listId);
    IReadOnlyList<BoardDeletedHymnInfo> GetListDeletedHymns(string groupId, string listId);
}

public sealed class BoardDeletedHymnInfo
{
    public string Id { get; init; } = string.Empty;
    public string HymnNumber { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public string DeletedByName { get; init; } = string.Empty;
    public DateTime DeletedAtUtc { get; init; }
}

public sealed class BoardListUnreadInfo
{
    public int NewHymnCount { get; init; }
    public DateTime OldestCreatedAtUtc { get; init; }
    public IReadOnlyList<BoardDeletedHymnInfo> DeletedHymns { get; init; }
        = Array.Empty<BoardDeletedHymnInfo>();
}

public interface IBoardNotificationService
{
    Task StartAsync();
    Task StopAsync();
    Task RegisterTokenAsync();
    Task MarkAllReadAsync();
    Task MarkListReadAsync(string groupId, string listId);
}
