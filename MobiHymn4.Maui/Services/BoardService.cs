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
    public const int DefaultBoardListPageSize = BoardListsPage.DefaultPageSize;

    readonly IFirebaseFirestoreAccessor firebase;
    readonly IAuthService auth;
    readonly IProfileService profileService;
    readonly Dictionary<string, (DateTime LoadedAt, BoardListsPage Page)> summaryCache = new(StringComparer.Ordinal);
    readonly Dictionary<string, (DateTime LoadedAt, BoardSectionTemplate Template)> templateCache = new(StringComparer.Ordinal);
    string cachedListGroupId;
    string cachedListId;
    GroupHymnList cachedList;
    DateTime cachedListLoadedAt;
    static readonly TimeSpan BoardCacheTtl = TimeSpan.FromMinutes(2);
    static readonly TimeSpan ListCacheTtl = TimeSpan.FromSeconds(45);
    static readonly TimeSpan ListHymnListsTimeout = TimeSpan.FromSeconds(12);
    static readonly TimeSpan HymnListFetchTimeout = TimeSpan.FromSeconds(10);
    static readonly TimeSpan HymnListInitialDefaultTimeout = TimeSpan.FromSeconds(4);

    public BoardService(IFirebaseFirestoreAccessor firebase, IAuthService auth, IProfileService profileService)
    {
        this.firebase = firebase;
        this.auth = auth;
        this.profileService = profileService;
    }

    public event EventHandler<GroupHymnList> HymnListChanged;

    public async Task<BoardListsPage> ListHymnListsAsync(
        string groupId,
        GroupHymnListSummary startAfter = null,
        int pageSize = DefaultBoardListPageSize)
    {
        if (startAfter == null && TryGetCachedSummaries(groupId, out var cached))
            return cached;

        var size = Math.Clamp(pageSize, 1, 100);
        try
        {
            // Unordered + loose map: PWA writes createdAt as epoch ms or ISO strings;
            // OrderBy(Timestamp) and typed DateTimeOffset deserialize can drop those docs.
            var page = await ListHymnListsLooseAsync(groupId, startAfter, size);
            if (page.Items.Count == 0 && startAfter == null)
            {
                // Fallback: typed summary docs (works for Timestamp / epoch that Plugin can map).
                try
                {
                    var typed = await ListHymnListsTypedSummaryAsync(groupId, size);
                    if (typed.Items.Count > 0)
                        page = typed;
                }
                catch (Exception typedEx)
                {
                    Debug.WriteLine($"ListHymnLists typed fallback failed: {typedEx.Message}");
                }
            }

            if (startAfter == null && page.Items.Count > 0)
                CacheSummaries(groupId, page);
            return page;
        }
        catch (TimeoutException)
        {
            Debug.WriteLine("ListHymnListsAsync timed out.");
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ListHymnListsAsync failed: {ex}");
            throw;
        }
    }

    async Task<BoardListsPage> ListHymnListsTypedSummaryAsync(string groupId, int size)
    {
        var snapshot = await firebase.Firestore
            .GetCollection(FirestorePaths.Groups)
            .GetDocument(groupId)
            .GetCollection(FirestorePaths.Boards)
            .LimitedTo(Math.Max(size + 1, 80))
            .GetDocumentsAsync<BoardListSummaryFirestoreDocument>()
            .WaitAsync(ListHymnListsTimeout);

        var all = (snapshot?.Documents ?? Array.Empty<IDocumentSnapshot<BoardListSummaryFirestoreDocument>>())
            .Select(doc =>
            {
                var listId = doc?.Reference?.Id ?? doc?.Data?.Id ?? string.Empty;
                if (string.IsNullOrWhiteSpace(listId))
                    return null;
                return FirestoreMappers.ToGroupHymnListSummary(listId, doc.Data);
            })
            .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Id))
            .OrderByDescending(l => l.CreatedAt)
            .ThenByDescending(l => l.Id, StringComparer.Ordinal)
            .ToList();

        var hasMore = all.Count > size;
        return new BoardListsPage
        {
            Items = hasMore ? all.Take(size).ToList() : all,
            HasMore = hasMore,
        };
    }

    async Task<BoardListsPage> ListHymnListsLooseAsync(
        string groupId,
        GroupHymnListSummary startAfter,
        int size)
    {
        var snapshot = await firebase.Firestore
            .GetCollection(FirestorePaths.Groups)
            .GetDocument(groupId)
            .GetCollection(FirestorePaths.Boards)
            .LimitedTo(Math.Max(size + 1, 80))
            .GetDocumentsAsync<Dictionary<string, object>>()
            .WaitAsync(ListHymnListsTimeout);

        var all = new List<GroupHymnListSummary>();
        foreach (var doc in snapshot?.Documents ?? Array.Empty<IDocumentSnapshot<Dictionary<string, object>>>())
        {
            var summary = ToSummaryLoose(doc);
            if (summary != null && !string.IsNullOrWhiteSpace(summary.Id))
                all.Add(summary);
        }

        all = all
            .OrderByDescending(l => l.CreatedAt)
            .ThenByDescending(l => l.Id, StringComparer.Ordinal)
            .ToList();

        IEnumerable<GroupHymnListSummary> window = all;
        if (startAfter != null)
        {
            var idx = all.FindIndex(l => string.Equals(l.Id, startAfter.Id, StringComparison.Ordinal));
            window = idx >= 0
                ? all.Skip(idx + 1)
                : all.Where(l => l.CreatedAt < startAfter.CreatedAt);
        }

        var batch = window.Take(size + 1).ToList();
        var hasMore = batch.Count > size;
        return new BoardListsPage
        {
            Items = hasMore ? batch.Take(size).ToList() : batch,
            HasMore = hasMore,
        };
    }

    static GroupHymnListSummary ToSummaryLoose(IDocumentSnapshot<Dictionary<string, object>> doc)
    {
        var data = CoerceStringObjectMap(doc?.Data);
        var listId = doc?.Reference?.Id
            ?? ReadString(data, "id")
            ?? string.Empty;
        if (string.IsNullOrWhiteSpace(listId))
            return null;

        var name = ReadString(data, "name") ?? string.Empty;
        var createdAt = ReadDateTimeOffset(data, "createdAt")
            ?? ReadDateTimeOffset(data, "updatedAt")
            ?? default;
        var createdBy = ReadString(data, "createdBy") ?? string.Empty;
        var hymnCount = (int)ReadInt64(data, "hymnCount");
        var entryCount = (int)ReadInt64(data, "entryCount");
        if (entryCount == 0 && hymnCount > 0)
            entryCount = hymnCount;

        // If denormalized counts missing, count hymns array when present.
        if (hymnCount == 0 && entryCount == 0
            && data != null
            && data.TryGetValue("hymns", out var hymnsObj)
            && hymnsObj is System.Collections.IEnumerable hymnsEnum)
        {
            var list = hymnsEnum.Cast<object>().Where(h => h != null).ToList();
            entryCount = list.Count;
            hymnCount = list.Count(h =>
            {
                var map = CoerceStringObjectMap(h);
                if (map != null
                    && map.TryGetValue("isSection", out var sec)
                    && sec is bool b)
                    return !b;
                return true;
            });
        }

        return FirestoreMappers.ToGroupHymnListSummary(listId, new BoardFirestoreDocument
        {
            Name = name,
            CreatedAtValue = createdAt,
            CreatedBy = createdBy,
            HymnCount = hymnCount,
            EntryCount = entryCount,
        });
    }

    static Dictionary<string, object> CoerceStringObjectMap(object value)
    {
        if (value == null)
            return null;
        if (value is Dictionary<string, object> dict)
            return dict;
        if (value is IDictionary<string, object> generic)
            return new Dictionary<string, object>(generic);
        if (value is System.Collections.IDictionary plain)
        {
            var copy = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (System.Collections.DictionaryEntry entry in plain)
            {
                if (entry.Key == null)
                    continue;
                copy[entry.Key.ToString()] = entry.Value;
            }
            return copy;
        }

        return null;
    }

    static string ReadString(IDictionary<string, object> data, string key)
    {
        if (data == null || !data.TryGetValue(key, out var value) || value == null)
            return null;
        return value.ToString();
    }

    static long ReadInt64(IDictionary<string, object> data, string key)
    {
        if (data == null || !data.TryGetValue(key, out var value) || value == null)
            return 0;
        try
        {
            return Convert.ToInt64(value);
        }
        catch
        {
            return 0;
        }
    }

    static DateTimeOffset? ReadDateTimeOffset(IDictionary<string, object> data, string key)
    {
        if (data == null || !data.TryGetValue(key, out var value) || value == null)
            return null;

        switch (value)
        {
            case DateTimeOffset dto:
                return dto;
            case DateTime dt:
                return dt.Kind == DateTimeKind.Unspecified
                    ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc))
                    : new DateTimeOffset(dt.ToUniversalTime());
            case string s when DateTimeOffset.TryParse(s, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var parsed):
                return parsed;
            case long ms when ms > 1_000_000_000_000L:
                return DateTimeOffset.FromUnixTimeMilliseconds(ms);
            case long sec:
                return DateTimeOffset.FromUnixTimeSeconds(sec);
            case int i:
                return DateTimeOffset.FromUnixTimeSeconds(i);
            case double d:
                return d > 1_000_000_000_000d
                    ? DateTimeOffset.FromUnixTimeMilliseconds((long)d)
                    : DateTimeOffset.FromUnixTimeSeconds((long)d);
            case float f:
                return f > 1_000_000_000_000f
                    ? DateTimeOffset.FromUnixTimeMilliseconds((long)f)
                    : DateTimeOffset.FromUnixTimeSeconds((long)f);
            case Dictionary<string, object> map:
                return ReadTimestampMap(map);
            case IDictionary<string, object> genericMap:
                return ReadTimestampMap(genericMap);
        }

        var coerced = CoerceStringObjectMap(value);
        if (coerced != null)
        {
            var fromMap = ReadTimestampMap(coerced);
            if (fromMap.HasValue)
                return fromMap;
        }

        // Numeric boxed as Java.Lang.Long / etc.
        try
        {
            if (value is IConvertible && value is not string)
            {
                var n = Convert.ToInt64(value);
                return n > 1_000_000_000_000L
                    ? DateTimeOffset.FromUnixTimeMilliseconds(n)
                    : DateTimeOffset.FromUnixTimeSeconds(n);
            }
        }
        catch { /* ignore */ }

        // Android/iOS Timestamp types often expose Seconds as a property.
        try
        {
            var secondsProp = value.GetType().GetProperty("Seconds")
                ?? value.GetType().GetProperty("seconds");
            if (secondsProp != null)
            {
                var secVal = Convert.ToInt64(secondsProp.GetValue(value));
                return DateTimeOffset.FromUnixTimeSeconds(secVal);
            }
        }
        catch { /* ignore */ }

        return null;
    }

    static DateTimeOffset? ReadTimestampMap(IDictionary<string, object> map)
    {
        if (map == null)
            return null;
        if (map.TryGetValue("seconds", out var secObj) || map.TryGetValue("_seconds", out secObj))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(secObj));
            }
            catch { /* ignore */ }
        }

        return null;
    }

    static GroupHymnListSummary ToSummary(IDocumentSnapshot<BoardFirestoreDocument> doc)
    {
        var listId = doc?.Reference?.Id
            ?? doc?.Data?.Id
            ?? string.Empty;
        return FirestoreMappers.ToGroupHymnListSummary(listId, doc?.Data);
    }

    static BoardListsPage ToBoardListsPage(IQuerySnapshot<BoardFirestoreDocument> snapshot, int pageSize)
    {
        var docs = snapshot?.Documents?.ToList();
        if (docs == null || docs.Count == 0)
            return BoardListsPage.Empty;

        var hasMore = docs.Count > pageSize;
        var take = hasMore ? docs.Take(pageSize) : docs;
        var items = take.Select(ToSummary).ToList();

        return new BoardListsPage
        {
            Items = items,
            HasMore = hasMore,
        };
    }

    public async Task<bool> HymnListExistsForDateAsync(string groupId, DateTime date, string excludeListId = null)
    {
        var target = date.Date;
        var dateKey = GroupHymnListDates.ToDateKey(target);
        try
        {
            var boards = firebase.Firestore
                .GetCollection(FirestorePaths.Groups)
                .GetDocument(groupId)
                .GetCollection(FirestorePaths.Boards);

            var byId = await boards
                .GetDocument(dateKey)
                .GetDocumentSnapshotAsync<BoardListSummaryFirestoreDocument>();

            if (byId?.Data != null)
            {
                var existingId = byId.Data.Id ?? dateKey;
                if (string.IsNullOrWhiteSpace(excludeListId)
                    || !string.Equals(existingId, excludeListId, StringComparison.Ordinal))
                    return true;
            }

            // Legacy lists may use a non-date document id with a formatted name.
            var name = GroupHymnListDates.FormatName(target);
            var byName = await boards
                .WhereEqualsTo("name", name)
                .LimitedTo(8)
                .GetDocumentsAsync<BoardListSummaryFirestoreDocument>();

            if (byName?.Documents == null)
                return false;

            foreach (var doc in byName.Documents)
            {
                var listId = doc.Reference?.Id ?? doc.Data?.Id ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(excludeListId)
                    && string.Equals(listId, excludeListId, StringComparison.Ordinal))
                    continue;

                if (GroupHymnListDates.TryGetScheduledDate(listId, doc.Data?.Name, out var existing)
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

    static object ToCreatedAtCursor(GroupHymnListSummary summary)
    {
        var created = summary?.CreatedAt ?? default;
        if (created == default)
            return DateTimeOffset.MinValue;

        var utc = created.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(created, DateTimeKind.Utc)
            : created.ToUniversalTime();
        return new DateTimeOffset(utc);
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
            Hymns = new List<BoardHymnEntry>(),
        };

        // Seed only template (saved) sections — never copy list-only sections from another day.
        var template = await GetSectionTemplateAsync(groupId);
        if (ShouldShowSavedSections(template))
            MergeSavedSectionsIfNeeded(list, template);

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
        // Don't trust an empty cached list — typed reads often cached sections-only.
        if (TryGetCachedList(groupId, listId, out var cached)
            && cached.Hymns != null
            && cached.Hymns.Any(h => !h.IsSection))
            return cached;

        try
        {
            var docRef = firebase.Firestore
                .GetCollection(FirestorePaths.Groups)
                .GetDocument(groupId)
                .GetCollection(FirestorePaths.Boards)
                .GetDocument(listId);

            GroupHymnList typed = null;
            try
            {
                var snapshot = await docRef
                    .GetDocumentSnapshotAsync<BoardFirestoreDocument>(Source.Server)
                    .WaitAsync(HymnListFetchTimeout);
                if (snapshot?.Data != null)
                    typed = FirestoreMappers.ToGroupHymnList(groupId, listId, snapshot.Data);
            }
            catch (Exception typedEx)
            {
                Debug.WriteLine($"GetHymnListAsync typed read failed: {typedEx.Message}");
                try
                {
                    var snapshot = await docRef
                        .GetDocumentSnapshotAsync<BoardFirestoreDocument>()
                        .WaitAsync(HymnListFetchTimeout);
                    if (snapshot?.Data != null)
                        typed = FirestoreMappers.ToGroupHymnList(groupId, listId, snapshot.Data);
                }
                catch (Exception typedRetryEx)
                {
                    Debug.WriteLine($"GetHymnListAsync typed cache read failed: {typedRetryEx.Message}");
                }
            }

            GroupHymnList loose = null;
            try
            {
                var looseSnap = await docRef
                    .GetDocumentSnapshotAsync<Dictionary<string, object>>(Source.Server)
                    .WaitAsync(HymnListFetchTimeout);
                var data = FirestoreMappers.CoerceStringObjectMap(looseSnap?.Data) ?? looseSnap?.Data;
                if (data != null)
                    loose = FirestoreMappers.ToGroupHymnListFromDictionary(groupId, listId, data);
            }
            catch (Exception looseEx)
            {
                Debug.WriteLine($"GetHymnListAsync loose read failed: {looseEx.Message}");
                try
                {
                    var looseSnap = await docRef
                        .GetDocumentSnapshotAsync<Dictionary<string, object>>()
                        .WaitAsync(HymnListFetchTimeout);
                    var data = FirestoreMappers.CoerceStringObjectMap(looseSnap?.Data) ?? looseSnap?.Data;
                    if (data != null)
                        loose = FirestoreMappers.ToGroupHymnListFromDictionary(groupId, listId, data);
                }
                catch (Exception looseRetryEx)
                {
                    Debug.WriteLine($"GetHymnListAsync loose cache read failed: {looseRetryEx.Message}");
                }
            }

            // Prefer richer parse: typed often drops nested hymns; Dictionary keeps PWA entries.
            var list = PreferRicherHymnList(typed, loose)
                ?? FirestoreMappers.ToGroupHymnList(groupId, listId, null);

            var parsedHymns = list.Hymns?.Count(h => !h.IsSection) ?? 0;
            Debug.WriteLine(
                $"GetHymnListAsync {groupId}/{listId}: typed={typed?.Hymns?.Count ?? -1}, loose={loose?.Hymns?.Count ?? -1}, chosen={list.Hymns?.Count ?? 0} (hymns={parsedHymns})");

            CacheList(groupId, listId, list);
            return list;
        }
        catch (TimeoutException)
        {
            Debug.WriteLine($"GetHymnListAsync timed out for {groupId}/{listId}.");
            return FirestoreMappers.ToGroupHymnList(groupId, listId, null);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetHymnListAsync failed: {ex.Message}");
            return FirestoreMappers.ToGroupHymnList(groupId, listId, null);
        }
    }

    static GroupHymnList PreferRicherHymnList(GroupHymnList a, GroupHymnList b)
    {
        if (a == null)
            return b;
        if (b == null)
            return a;

        static int Score(GroupHymnList list)
        {
            var hymns = list.Hymns;
            if (hymns == null || hymns.Count == 0)
                return 0;
            var nonSection = hymns.Count(h => !h.IsSection);
            return nonSection * 1000 + hymns.Count;
        }

        return Score(b) > Score(a) ? b : a;
    }

    public async Task<(IDisposable Subscription, GroupHymnList Initial)> SubscribeHymnListWithInitialAsync(
        string groupId,
        string listId,
        Action<GroupHymnList> onChanged,
        TimeSpan? initialTimeout = null)
    {
        var tcs = new TaskCompletionSource<GroupHymnList>(TaskCreationOptions.RunContinuationsAsynchronously);
        var initialDelivered = 0;
        var timeout = initialTimeout ?? HymnListInitialDefaultTimeout;

        void Deliver(GroupHymnList incoming)
        {
            if (incoming == null)
                return;

            var list = incoming;
            if (TryGetCachedList(groupId, listId, out var cached))
                list = PreferRicherHymnList(cached, incoming) ?? incoming;

            CacheList(groupId, listId, list);

            if (Interlocked.CompareExchange(ref initialDelivered, 1, 0) == 0)
                tcs.TrySetResult(list);
            else
                onChanged?.Invoke(list);

            HymnListChanged?.Invoke(this, list);
        }

        // Loose Dictionary snapshots keep nested hymns maps on Android/iOS.
        IDisposable registration = firebase.Firestore
            .GetCollection(FirestorePaths.Groups)
            .GetDocument(groupId)
            .GetCollection(FirestorePaths.Boards)
            .GetDocument(listId)
            .AddSnapshotListener<Dictionary<string, object>>(
                snapshot =>
                {
                    try
                    {
                        var data = FirestoreMappers.CoerceStringObjectMap(snapshot?.Data) ?? snapshot?.Data;
                        var loose = data != null
                            ? FirestoreMappers.ToGroupHymnListFromDictionary(groupId, listId, data)
                            : FirestoreMappers.ToGroupHymnList(groupId, listId, null);
                        Deliver(loose);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Hymn list loose snapshot failed: {ex.Message}");
                        _ = GetHymnListAsync(groupId, listId).ContinueWith(t =>
                        {
                            if (t.Status == TaskStatus.RanToCompletion)
                                Deliver(t.Result);
                        }, TaskScheduler.Default);
                    }
                },
                ex => Debug.WriteLine($"Hymn list subscription error: {ex?.Message}"));

        var disposable = new SubscriptionDisposable(() => registration?.Dispose());

        var getTask = GetHymnListAsync(groupId, listId);
        _ = getTask.ContinueWith(
            t =>
            {
                if (t.Status == TaskStatus.RanToCompletion)
                    Deliver(t.Result);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        try
        {
            var initial = await tcs.Task.WaitAsync(timeout);
            return (disposable, initial);
        }
        catch (TimeoutException)
        {
            Debug.WriteLine($"SubscribeHymnListWithInitialAsync timed out for {groupId}/{listId}.");
            GroupHymnList fallback;
            try
            {
                fallback = await getTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch
            {
                fallback = FirestoreMappers.ToGroupHymnList(groupId, listId, null);
            }

            Deliver(fallback);
            return (disposable, fallback ?? FirestoreMappers.ToGroupHymnList(groupId, listId, null));
        }
    }

    public IDisposable SubscribeHymnList(string groupId, string listId, Action<GroupHymnList> onChanged)
    {
        IDisposable registration = firebase.Firestore
            .GetCollection(FirestorePaths.Groups)
            .GetDocument(groupId)
            .GetCollection(FirestorePaths.Boards)
            .GetDocument(listId)
            .AddSnapshotListener<Dictionary<string, object>>(
                snapshot =>
                {
                    try
                    {
                        var data = FirestoreMappers.CoerceStringObjectMap(snapshot?.Data) ?? snapshot?.Data;
                        var list = data != null
                            ? FirestoreMappers.ToGroupHymnListFromDictionary(groupId, listId, data)
                            : FirestoreMappers.ToGroupHymnList(groupId, listId, null);
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
        // Place new hymns before the first section so they are unsectioned by default.
        var firstSectionIndex = list.Hymns.FindIndex(h => h.IsSection);
        var insertAt = firstSectionIndex < 0 ? list.Hymns.Count : firstSectionIndex;
        list.Hymns.Insert(insertAt, new BoardHymnEntry
        {
            HymnNumber = normalized,
            Notes = notes ?? string.Empty,
            AddedBy = profile.Uid,
            AddedByName = profile.DisplayName,
            UpdatedAt = DateTime.UtcNow,
        });
        Renumber(list.Hymns);

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

    bool TryGetCachedSummaries(string groupId, out BoardListsPage page)
    {
        page = null;
        if (!summaryCache.TryGetValue(groupId, out var entry))
            return false;

        if (DateTime.UtcNow - entry.LoadedAt > BoardCacheTtl)
        {
            summaryCache.Remove(groupId);
            return false;
        }

        page = entry.Page;
        return page != null;
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

    void CacheSummaries(string groupId, BoardListsPage page) =>
        summaryCache[groupId] = (DateTime.UtcNow, page);

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
