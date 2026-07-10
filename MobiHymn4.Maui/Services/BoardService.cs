using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MobiHymn4.Models;
using MobiHymn4.Models.Firestore;
using MobiHymn4.Utils;
using Plugin.Firebase.Firestore;

namespace MobiHymn4.Services;

public sealed class BoardService : IBoardService
{
    readonly IFirebaseFirestoreAccessor firebase;
    readonly IAuthService auth;
    readonly IProfileService profileService;
    readonly Dictionary<string, (DateTime LoadedAt, IReadOnlyList<GroupHymnListSummary> Summaries)> summaryCache = new(StringComparer.Ordinal);
    readonly Dictionary<string, (DateTime LoadedAt, BoardSectionTemplate Template)> templateCache = new(StringComparer.Ordinal);
    string cachedListGroupId;
    string cachedListId;
    GroupHymnList cachedList;
    DateTime cachedListLoadedAt;
    static readonly TimeSpan BoardCacheTtl = TimeSpan.FromSeconds(60);
    static readonly TimeSpan ListCacheTtl = TimeSpan.FromSeconds(45);

    public BoardService(IFirebaseFirestoreAccessor firebase, IAuthService auth, IProfileService profileService)
    {
        this.firebase = firebase;
        this.auth = auth;
        this.profileService = profileService;
    }

    public event EventHandler<GroupHymnList> HymnListChanged;

    public async Task<IReadOnlyList<GroupHymnListSummary>> ListHymnListsAsync(string groupId)
    {
        if (TryGetCachedSummaries(groupId, out var cached))
            return cached;

        try
        {
            var snapshot = await firebase.Firestore
                .GetCollection(FirestorePaths.Groups)
                .GetDocument(groupId)
                .GetCollection(FirestorePaths.Boards)
                .GetDocumentsAsync<BoardFirestoreDocument>();

            var summaries = snapshot?.Documents?
                .Select(d => FirestoreMappers.ToGroupHymnListSummary(d.Data?.Id ?? string.Empty, d.Data))
                .OrderByDescending(l => l.CreatedAt)
                .ToList() ?? new List<GroupHymnListSummary>();

            CacheSummaries(groupId, summaries);
            return summaries;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ListHymnListsAsync failed: {ex.Message}");
            return Array.Empty<GroupHymnListSummary>();
        }
    }

    public async Task<bool> HymnListExistsForDateAsync(string groupId, DateTime date, string excludeListId = null)
    {
        var target = date.Date;
        try
        {
            var snapshot = await firebase.Firestore
                .GetCollection(FirestorePaths.Groups)
                .GetDocument(groupId)
                .GetCollection(FirestorePaths.Boards)
                .GetDocumentsAsync<BoardFirestoreDocument>();

            if (snapshot?.Documents == null)
                return false;

            foreach (var doc in snapshot.Documents)
            {
                var listId = doc.Data?.Id ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(excludeListId)
                    && string.Equals(listId, excludeListId, StringComparison.Ordinal))
                    continue;

                var name = doc.Data?.Name;
                if (GroupHymnListDates.TryGetScheduledDate(listId, name, out var existing)
                    && existing.Date == target)
                    return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HymnListExistsForDateAsync failed: {ex.Message}");
        }

        return false;
    }

    public async Task<GroupHymnList> CreateHymnListAsync(string groupId, DateTime date)
    {
        EnsureCanEdit();
        var scheduled = date.Date;
        if (await HymnListExistsForDateAsync(groupId, scheduled))
            throw new InvalidOperationException($"A hymn list already exists for {GroupHymnListDates.FormatName(scheduled)}.");

        var now = DateTime.UtcNow;
        var listId = GroupHymnListDates.ToDateKey(scheduled);
        var list = new GroupHymnList
        {
            Id = listId,
            GroupId = groupId,
            Name = GroupHymnListDates.FormatName(scheduled),
            CreatedAt = scheduled,
            CreatedBy = auth.CurrentUserId,
            UpdatedAt = now,
            UpdatedBy = auth.CurrentUserId,
        };

        await SaveHymnListAsync(groupId, list);
        return list;
    }

    public async Task<GroupHymnList> UpdateHymnListDateAsync(string groupId, string listId, DateTime date)
    {
        EnsureCanEdit();
        var scheduled = date.Date;
        var list = await GetHymnListAsync(groupId, listId);

        var currentScheduled = list.CreatedAt.Date;
        if (GroupHymnListDates.TryGetScheduledDate(list.Id, list.Name, out var parsed))
            currentScheduled = parsed.Date;

        var newListId = GroupHymnListDates.ToDateKey(scheduled);
        if (currentScheduled == scheduled
            && string.Equals(list.Id, newListId, StringComparison.Ordinal))
            return list;

        if (await HymnListExistsForDateAsync(groupId, scheduled, listId))
            throw new InvalidOperationException($"A hymn list already exists for {GroupHymnListDates.FormatName(scheduled)}.");

        var oldListId = list.Id;
        list.Id = newListId;
        list.Name = GroupHymnListDates.FormatName(scheduled);
        list.CreatedAt = scheduled;

        await SaveHymnListAsync(groupId, list);

        if (!string.Equals(oldListId, newListId, StringComparison.Ordinal))
            await DeleteHymnListDocumentAsync(groupId, oldListId);

        return list;
    }

    public async Task DeleteHymnListAsync(string groupId, string listId)
    {
        EnsureCanEdit();
        if (string.IsNullOrWhiteSpace(listId))
            throw new InvalidOperationException("Hymn list not found.");

        await DeleteHymnListDocumentAsync(groupId, listId);
    }

    async Task DeleteHymnListDocumentAsync(string groupId, string listId)
    {
        await firebase.Firestore
            .GetCollection(FirestorePaths.Groups)
            .GetDocument(groupId)
            .GetCollection(FirestorePaths.Boards)
            .GetDocument(listId)
            .DeleteDocumentAsync();
        InvalidateBoardCache(groupId);
    }

    public async Task<GroupHymnList> GetHymnListAsync(string groupId, string listId)
    {
        if (TryGetCachedList(groupId, listId, out var cached))
            return cached;

        try
        {
            var snapshot = await firebase.Firestore
                .GetCollection(FirestorePaths.Groups)
                .GetDocument(groupId)
                .GetCollection(FirestorePaths.Boards)
                .GetDocument(listId)
                .GetDocumentSnapshotAsync<BoardFirestoreDocument>();

            var list = FirestoreMappers.ToGroupHymnList(groupId, listId, snapshot?.Data);
            CacheList(groupId, listId, list);
            return list;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetHymnListAsync failed: {ex.Message}");
            return FirestoreMappers.ToGroupHymnList(groupId, listId, null);
        }
    }

    public async Task<(IDisposable Subscription, GroupHymnList Initial)> SubscribeHymnListWithInitialAsync(
        string groupId,
        string listId,
        Action<GroupHymnList> onChanged,
        TimeSpan? initialTimeout = null)
    {
        var tcs = new TaskCompletionSource<GroupHymnList>(TaskCreationOptions.RunContinuationsAsynchronously);
        var initialDelivered = 0;
        var timeout = initialTimeout ?? TimeSpan.FromSeconds(8);

        IDisposable registration = firebase.Firestore
            .GetCollection(FirestorePaths.Groups)
            .GetDocument(groupId)
            .GetCollection(FirestorePaths.Boards)
            .GetDocument(listId)
            .AddSnapshotListener<BoardFirestoreDocument>(
                snapshot =>
                {
                    try
                    {
                        var list = FirestoreMappers.ToGroupHymnList(groupId, listId, snapshot?.Data);
                        CacheList(groupId, listId, list);

                        if (Interlocked.CompareExchange(ref initialDelivered, 1, 0) == 0)
                            tcs.TrySetResult(list);
                        else
                            onChanged?.Invoke(list);

                        HymnListChanged?.Invoke(this, list);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Hymn list snapshot handler failed: {ex.Message}");
                    }
                },
                ex => Debug.WriteLine($"Hymn list subscription error: {ex?.Message}"));

        var disposable = new SubscriptionDisposable(() => registration?.Dispose());

        try
        {
            var initial = await tcs.Task.WaitAsync(timeout);
            return (disposable, initial);
        }
        catch (TimeoutException)
        {
            var fallback = await GetHymnListAsync(groupId, listId);
            if (Interlocked.CompareExchange(ref initialDelivered, 1, 0) == 0)
                tcs.TrySetResult(fallback);
            return (disposable, fallback);
        }
    }

    public IDisposable SubscribeHymnList(string groupId, string listId, Action<GroupHymnList> onChanged)
    {
        IDisposable registration = firebase.Firestore
            .GetCollection(FirestorePaths.Groups)
            .GetDocument(groupId)
            .GetCollection(FirestorePaths.Boards)
            .GetDocument(listId)
            .AddSnapshotListener<BoardFirestoreDocument>(
                snapshot =>
                {
                    try
                    {
                        var list = FirestoreMappers.ToGroupHymnList(groupId, listId, snapshot?.Data);
                        CacheList(groupId, listId, list);
                        onChanged?.Invoke(list);
                        HymnListChanged?.Invoke(this, list);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Hymn list snapshot handler failed: {ex.Message}");
                    }
                },
                ex => Debug.WriteLine($"Hymn list subscription error: {ex?.Message}"));

        return new SubscriptionDisposable(() => registration?.Dispose());
    }

    sealed class SubscriptionDisposable : IDisposable
    {
        Action remove;

        public SubscriptionDisposable(Action remove)
        {
            this.remove = remove;
        }

        public void Dispose()
        {
            var action = remove;
            remove = null;
            if (action == null)
                return;
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hymn list subscription dispose failed: {ex.Message}");
            }
        }
    }

    public async Task AddHymnAsync(string groupId, string listId, string hymnNumber, string notes = null)
    {
        EnsureCanEdit();
        var list = await GetHymnListAsync(groupId, listId);
        var normalized = hymnNumber?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("Enter a hymn number.");

        if (list.Hymns.Any(h => !h.IsSection
            && string.Equals(h.HymnNumber, normalized, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("That hymn is already on the board.");

        var profile = profileService.CurrentProfile;
        list.Hymns.Add(new BoardHymnEntry
        {
            HymnNumber = normalized,
            SortOrder = list.Hymns.Count,
            Notes = notes ?? string.Empty,
            AddedBy = profile.Uid,
            AddedByName = profile.DisplayName,
            UpdatedAt = DateTime.UtcNow,
        });

        await SaveHymnListAsync(groupId, list);
    }

    public async Task AddSectionAsync(string groupId, string listId, string sectionName)
    {
        EnsureCanEdit();
        var name = sectionName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Enter a section name.");

        var list = await GetHymnListAsync(groupId, listId);
        var profile = profileService.CurrentProfile;
        list.Hymns.Add(new BoardHymnEntry
        {
            IsSection = true,
            SectionName = name,
            SortOrder = list.Hymns.Count,
            AddedBy = profile.Uid,
            AddedByName = profile.DisplayName,
            UpdatedAt = DateTime.UtcNow,
        });

        await SaveHymnListAsync(groupId, list);
    }

    public async Task RemoveHymnAsync(string groupId, string listId, string entryId)
    {
        var list = await GetHymnListAsync(groupId, listId);
        var existing = list.Hymns.FirstOrDefault(h => h.Id == entryId);
        if (existing == null)
            return;

        EnsureCanEditEntry(existing);
        list.Hymns = list.Hymns.Where(h => h.Id != entryId).ToList();
        Renumber(list.Hymns);
        await SaveHymnListAsync(groupId, list);
    }

    public async Task RemoveSectionAsync(string groupId, string listId, string sectionId, bool deleteHymnsInSection)
    {
        var list = await GetHymnListAsync(groupId, listId);
        var sectionIndex = list.Hymns.FindIndex(h => h.Id == sectionId);
        if (sectionIndex < 0)
            return;

        EnsureCanEditEntry(list.Hymns[sectionIndex]);

        var idsToRemove = new HashSet<string> { sectionId };
        if (deleteHymnsInSection)
        {
            for (var i = sectionIndex + 1; i < list.Hymns.Count; i++)
            {
                if (list.Hymns[i].IsSection)
                    break;

                idsToRemove.Add(list.Hymns[i].Id);
            }
        }

        list.Hymns = list.Hymns.Where(h => !idsToRemove.Contains(h.Id)).ToList();
        Renumber(list.Hymns);
        await SaveHymnListAsync(groupId, list);
    }

    public async Task UpdateHymnAsync(string groupId, string listId, BoardHymnEntry entry)
    {
        var list = await GetHymnListAsync(groupId, listId);
        var index = list.Hymns.FindIndex(h => h.Id == entry.Id);
        if (index < 0)
            throw new InvalidOperationException("Hymn entry not found.");

        EnsureCanEditEntry(list.Hymns[index]);

        var original = list.Hymns[index];
        entry.IsSection = original.IsSection;
        entry.AddedBy = original.AddedBy;
        entry.AddedByName = original.AddedByName;
        entry.SortOrder = original.SortOrder;
        entry.UpdatedAt = DateTime.UtcNow;
        list.Hymns[index] = entry;
        await SaveHymnListAsync(groupId, list);
    }

    public async Task ReorderHymnsAsync(string groupId, string listId, IList<BoardHymnEntry> hymns)
    {
        EnsureCanEdit();
        var list = await GetHymnListAsync(groupId, listId);
        list.Hymns = hymns?.ToList() ?? new List<BoardHymnEntry>();
        Renumber(list.Hymns);
        await SaveHymnListAsync(groupId, list);
    }

    public async Task ClearAllSectionsInListAsync(string groupId, string listId)
    {
        EnsureCanEdit();
        var list = await GetHymnListAsync(groupId, listId);
        list.Hymns = list.Hymns.Where(h => !h.IsSection).ToList();
        Renumber(list.Hymns);
        await SaveHymnListAsync(groupId, list);
    }

    public async Task ClearAllSectionsAsync(string groupId, string listId)
    {
        EnsureCanEdit();
        var template = await GetSectionTemplateAsync(groupId);
        template.SectionNames = new List<string>();
        template.AutoApply = false;
        await SaveSectionTemplateAsync(groupId, template);
        await ClearAllSectionsInListAsync(groupId, listId);
    }

    public async Task ClearAllHymnsAsync(string groupId, string listId)
    {
        EnsureCanEdit();
        var template = await GetSectionTemplateAsync(groupId);
        var savedNames = template.SectionNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var list = await GetHymnListAsync(groupId, listId);
        list.Hymns = list.Hymns
            .Where(h => h.IsSection && savedNames.Contains(h.SectionName?.Trim() ?? string.Empty))
            .ToList();
        Renumber(list.Hymns);
        await SaveHymnListAsync(groupId, list);
    }

    public async Task<BoardSectionTemplate> GetSectionTemplateAsync(string groupId)
    {
        if (TryGetCachedTemplate(groupId, out var cached))
            return cached;

        try
        {
            var snapshot = await firebase.Firestore
                .GetCollection(FirestorePaths.Groups)
                .GetDocument(groupId)
                .GetCollection(FirestorePaths.SectionTemplate)
                .GetDocument("default")
                .GetDocumentSnapshotAsync<BoardSectionTemplateFirestoreDocument>();

            var template = FirestoreMappers.ToBoardSectionTemplate(snapshot?.Data);
            CacheTemplate(groupId, template);
            return template;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetSectionTemplateAsync failed: {ex.Message}");
            return new BoardSectionTemplate();
        }
    }

    public async Task<BoardSectionTemplate> SaveSectionToTemplateAsync(string groupId, string listId, string sectionName)
    {
        EnsureCanEdit();
        var trimmed = sectionName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        var list = await GetHymnListAsync(groupId, listId);
        var listSections = list.Hymns
            .Where(h => h.IsSection && !string.IsNullOrWhiteSpace(h.SectionName))
            .Select(h => h.SectionName.Trim())
            .ToList();

        if (!listSections.Any(n => string.Equals(n, trimmed, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Section not found on the board.");

        var template = await GetSectionTemplateAsync(groupId);
        var savedSet = template.SectionNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        savedSet.Add(trimmed);

        template.SectionNames = listSections
            .Where(n => savedSet.Contains(n))
            .ToList();
        template.AutoApply = true;
        template.ApplyFrequency = SectionApplyFrequency.Daily;
        await SaveSectionTemplateAsync(groupId, template);
        return template;
    }

    public async Task RemoveSectionFromTemplateAsync(string groupId, string sectionName)
    {
        EnsureCanEdit();
        var trimmed = sectionName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return;

        var template = await GetSectionTemplateAsync(groupId);
        template.SectionNames = template.SectionNames
            .Where(n => !string.Equals(n?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (template.SectionNames.Count == 0)
            template.AutoApply = false;

        await SaveSectionTemplateAsync(groupId, template);
    }

    public async Task DisableSectionAutoApplyAsync(string groupId)
    {
        EnsureCanEdit();
        var template = await GetSectionTemplateAsync(groupId);
        template.AutoApply = false;
        await SaveSectionTemplateAsync(groupId, template);
    }

    public async Task<GroupHymnList> EnsureSavedSectionsOnListAsync(
        string groupId,
        string listId,
        bool persistIfChanged,
        BoardSectionTemplate template = null,
        GroupHymnList list = null)
    {
        template ??= await GetSectionTemplateAsync(groupId);
        list ??= await GetHymnListAsync(groupId, listId);
        var changed = false;

        if (ShouldShowSavedSections(template))
            changed |= MergeSavedSectionsIfNeeded(list, template);

        changed |= ApplySectionSortIfNeeded(list, template?.SectionSort ?? SectionSortMode.AddedOrder);

        if (!changed)
            return null;

        if (persistIfChanged && IsLeader())
            await SaveHymnListAsync(groupId, list);

        return list;
    }

    public async Task<BoardSectionTemplate> SetSectionSortPreferenceAsync(string groupId, SectionSortMode sort)
    {
        EnsureCanEdit();
        var template = await GetSectionTemplateAsync(groupId);
        template.SectionSort = sort;
        await SaveSectionTemplateAsync(groupId, template);
        return template;
    }

    public async Task<BoardSectionTemplate> SetSectionSortAsync(string groupId, string listId, SectionSortMode sort)
    {
        EnsureCanEdit();
        var template = await GetSectionTemplateAsync(groupId);
        template.SectionSort = sort;
        await SaveSectionTemplateAsync(groupId, template);

        var list = await GetHymnListAsync(groupId, listId);
        if (ShouldShowSavedSections(template))
            MergeSavedSectionsIfNeeded(list, template);

        if (ApplySectionSortIfNeeded(list, sort))
            await SaveHymnListAsync(groupId, list);

        return template;
    }

    public GroupHymnList ApplySectionSort(GroupHymnList list, SectionSortMode sort)
    {
        if (list == null)
            return null;

        ApplySectionSortIfNeeded(list, sort);
        return list;
    }

    public GroupHymnList MergeSavedSections(GroupHymnList list, BoardSectionTemplate template)
    {
        if (list == null || template == null)
            return list;

        MergeSavedSectionsIfNeeded(list, template);
        ApplySectionSortIfNeeded(list, template.SectionSort);
        return list;
    }

    static bool ShouldShowSavedSections(BoardSectionTemplate template) =>
        template != null
        && template.AutoApply
        && template.SectionNames.Count > 0;

    bool MergeSavedSectionsIfNeeded(GroupHymnList list, BoardSectionTemplate template)
    {
        var savedNames = template.SectionNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .ToList();
        if (savedNames.Count == 0)
            return false;

        var entries = list.Hymns?.OrderBy(h => h.SortOrder).ToList() ?? new List<BoardHymnEntry>();
        var (leadingHymns, sectionGroups) = ParseSectionGroups(entries);

        var existingNames = sectionGroups
            .Select(g => g.Section.SectionName?.Trim())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var name in savedNames)
        {
            if (existingNames.Contains(name))
                continue;

            sectionGroups.Add((CreateSectionEntry(name, profileService.CurrentProfile), new List<BoardHymnEntry>()));
            changed = true;
        }

        if (!changed)
            return false;

        list.Hymns = BuildBoardEntries(leadingHymns, sectionGroups);
        Renumber(list.Hymns);
        return true;
    }

    bool ApplySectionSortIfNeeded(GroupHymnList list, SectionSortMode sort)
    {
        var entries = list.Hymns?.OrderBy(h => h.SortOrder).ToList() ?? new List<BoardHymnEntry>();
        if (!entries.Any(h => h.IsSection))
            return false;

        var (leadingHymns, sectionGroups) = ParseSectionGroups(entries);
        if (sectionGroups.Count <= 1)
            return false;

        var sortedGroups = SectionSortHelper.SortGroups(sectionGroups, sort);
        if (SectionSortHelper.GroupsInSameOrder(sectionGroups, sortedGroups))
            return false;

        list.Hymns = BuildBoardEntries(leadingHymns, sortedGroups);
        Renumber(list.Hymns);
        return true;
    }

    static List<BoardHymnEntry> BuildBoardEntries(
        List<BoardHymnEntry> leadingHymns,
        List<(BoardHymnEntry Section, List<BoardHymnEntry> Hymns)> sectionGroups)
    {
        var merged = new List<BoardHymnEntry>();
        merged.AddRange(leadingHymns);
        foreach (var group in sectionGroups)
        {
            merged.Add(group.Section);
            merged.AddRange(group.Hymns);
        }

        return merged;
    }

    static (List<BoardHymnEntry> LeadingHymns, List<(BoardHymnEntry Section, List<BoardHymnEntry> Hymns)> Groups)
        ParseSectionGroups(IList<BoardHymnEntry> entries)
    {
        var leadingHymns = new List<BoardHymnEntry>();
        var groups = new List<(BoardHymnEntry Section, List<BoardHymnEntry> Hymns)>();
        BoardHymnEntry currentSection = null;
        List<BoardHymnEntry> currentHymns = null;

        foreach (var entry in entries)
        {
            if (entry.IsSection)
            {
                if (currentSection != null)
                    groups.Add((currentSection, currentHymns ?? new List<BoardHymnEntry>()));

                currentSection = entry;
                currentHymns = new List<BoardHymnEntry>();
                continue;
            }

            if (currentSection == null)
                leadingHymns.Add(entry);
            else
                currentHymns.Add(entry);
        }

        if (currentSection != null)
            groups.Add((currentSection, currentHymns ?? new List<BoardHymnEntry>()));

        return (leadingHymns, groups);
    }

    async Task SaveSectionTemplateAsync(string groupId, BoardSectionTemplate template)
    {
        var doc = FirestoreMappers.ToFirestore(template, auth.CurrentUserId);
        await firebase.Firestore
            .GetCollection(FirestorePaths.Groups)
            .GetDocument(groupId)
            .GetCollection(FirestorePaths.SectionTemplate)
            .GetDocument("default")
            .SetDataAsync(doc);
        templateCache.Remove(groupId);
    }

    static BoardHymnEntry CreateSectionEntry(string name, UserProfile profile) =>
        new()
        {
            IsSection = true,
            SectionName = name.Trim(),
            AddedBy = profile?.Uid ?? string.Empty,
            AddedByName = profile?.DisplayName ?? string.Empty,
            UpdatedAt = DateTime.UtcNow,
        };

    async Task SaveHymnListAsync(string groupId, GroupHymnList list)
    {
        list.UpdatedAt = DateTime.UtcNow;
        list.UpdatedBy = auth.CurrentUserId;
        var doc = FirestoreMappers.ToFirestore(list);
        await firebase.Firestore
            .GetCollection(FirestorePaths.Groups)
            .GetDocument(groupId)
            .GetCollection(FirestorePaths.Boards)
            .GetDocument(list.Id)
            .SetDataAsync(doc);
        InvalidateBoardCache(groupId);
    }

    bool TryGetCachedSummaries(string groupId, out IReadOnlyList<GroupHymnListSummary> summaries)
    {
        summaries = null;
        if (!summaryCache.TryGetValue(groupId, out var entry))
            return false;

        if (DateTime.UtcNow - entry.LoadedAt > BoardCacheTtl)
        {
            summaryCache.Remove(groupId);
            return false;
        }

        summaries = entry.Summaries;
        return true;
    }

    bool TryGetCachedTemplate(string groupId, out BoardSectionTemplate template)
    {
        template = null;
        if (!templateCache.TryGetValue(groupId, out var entry))
            return false;

        if (DateTime.UtcNow - entry.LoadedAt > BoardCacheTtl)
        {
            templateCache.Remove(groupId);
            return false;
        }

        template = entry.Template;
        return true;
    }

    void CacheSummaries(string groupId, IReadOnlyList<GroupHymnListSummary> summaries) =>
        summaryCache[groupId] = (DateTime.UtcNow, summaries);

    void CacheTemplate(string groupId, BoardSectionTemplate template) =>
        templateCache[groupId] = (DateTime.UtcNow, template);

    void InvalidateBoardCache(string groupId)
    {
        summaryCache.Remove(groupId);
        templateCache.Remove(groupId);
        if (string.Equals(cachedListGroupId, groupId, StringComparison.Ordinal))
            ClearListCache();
    }

    bool TryGetCachedList(string groupId, string listId, out GroupHymnList list)
    {
        list = null;
        if (!string.Equals(cachedListGroupId, groupId, StringComparison.Ordinal)
            || !string.Equals(cachedListId, listId, StringComparison.Ordinal))
            return false;

        if (DateTime.UtcNow - cachedListLoadedAt > ListCacheTtl)
        {
            ClearListCache();
            return false;
        }

        list = cachedList;
        return list != null;
    }

    void CacheList(string groupId, string listId, GroupHymnList list)
    {
        cachedListGroupId = groupId;
        cachedListId = listId;
        cachedList = list;
        cachedListLoadedAt = DateTime.UtcNow;
    }

    void ClearListCache()
    {
        cachedListGroupId = null;
        cachedListId = null;
        cachedList = null;
        cachedListLoadedAt = default;
    }

    static void Renumber(IList<BoardHymnEntry> hymns)
    {
        for (var i = 0; i < hymns.Count; i++)
            hymns[i].SortOrder = i;
    }

    void EnsureCanEdit()
    {
        EnsureSignedInVerified();
        if (!IsLeader())
            throw new InvalidOperationException("Your role cannot edit the board.");
    }

    void EnsureCanEditEntry(BoardHymnEntry entry)
    {
        EnsureSignedInVerified();
        if (IsLeader() || IsOwner(entry))
            return;

        throw new InvalidOperationException("You can only edit hymns you added.");
    }

    void EnsureSignedInVerified()
    {
        if (!auth.IsSignedIn)
            throw new InvalidOperationException("Sign in to edit the board.");
        if (!auth.IsEmailVerified)
            throw new InvalidOperationException("Verify your email to edit the board.");
    }

    bool IsLeader() => RolePermissions.HasLeadershipRole(profileService.CurrentProfile?.Roles);

    bool IsOwner(BoardHymnEntry entry)
    {
        var uid = auth.CurrentUserId;
        return entry != null
            && !string.IsNullOrEmpty(uid)
            && string.Equals(entry.AddedBy, uid, StringComparison.Ordinal);
    }
}
