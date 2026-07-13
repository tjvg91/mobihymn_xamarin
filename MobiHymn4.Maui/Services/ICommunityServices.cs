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
    Task RefreshCurrentProfileAsync();
}

public interface IGroupService
{
    Task<IReadOnlyList<WorshipGroup>> GetMyGroupsAsync();
    Task<WorshipGroup> CreateGroupAsync(string name);
    Task<WorshipGroup> JoinGroupByCodeAsync(string joinCode);
    Task<WorshipGroup> JoinGroupByIdAsync(string groupId);
    Task InviteByEmailAsync(string groupId, string email);
    Task<IReadOnlyList<GroupMember>> GetMembersAsync(string groupId);
    Task LeaveGroupAsync(string groupId);
    Task<IReadOnlyList<WorshipGroup>> AcceptPendingInvitesAsync();
}

public interface IBoardService
{
    event EventHandler<GroupHymnList> HymnListChanged;

    Task<IReadOnlyList<GroupHymnListSummary>> ListHymnListsAsync(string groupId);
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

    bool IsOpen { get; }
    bool HasUnreadNotifications { get; }

    void Open(string groupId = null, string listId = null);
    void Close();
    void Toggle();
    void MarkNotificationsRead();
    void SetUnreadNotifications(bool hasUnread);
}

public interface IBoardNotificationService
{
    Task StartAsync();
    Task StopAsync();
    Task RegisterTokenAsync();
    Task MarkAllReadAsync();
}
