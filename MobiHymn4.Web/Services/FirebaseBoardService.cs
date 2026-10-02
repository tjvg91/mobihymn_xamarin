using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MobiHymn4.Shared;
using MobiHymn4.Shared.Models;
using MobiHymn4.Shared.Services;

namespace MobiHymn4.Web.Services;

public sealed class FirebaseBoardService : IBoardService
{
    readonly FirebaseJs firebase;
    readonly IAuthService auth;

    public FirebaseBoardService(FirebaseJs firebase, IAuthService auth)
    {
        this.firebase = firebase;
        this.auth = auth;
    }

    public async Task<IReadOnlyList<BoardListDoc>> GetListsAsync(string groupId)
    {
        await EnsureReadyVerifiedAsync();
        var rows = await firebase.QueryCollectionAsync($"{FirestorePaths.Groups}/{groupId}/{FirestorePaths.Boards}");
        return rows.Select(r => MapList(r)).OrderByDescending(l => l.CreatedAt).ToList();
    }

    public async Task<BoardListDoc?> GetListAsync(string groupId, string listId)
    {
        await EnsureReadyVerifiedAsync();
        var doc = await firebase.GetDocAsync($"{FirestorePaths.Groups}/{groupId}/{FirestorePaths.Boards}/{listId}");
        if (doc == null || doc.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return MapList(doc.Value, listId);
    }

    public async Task<BoardListDoc> CreateListAsync(string groupId, string name)
    {
        await EnsureReadyVerifiedAsync();
        var uid = auth.CurrentUserId;
        var trimmed = name.Trim();
        // Match MAUI: list id is yyyy-MM-dd when the name is a calendar date.
        var scheduled = TryParseListDate(trimmed) ?? DateTime.Today;
        var id = scheduled.ToString("yyyy-MM-dd");
        var displayName = scheduled.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);

        var existing = await GetListAsync(groupId, id);
        if (existing != null)
            throw new InvalidOperationException($"A hymn list already exists for {displayName}.");

        var list = new BoardListDoc
        {
            Id = id,
            Name = displayName,
            CreatedAt = new DateTimeOffset(scheduled, TimeSpan.Zero),
            CreatedBy = uid,
            Hymns = new List<BoardHymnDoc>()
        };

        // Seed only template (saved) sections — never copy list-only sections from another day.
        var template = await GetSectionTemplateAsync(groupId);
        if (template.AutoApply && template.SectionNames.Count > 0)
            MergeSavedSectionsIfNeeded(list, template.SectionNames);

        await WriteListDocAsync(groupId, list);
        return list;
    }

    static DateTime? TryParseListDate(string name)
    {
        if (DateTime.TryParseExact(
                name,
                "MMM d, yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var exact))
            return exact.Date;

        if (DateTime.TryParse(name, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var loose))
            return loose.Date;

        return null;
    }

    public async Task<BoardListDoc> UpdateListDateAsync(string groupId, string listId, DateTime date)
    {
        await EnsureReadyVerifiedAsync();
        var list = await GetListAsync(groupId, listId)
            ?? throw new InvalidOperationException("Hymn list not found.");

        var scheduled = date.Date;
        var newId = scheduled.ToString("yyyy-MM-dd");
        var displayName = scheduled.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);

        var current = TryParseListDate(list.Name) ?? list.CreatedAt.Date;
        if (DateTime.TryParseExact(
                list.Id,
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var idDate))
            current = idDate.Date;

        if (current == scheduled && string.Equals(list.Id, newId, StringComparison.Ordinal))
            return list;

        var existing = await GetListAsync(groupId, newId);
        if (existing != null && !string.Equals(existing.Id, listId, StringComparison.Ordinal))
            throw new InvalidOperationException($"A hymn list already exists for {displayName}.");

        var oldId = list.Id;
        list.Id = newId;
        list.Name = displayName;
        list.CreatedAt = new DateTimeOffset(scheduled, TimeSpan.Zero);
        await WriteListDocAsync(groupId, list);

        if (!string.Equals(oldId, newId, StringComparison.Ordinal))
            await DeleteListAsync(groupId, oldId);

        return list;
    }

    public async Task UpdateListAsync(string groupId, BoardListDoc list)
    {
        await EnsureReadyVerifiedAsync();
        await WriteListDocAsync(groupId, list);
    }

    /// <summary>
    /// The ONLY place that writes board list content. Routes through the
    /// <c>boardUpdateList</c> Cloud Function instead of a direct Firestore write —
    /// Firestore rules deny client create/update on this path — because per-entry
    /// "admin or original adder" ownership can only be enforced server-side by
    /// diffing against the previous document (rules alone can't do that).
    /// The function also re-stamps addedBy/addedByName from the caller's verified
    /// identity, so a client can never spoof authorship.
    /// </summary>
    async Task WriteListDocAsync(string groupId, BoardListDoc list)
    {
        await EnsureReadyVerifiedAsync();
        var createdMs = list.CreatedAt == default
            ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            : list.CreatedAt.ToUnixTimeMilliseconds();
        await firebase.CallFunctionAsync("boardUpdateList", new
        {
            groupId,
            listId = list.Id,
            name = list.Name,
            createdAt = createdMs,
            createdBy = list.CreatedBy,
            hymns = list.Hymns.Select(h => new
            {
                id = h.Id,
                isSection = h.IsSection,
                sectionName = h.SectionName,
                hymnNumber = h.HymnNumber,
                sortOrder = h.SortOrder,
                notes = h.Notes,
                updatedAt = h.UpdatedAt == default ? (long?)null : h.UpdatedAt.ToUnixTimeMilliseconds()
            }).ToArray()
        });
    }

    public async Task DeleteListAsync(string groupId, string listId)
    {
        await EnsureReadyVerifiedAsync();
        await firebase.DeleteDocAsync($"{FirestorePaths.Groups}/{groupId}/{FirestorePaths.Boards}/{listId}");
    }

    public async Task ClearAllHymnsAsync(string groupId, string listId)
    {
        var list = await GetListAsync(groupId, listId)
            ?? throw new InvalidOperationException("List not found.");
        var saved = (await GetSavedSectionNamesAsync(groupId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        list.Hymns = list.Hymns
            .Where(h => h.IsSection && saved.Contains(h.SectionName?.Trim() ?? ""))
            .ToList();
        Renumber(list.Hymns);
        await UpdateListAsync(groupId, list);
    }

    public async Task ClearAllSectionsInListAsync(string groupId, string listId)
    {
        var list = await GetListAsync(groupId, listId)
            ?? throw new InvalidOperationException("List not found.");
        list.Hymns = list.Hymns.Where(h => !h.IsSection).ToList();
        Renumber(list.Hymns);
        await UpdateListAsync(groupId, list);
    }

    public async Task ClearAllSavedSectionsAsync(string groupId, string listId)
    {
        await EnsureReadyVerifiedAsync();
        await firebase.SetDocAsync(
            $"{FirestorePaths.Groups}/{groupId}/{FirestorePaths.SectionTemplate}/default",
            new { sectionNames = Array.Empty<string>(), autoApply = false });
        await ClearAllSectionsInListAsync(groupId, listId);
    }

    public async Task SaveSectionToTemplateAsync(string groupId, string listId, string sectionName)
    {
        var trimmed = sectionName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return;

        var list = await GetListAsync(groupId, listId)
            ?? throw new InvalidOperationException("List not found.");
        var listSections = list.Hymns
            .Where(h => h.IsSection && !string.IsNullOrWhiteSpace(h.SectionName))
            .Select(h => h.SectionName!.Trim())
            .ToList();

        if (!listSections.Any(n => string.Equals(n, trimmed, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Section not found on the board.");

        var saved = (await GetSavedSectionNamesAsync(groupId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        saved.Add(trimmed);
        var sectionNames = listSections.Where(n => saved.Contains(n)).ToList();

        await EnsureReadyVerifiedAsync();
        await firebase.SetDocAsync(
            $"{FirestorePaths.Groups}/{groupId}/{FirestorePaths.SectionTemplate}/default",
            new { sectionNames, autoApply = true });
    }

    public async Task RemoveSectionFromTemplateAsync(string groupId, string sectionName)
    {
        var trimmed = sectionName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return;

        var saved = (await GetSavedSectionNamesAsync(groupId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        saved.Remove(trimmed);
        var sectionNames = saved.ToList();

        await EnsureReadyVerifiedAsync();
        await firebase.SetDocAsync(
            $"{FirestorePaths.Groups}/{groupId}/{FirestorePaths.SectionTemplate}/default",
            new { sectionNames, autoApply = sectionNames.Count > 0 });
    }

    public async Task RemoveSectionAsync(string groupId, string listId, string sectionId, bool deleteHymnsInSection)
    {
        var list = await GetListAsync(groupId, listId)
            ?? throw new InvalidOperationException("List not found.");
        var sectionIndex = list.Hymns.FindIndex(h => h.Id == sectionId);
        if (sectionIndex < 0)
            return;

        var idsToRemove = new HashSet<string>(StringComparer.Ordinal) { sectionId };
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
        await UpdateListAsync(groupId, list);
    }

    public async Task<BoardListDoc> ApplySectionSortAsync(string groupId, string listId, string sortMode)
    {
        var list = await GetListAsync(groupId, listId)
            ?? throw new InvalidOperationException("List not found.");
        list.Hymns = SortSections(list.Hymns, sortMode);
        Renumber(list.Hymns);
        await UpdateListAsync(groupId, list);
        try
        {
            await firebase.SetDocAsync(
                $"{FirestorePaths.Groups}/{groupId}/{FirestorePaths.SectionTemplate}/default",
                new { sectionSort = sortMode });
        }
        catch { /* preference is optional */ }

        return list;
    }

    public IDisposable SubscribeList(string groupId, string listId, Action<BoardListDoc?> onChange)
    {
        var bridge = new BoardSnapshotBridge(onChange);
        var cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                await EnsureReadyVerifiedAsync();
                var path = $"{FirestorePaths.Groups}/{groupId}/{FirestorePaths.Boards}/{listId}";
                var subId = await firebase.SubscribeDocAsync(path, bridge.Ref);
                if (!string.IsNullOrEmpty(subId))
                {
                    cts.Token.Register(() =>
                    {
                        _ = firebase.UnsubscribeAsync(subId);
                        bridge.Dispose();
                    });
                    return;
                }
            }
            catch
            {
                // Fall through to polling.
            }

            while (!cts.IsCancellationRequested)
            {
                try { onChange(await GetListAsync(groupId, listId)); }
                catch { /* ignore */ }

                try { await Task.Delay(4000, cts.Token); }
                catch { break; }
            }

            bridge.Dispose();
        }, cts.Token);

        return cts;
    }

    sealed class BoardSnapshotBridge : IDisposable
    {
        readonly Action<BoardListDoc?> onChange;
        public DotNetObjectReference<BoardSnapshotBridge> Ref { get; }

        public BoardSnapshotBridge(Action<BoardListDoc?> onChange)
        {
            this.onChange = onChange;
            Ref = DotNetObjectReference.Create(this);
        }

        [JSInvokable]
        public void OnSnapshot(string? json)
        {
            if (string.IsNullOrWhiteSpace(json) || string.Equals(json, "null", StringComparison.Ordinal))
            {
                onChange(null);
                return;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                onChange(MapList(doc.RootElement.Clone()));
            }
            catch
            {
                onChange(null);
            }
        }

        public void Dispose() => Ref.Dispose();
    }

    static BoardListDoc MapList(JsonElement d, string? fallbackId = null)
    {
        var id = FirebaseProfileService.GetString(d, "id")
            ?? FirebaseProfileService.GetString(d, "_id")
            ?? fallbackId
            ?? "";

        var hymns = new List<BoardHymnDoc>();
        if (d.TryGetProperty("hymns", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var h in arr.EnumerateArray())
            {
                var hymnId = FirebaseProfileService.GetFlexibleString(h, "id");
                // Never invent an id for an existing row — a new id orphans edits
                // and trips ownership checks on the next boardUpdateList write.
                if (string.IsNullOrWhiteSpace(hymnId))
                    continue;

                hymns.Add(new BoardHymnDoc
                {
                    Id = hymnId,
                    IsSection = h.TryGetProperty("isSection", out var s) && s.ValueKind == JsonValueKind.True,
                    SectionName = FirebaseProfileService.GetFlexibleString(h, "sectionName"),
                    HymnNumber = FirebaseProfileService.GetFlexibleString(h, "hymnNumber"),
                    SortOrder = h.TryGetProperty("sortOrder", out var so) && so.TryGetInt64(out var n) ? n : 0,
                    Notes = FirebaseProfileService.GetFlexibleString(h, "notes"),
                    AddedBy = FirebaseProfileService.GetFlexibleString(h, "addedBy"),
                    AddedByName = FirebaseProfileService.GetFlexibleString(h, "addedByName"),
                    UpdatedAt = GetDateTime(h, "updatedAt") ?? default
                });
            }
        }

        DateTimeOffset created = GetDateTime(d, "createdAt") ?? default;

        return new BoardListDoc
        {
            Id = id,
            Name = FirebaseProfileService.GetString(d, "name") ?? "",
            CreatedBy = FirebaseProfileService.GetString(d, "createdBy") ?? "",
            CreatedAt = created,
            Hymns = hymns.OrderBy(h => h.SortOrder).ToList()
        };
    }

    static DateTimeOffset? GetDateTime(JsonElement d, string name)
    {
        if (!d.TryGetProperty(name, out var p))
            return null;
        if (p.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(p.GetString(), out var parsed))
            return parsed;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var ms))
            return DateTimeOffset.FromUnixTimeMilliseconds(ms);
        if (p.ValueKind == JsonValueKind.Object)
        {
            if (p.TryGetProperty("seconds", out var secEl) && secEl.TryGetInt64(out var sec))
                return DateTimeOffset.FromUnixTimeSeconds(sec);
            if (p.TryGetProperty("_seconds", out var secEl2) && secEl2.TryGetInt64(out var sec2))
                return DateTimeOffset.FromUnixTimeSeconds(sec2);
        }

        return null;
    }

    public async Task<IReadOnlyCollection<string>> GetSavedSectionNamesAsync(string groupId)
    {
        var template = await GetSectionTemplateAsync(groupId);
        return template.SectionNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<BoardListDoc?> EnsureSavedSectionsOnListAsync(string groupId, string listId)
    {
        var list = await GetListAsync(groupId, listId);
        if (list == null)
            return null;

        var template = await GetSectionTemplateAsync(groupId);
        if (!template.AutoApply || template.SectionNames.Count == 0)
            return list;

        if (!MergeSavedSectionsIfNeeded(list, template.SectionNames))
            return list;

        await UpdateListAsync(groupId, list);
        return list;
    }

    async Task<(IReadOnlyList<string> SectionNames, bool AutoApply)> GetSectionTemplateAsync(string groupId)
    {
        try
        {
            await EnsureReadyVerifiedAsync();
            var doc = await firebase.GetDocAsync(
                $"{FirestorePaths.Groups}/{groupId}/{FirestorePaths.SectionTemplate}/default");
            if (doc == null || doc.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return (Array.Empty<string>(), false);

            var autoApply = doc.Value.TryGetProperty("autoApply", out var aa)
                && aa.ValueKind == JsonValueKind.True;

            var names = new List<string>();
            if (doc.Value.TryGetProperty("sectionNames", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in arr.EnumerateArray())
                {
                    var name = x.GetString()?.Trim() ?? "";
                    if (name.Length > 0)
                        names.Add(name);
                }
            }

            return (names, autoApply);
        }
        catch
        {
            return (Array.Empty<string>(), false);
        }
    }

    bool MergeSavedSectionsIfNeeded(BoardListDoc list, IReadOnlyList<string> savedNames)
    {
        if (list.Hymns == null)
            list.Hymns = new List<BoardHymnDoc>();

        var existing = list.Hymns
            .Where(h => h.IsSection && !string.IsNullOrWhiteSpace(h.SectionName))
            .Select(h => h.SectionName!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = false;
        var uid = auth.CurrentUserId;
        foreach (var name in savedNames)
        {
            var trimmed = name?.Trim() ?? "";
            if (trimmed.Length == 0 || existing.Contains(trimmed))
                continue;

            list.Hymns.Add(new BoardHymnDoc
            {
                Id = Guid.NewGuid().ToString("N")[..10],
                IsSection = true,
                SectionName = trimmed,
                SortOrder = list.Hymns.Count + 1,
                AddedBy = uid,
                AddedByName = "",
                UpdatedAt = DateTimeOffset.UtcNow
            });
            existing.Add(trimmed);
            added = true;
        }

        if (!added)
            return false;

        Renumber(list.Hymns);
        return true;
    }

    static void Renumber(List<BoardHymnDoc> hymns)
    {
        for (var i = 0; i < hymns.Count; i++)
            hymns[i].SortOrder = i + 1;
    }

    static List<BoardHymnDoc> SortSections(IReadOnlyList<BoardHymnDoc> source, string sortMode)
    {
        var ordered = source.OrderBy(h => h.SortOrder).ToList();
        var leading = new List<BoardHymnDoc>();
        var groups = new List<(BoardHymnDoc Section, List<BoardHymnDoc> Hymns)>();
        (BoardHymnDoc Section, List<BoardHymnDoc> Hymns)? current = null;

        foreach (var entry in ordered)
        {
            if (entry.IsSection)
            {
                current = (entry, new List<BoardHymnDoc>());
                groups.Add(current.Value);
                continue;
            }

            if (current == null)
                leading.Add(entry);
            else
                current.Value.Hymns.Add(entry);
        }

        if (groups.Count > 1)
        {
            groups = sortMode switch
            {
                "DateNewest" => groups
                    .OrderByDescending(g => g.Section.UpdatedAt)
                    .ThenBy(g => g.Section.SectionName, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                "DateOldest" => groups
                    .OrderBy(g => g.Section.UpdatedAt)
                    .ThenBy(g => g.Section.SectionName, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                "NameAsc" => groups
                    .OrderBy(g => g.Section.SectionName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(g => g.Section.SortOrder)
                    .ToList(),
                "NameDesc" => groups
                    .OrderByDescending(g => g.Section.SectionName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(g => g.Section.SortOrder)
                    .ToList(),
                _ => groups
                    .OrderBy(g => g.Section.SortOrder)
                    .ThenBy(g => g.Section.SectionName, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            };
        }

        var result = new List<BoardHymnDoc>(ordered.Count);
        result.AddRange(leading);
        foreach (var (section, hymns) in groups)
        {
            result.Add(section);
            result.AddRange(hymns);
        }

        return result;
    }

    async Task EnsureReadyVerifiedAsync()
    {
        if (!auth.IsSignedIn)
            throw new InvalidOperationException("Sign in to continue.");
        if (!auth.IsEmailVerified)
            throw new InvalidOperationException("Verify your email to continue.");
        await firebase.EnsureReadyAsync();
    }
}
