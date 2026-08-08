using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MobiHymn4.Models;
using MobiHymn4.Models.Firestore;
using MobiHymn4.Utils;

namespace MobiHymn4.Services;

public static class FirestoreMappers
{
    public static UserProfile ToUserProfile(UserFirestoreDocument doc)
    {
        if (doc == null)
            return null;

        return new UserProfile
        {
            Uid = doc.Id ?? string.Empty,
            Email = doc.Email ?? string.Empty,
            FirstName = doc.FirstName ?? string.Empty,
            LastName = doc.LastName ?? string.Empty,
            Nickname = doc.Nickname ?? string.Empty,
            NotificationsMuted = doc.NotificationsMuted,
            NotificationsPreferenceSet = doc.NotificationsPreferenceSet,
            Roles = doc.Roles?.Select(r => UserRoleExtensions.Parse(r)).Where(r => r.HasValue).Select(r => r.Value).ToList() ?? new List<UserRole>(),
            GroupIds = doc.GroupIds?.ToList() ?? new List<string>(),
        };
    }

    public static UserFirestoreDocument ToFirestore(UserProfile profile)
    {
        return new UserFirestoreDocument
        {
            Id = profile.Uid,
            Email = profile.Email,
            FirstName = profile.FirstName,
            LastName = profile.LastName,
            Nickname = profile.Nickname,
            DisplayName = profile.DisplayName,
            NotificationsMuted = profile.NotificationsMuted,
            NotificationsPreferenceSet = profile.NotificationsPreferenceSet,
            Roles = profile.Roles?.Select(r => r.ToStorageKey()).ToList() ?? new List<string>(),
            GroupIds = profile.GroupIds?.ToList() ?? new List<string>(),
        };
    }

    public static WorshipGroup ToWorshipGroup(GroupFirestoreDocument doc) =>
        new()
        {
            Id = doc?.Id ?? string.Empty,
            Name = doc?.Name ?? string.Empty,
            JoinCode = doc?.JoinCode ?? string.Empty,
            CreatedBy = doc?.CreatedBy ?? string.Empty,
            CreatedAt = doc?.CreatedAt.UtcDateTime ?? default,
        };

    public static GroupMember ToGroupMember(MemberFirestoreDocument doc) =>
        new()
        {
            Uid = doc?.Id ?? string.Empty,
            Email = doc?.Email ?? string.Empty,
            FirstName = doc?.FirstName ?? string.Empty,
            LastName = doc?.LastName ?? string.Empty,
            Nickname = doc?.Nickname ?? string.Empty,
            Roles = doc?.Roles?.Select(r => UserRoleExtensions.Parse(r)).Where(r => r.HasValue).Select(r => r.Value).ToList() ?? new List<UserRole>(),
            JoinedAt = doc?.JoinedAt.UtcDateTime ?? default,
            InvitedBy = doc?.InvitedBy ?? string.Empty,
            NotificationsMuted = doc?.NotificationsMuted ?? false,
            IsAdmin = doc?.IsAdmin ?? false,
        };

    public static GroupHymnList ToGroupHymnList(string groupId, string listId, BoardFirestoreDocument doc)
    {
        var createdAt = ConvertFlexibleDate(doc?.CreatedAtValue) ?? default;
        var updatedAt = ConvertFlexibleDate(doc?.UpdatedAtValue) ?? default;
        var (name, created, createdBy) = ResolveListMetadata(
            listId,
            doc?.Name,
            createdAt,
            updatedAt,
            doc?.CreatedBy);
        var list = new GroupHymnList
        {
            Id = listId ?? string.Empty,
            GroupId = groupId,
            Name = name,
            CreatedAt = created,
            CreatedBy = createdBy,
        };

        if (doc == null)
            return list;

        list.UpdatedAt = updatedAt == default ? created : updatedAt.UtcDateTime;
        list.UpdatedBy = doc.UpdatedBy ?? string.Empty;
        list.Hymns = doc.Hymns?.Select(h => new BoardHymnEntry
        {
            Id = h.Id ?? Guid.NewGuid().ToString("N"),
            IsSection = h.IsSection,
            SectionName = h.SectionName ?? string.Empty,
            HymnNumber = h.HymnNumber ?? string.Empty,
            SortOrder = (int)h.SortOrder,
            Notes = h.Notes ?? string.Empty,
            AddedBy = h.AddedBy ?? string.Empty,
            AddedByName = h.AddedByName ?? string.Empty,
            UpdatedAt = ConvertFlexibleDate(h.UpdatedAtValue)?.UtcDateTime ?? default,
        }).OrderBy(h => h.SortOrder).ToList() ?? new List<BoardHymnEntry>();

        return list;
    }

    public static DateTimeOffset? ConvertFlexibleDate(object value)
    {
        if (value == null)
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
                DateTimeStyles.RoundtripKind, out var parsed):
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
        }

        var map = CoerceStringObjectMap(value);
        if (map != null
            && (map.TryGetValue("seconds", out var secObj) || map.TryGetValue("_seconds", out secObj)))
        {
            try { return DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(secObj)); }
            catch { /* ignore */ }
        }

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

        try
        {
            var secondsProp = value.GetType().GetProperty("Seconds")
                ?? value.GetType().GetProperty("seconds");
            if (secondsProp != null)
                return DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(secondsProp.GetValue(value)));
        }
        catch { /* ignore */ }

        return null;
    }

    public static GroupHymnList ToGroupHymnListFromDictionary(
        string groupId,
        string listId,
        IDictionary<string, object> data)
    {
        var name = ReadDictString(data, "name") ?? string.Empty;
        var createdAt = ReadDictDate(data, "createdAt") ?? default;
        var updatedAt = ReadDictDate(data, "updatedAt") ?? default;
        var createdBy = ReadDictString(data, "createdBy") ?? string.Empty;
        var (resolvedName, resolvedCreated, resolvedBy) = ResolveListMetadata(
            listId, name, createdAt, updatedAt, createdBy);

        var list = new GroupHymnList
        {
            Id = listId ?? string.Empty,
            GroupId = groupId,
            Name = resolvedName,
            CreatedAt = resolvedCreated,
            CreatedBy = resolvedBy,
            UpdatedAt = updatedAt == default ? resolvedCreated : updatedAt.UtcDateTime,
            UpdatedBy = ReadDictString(data, "updatedBy") ?? string.Empty,
            Hymns = new List<BoardHymnEntry>(),
        };

        if (data != null
            && data.TryGetValue("hymns", out var hymnsObj)
            && hymnsObj != null)
        {
            foreach (var item in EnumerateFirestoreList(hymnsObj))
            {
                var h = CoerceStringObjectMap(item);
                if (h == null || h.Count == 0)
                    continue;

                list.Hymns.Add(BoardHymnEntry.FromDictionary(h));
            }

            list.Hymns = list.Hymns.OrderBy(x => x.SortOrder).ToList();
        }

        return list;
    }

    public static Dictionary<string, object> CoerceStringObjectMap(object value)
    {
        if (value == null)
            return null;
        if (value is Dictionary<string, object> dict)
            return dict;
        if (value is IDictionary<string, object> generic)
            return new Dictionary<string, object>(generic);

        // Plugin.Firebase / Android often yields Dictionary<string, object?> or non-generic maps.
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

        // Reflective fallback for Java HashMap / ArrayMap wrappers.
        try
        {
            var type = value.GetType();
            var keysProp = type.GetProperty("Keys") ?? type.GetProperty("KeySet");
            var getMethod = type.GetMethod("Get", new[] { typeof(object) })
                ?? type.GetMethod("get_Item", new[] { typeof(object) });
            if (keysProp != null && getMethod != null)
            {
                var keysObj = keysProp.GetValue(value);
                if (keysObj is System.Collections.IEnumerable keysEnum)
                {
                    var copy = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (var key in keysEnum)
                    {
                        if (key == null)
                            continue;
                        var v = getMethod.Invoke(value, new[] { key });
                        copy[key.ToString()] = v;
                    }
                    return copy;
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    public static IEnumerable<object> EnumerateFirestoreList(object value)
    {
        if (value == null || value is string)
            yield break;

        if (value is System.Collections.IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item != null)
                    yield return item;
            }
        }
    }

    static string ReadDictString(IDictionary<string, object> data, string key)
    {
        if (data == null || !data.TryGetValue(key, out var value) || value == null)
            return null;
        return value.ToString();
    }

    static long ReadDictInt64(IDictionary<string, object> data, string key)
    {
        if (data == null || !data.TryGetValue(key, out var value) || value == null)
            return 0;
        try { return Convert.ToInt64(value); }
        catch { return 0; }
    }

    static DateTimeOffset? ReadDictDate(IDictionary<string, object> data, string key)
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
        }

        var map = CoerceStringObjectMap(value);
        if (map != null
            && (map.TryGetValue("seconds", out var secObj) || map.TryGetValue("_seconds", out secObj)))
        {
            try { return DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(secObj)); }
            catch { /* ignore */ }
        }

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

        try
        {
            var secondsProp = value.GetType().GetProperty("Seconds")
                ?? value.GetType().GetProperty("seconds");
            if (secondsProp != null)
                return DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(secondsProp.GetValue(value)));
        }
        catch { /* ignore */ }

        return null;
    }

    public static GroupHymnListSummary ToGroupHymnListSummary(string listId, BoardListSummaryFirestoreDocument doc)
    {
        var (name, createdAt, createdBy) = ResolveListMetadata(
            listId,
            doc?.Name,
            doc?.CreatedAt ?? default,
            doc?.UpdatedAt ?? default,
            doc?.CreatedBy);

        return new GroupHymnListSummary
        {
            Id = listId ?? string.Empty,
            Name = name,
            CreatedAt = createdAt,
            CreatedBy = createdBy,
            HymnCount = (int)(doc?.HymnCount ?? 0),
            EntryCount = (int)(doc?.EntryCount ?? doc?.HymnCount ?? 0),
        };
    }

    public static GroupHymnListSummary ToGroupHymnListSummary(string listId, BoardFirestoreDocument doc)
    {
        var createdAt = ConvertFlexibleDate(doc?.CreatedAtValue) ?? default;
        var updatedAt = ConvertFlexibleDate(doc?.UpdatedAtValue) ?? default;
        var (name, created, createdBy) = ResolveListMetadata(
            listId,
            doc?.Name,
            createdAt,
            updatedAt,
            doc?.CreatedBy);

        // Prefer denormalized counts so overview listing does not depend on hymns[].
        var hymnCount = (int)(doc?.HymnCount ?? 0);
        var entryCount = (int)(doc?.EntryCount ?? 0);
        var hymns = doc?.Hymns;
        if (hymnCount == 0 && entryCount == 0 && hymns?.Count > 0)
        {
            hymnCount = hymns.Count(h => !h.IsSection);
            entryCount = hymns.Count;
        }
        else if (entryCount == 0 && hymnCount > 0)
        {
            entryCount = hymnCount;
        }

        return new GroupHymnListSummary
        {
            Id = listId ?? string.Empty,
            Name = name,
            CreatedAt = created,
            CreatedBy = createdBy,
            HymnCount = hymnCount,
            EntryCount = entryCount,
        };
    }

    public static BoardFirestoreDocument ToFirestore(GroupHymnList list)
    {
        var hymns = list.Hymns ?? new List<BoardHymnEntry>();
        var hymnCount = hymns.Count(h => !h.IsSection);
        return new BoardFirestoreDocument
        {
            Id = list.Id,
            Name = list.Name ?? string.Empty,
            HymnCount = hymnCount,
            EntryCount = hymns.Count,
            CreatedAtValue = list.CreatedAt == default
                ? DateTimeOffset.UtcNow
                : new DateTimeOffset(list.CreatedAt, TimeSpan.Zero),
            CreatedBy = list.CreatedBy ?? string.Empty,
            UpdatedAtValue = list.UpdatedAt == default ? DateTimeOffset.UtcNow : new DateTimeOffset(list.UpdatedAt, TimeSpan.Zero),
            UpdatedBy = list.UpdatedBy ?? string.Empty,
            Hymns = hymns.Select(h => new BoardHymnFirestoreDocument
            {
                Id = h.Id,
                IsSection = h.IsSection,
                SectionName = h.SectionName,
                HymnNumber = h.HymnNumber,
                SortOrder = h.SortOrder,
                Notes = h.Notes,
                AddedBy = h.AddedBy,
                AddedByName = h.AddedByName,
                UpdatedAtValue = h.UpdatedAt == default
                    ? DateTimeOffset.UtcNow
                    : new DateTimeOffset(h.UpdatedAt, TimeSpan.Zero),
            }).ToList(),
        };
    }

    static (string Name, DateTime CreatedAt, string CreatedBy) ResolveListMetadata(
        string listId,
        string name,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        string createdBy)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var created = createdAt != default
                ? createdAt.UtcDateTime
                : updatedAt.UtcDateTime;
            return (name.Trim(), created, createdBy ?? string.Empty);
        }

        if (DateTime.TryParseExact(
                listId,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var legacyDate))
        {
            return (GroupHymnListDates.FormatName(legacyDate), legacyDate, createdBy ?? string.Empty);
        }

        var fallbackCreated = createdAt != default
            ? createdAt.UtcDateTime
            : updatedAt != default ? updatedAt.UtcDateTime : DateTime.UtcNow;

        return ("Hymn list", fallbackCreated, createdBy ?? string.Empty);
    }

    public static BoardSectionTemplate ToBoardSectionTemplate(BoardSectionTemplateFirestoreDocument doc)
    {
        if (doc == null)
            return new BoardSectionTemplate();

        Enum.TryParse(doc.ApplyFrequency, ignoreCase: true, out SectionApplyFrequency frequency);
        Enum.TryParse(doc.SectionSort, ignoreCase: true, out SectionSortMode sectionSort);

        return new BoardSectionTemplate
        {
            SectionNames = doc.SectionNames?.Where(n => !string.IsNullOrWhiteSpace(n)).ToList() ?? new List<string>(),
            AutoApply = doc.AutoApply,
            ApplyFrequency = frequency,
            SectionSort = string.IsNullOrWhiteSpace(doc.SectionSort) ? SectionSortMode.AddedOrder : sectionSort,
            AnchorDate = doc.AnchorDate?.DateTime.Date,
        };
    }

    public static BoardSectionTemplateFirestoreDocument ToFirestore(BoardSectionTemplate template, string updatedBy)
    {
        return new BoardSectionTemplateFirestoreDocument
        {
            Id = "default",
            SectionNames = template?.SectionNames?.Where(n => !string.IsNullOrWhiteSpace(n)).ToList() ?? new List<string>(),
            AutoApply = template?.AutoApply ?? false,
            ApplyFrequency = template?.ApplyFrequency.ToString() ?? SectionApplyFrequency.Daily.ToString(),
            SectionSort = template?.SectionSort.ToString() ?? SectionSortMode.AddedOrder.ToString(),
            AnchorDate = template?.AnchorDate.HasValue == true
                ? new DateTimeOffset(template.AnchorDate.Value.Date, TimeSpan.Zero)
                : null,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = updatedBy ?? string.Empty,
        };
    }
}
