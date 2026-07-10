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
        };

    public static GroupHymnList ToGroupHymnList(string groupId, string listId, BoardFirestoreDocument doc)
    {
        var (name, createdAt, createdBy) = ResolveListMetadata(listId, doc);
        var list = new GroupHymnList
        {
            Id = listId ?? string.Empty,
            GroupId = groupId,
            Name = name,
            CreatedAt = createdAt,
            CreatedBy = createdBy,
        };

        if (doc == null)
            return list;

        list.UpdatedAt = doc.UpdatedAt.UtcDateTime;
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
            UpdatedAt = h.UpdatedAt.UtcDateTime,
        }).OrderBy(h => h.SortOrder).ToList() ?? new List<BoardHymnEntry>();

        return list;
    }

    public static GroupHymnListSummary ToGroupHymnListSummary(string listId, BoardFirestoreDocument doc)
    {
        var (name, createdAt, createdBy) = ResolveListMetadata(listId, doc);
        var hymns = doc?.Hymns;
        int hymnCount;
        int entryCount;
        if (hymns?.Count > 0)
        {
            hymnCount = hymns.Count(h => !h.IsSection);
            entryCount = hymns.Count;
        }
        else
        {
            hymnCount = (int)(doc?.HymnCount ?? 0);
            entryCount = (int)(doc?.EntryCount ?? doc?.HymnCount ?? 0);
        }

        return new GroupHymnListSummary
        {
            Id = listId ?? string.Empty,
            Name = name,
            CreatedAt = createdAt,
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
            CreatedAt = list.CreatedAt == default
                ? DateTimeOffset.UtcNow
                : new DateTimeOffset(list.CreatedAt, TimeSpan.Zero),
            CreatedBy = list.CreatedBy ?? string.Empty,
            UpdatedAt = list.UpdatedAt == default ? DateTimeOffset.UtcNow : new DateTimeOffset(list.UpdatedAt, TimeSpan.Zero),
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
                UpdatedAt = h.UpdatedAt == default ? DateTimeOffset.UtcNow : new DateTimeOffset(h.UpdatedAt, TimeSpan.Zero),
            }).ToList(),
        };
    }

    static (string Name, DateTime CreatedAt, string CreatedBy) ResolveListMetadata(string listId, BoardFirestoreDocument doc)
    {
        if (!string.IsNullOrWhiteSpace(doc?.Name))
        {
            var created = doc.CreatedAt != default
                ? doc.CreatedAt.UtcDateTime
                : doc.UpdatedAt.UtcDateTime;
            return (doc.Name.Trim(), created, doc.CreatedBy ?? string.Empty);
        }

        if (DateTime.TryParseExact(
                listId,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var legacyDate))
        {
            return (GroupHymnListDates.FormatName(legacyDate), legacyDate, doc?.CreatedBy ?? string.Empty);
        }

        var fallbackCreated = doc?.CreatedAt != default
            ? doc.CreatedAt.UtcDateTime
            : doc?.UpdatedAt.UtcDateTime ?? DateTime.UtcNow;

        return (string.IsNullOrWhiteSpace(listId) ? "Hymn list" : "Hymn list", fallbackCreated, doc?.CreatedBy ?? string.Empty);
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
