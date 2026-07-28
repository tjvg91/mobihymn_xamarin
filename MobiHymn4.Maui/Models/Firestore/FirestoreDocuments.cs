using System;
using System.Collections.Generic;
using Plugin.Firebase.Firestore;

namespace MobiHymn4.Models.Firestore;

public sealed class UserFirestoreDocument : IFirestoreObject
{
    [FirestoreDocumentId]
    public string Id { get; set; }

    [FirestoreProperty("email")]
    public string Email { get; set; }

    [FirestoreProperty("firstName")]
    public string FirstName { get; set; }

    [FirestoreProperty("lastName")]
    public string LastName { get; set; }

    [FirestoreProperty("nickname")]
    public string Nickname { get; set; }

    [FirestoreProperty("displayName")]
    public string DisplayName { get; set; }

    [FirestoreProperty("roles")]
    public IList<string> Roles { get; set; } = new List<string>();

    [FirestoreProperty("notificationsMuted")]
    public bool NotificationsMuted { get; set; }

    [FirestoreProperty("notificationsPreferenceSet")]
    public bool NotificationsPreferenceSet { get; set; }

    [FirestoreProperty("groupIds")]
    public IList<string> GroupIds { get; set; } = new List<string>();
}

public sealed class GroupFirestoreDocument : IFirestoreObject
{
    [FirestoreDocumentId]
    public string Id { get; set; }

    [FirestoreProperty("name")]
    public string Name { get; set; }

    [FirestoreProperty("joinCode")]
    public string JoinCode { get; set; }

    [FirestoreProperty("createdBy")]
    public string CreatedBy { get; set; }

    [FirestoreProperty("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class MemberFirestoreDocument : IFirestoreObject
{
    [FirestoreDocumentId]
    public string Id { get; set; }

    [FirestoreProperty("email")]
    public string Email { get; set; }

    [FirestoreProperty("firstName")]
    public string FirstName { get; set; }

    [FirestoreProperty("lastName")]
    public string LastName { get; set; }

    [FirestoreProperty("nickname")]
    public string Nickname { get; set; }

    [FirestoreProperty("roles")]
    public IList<string> Roles { get; set; } = new List<string>();

    [FirestoreProperty("joinedAt")]
    public DateTimeOffset JoinedAt { get; set; }

    [FirestoreProperty("invitedBy")]
    public string InvitedBy { get; set; }

    [FirestoreProperty("notificationsMuted")]
    public bool NotificationsMuted { get; set; }
}

public sealed class InviteFirestoreDocument : IFirestoreObject
{
    [FirestoreDocumentId]
    public string Id { get; set; }

    [FirestoreProperty("groupId")]
    public string GroupId { get; set; }

    [FirestoreProperty("email")]
    public string Email { get; set; }

    [FirestoreProperty("invitedBy")]
    public string InvitedBy { get; set; }

    [FirestoreProperty("status")]
    public string Status { get; set; }

    [FirestoreProperty("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BoardFirestoreDocument : IFirestoreObject
{
    [FirestoreDocumentId]
    public string Id { get; set; }

    [FirestoreProperty("name")]
    public string Name { get; set; }

    [FirestoreProperty("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [FirestoreProperty("createdBy")]
    public string CreatedBy { get; set; }

    [FirestoreProperty("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [FirestoreProperty("updatedBy")]
    public string UpdatedBy { get; set; }

    [FirestoreProperty("hymns")]
    public IList<BoardHymnFirestoreDocument> Hymns { get; set; } = new List<BoardHymnFirestoreDocument>();

    [FirestoreProperty("hymnCount")]
    public long HymnCount { get; set; }

    [FirestoreProperty("entryCount")]
    public long EntryCount { get; set; }
}

/// <summary>
/// Lightweight board doc for overview listing — omits <c>hymns</c> so nested entries are not deserialized.
/// </summary>
public sealed class BoardListSummaryFirestoreDocument : IFirestoreObject
{
    [FirestoreDocumentId]
    public string Id { get; set; }

    [FirestoreProperty("name")]
    public string Name { get; set; }

    [FirestoreProperty("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [FirestoreProperty("createdBy")]
    public string CreatedBy { get; set; }

    [FirestoreProperty("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [FirestoreProperty("hymnCount")]
    public long HymnCount { get; set; }

    [FirestoreProperty("entryCount")]
    public long EntryCount { get; set; }
}

public sealed class BoardHymnFirestoreDocument : IFirestoreObject
{
    [FirestoreProperty("id")]
    public string Id { get; set; }

    [FirestoreProperty("isSection")]
    public bool IsSection { get; set; }

    [FirestoreProperty("sectionName")]
    public string SectionName { get; set; }

    [FirestoreProperty("hymnNumber")]
    public string HymnNumber { get; set; }

    [FirestoreProperty("sortOrder")]
    public long SortOrder { get; set; }

    [FirestoreProperty("notes")]
    public string Notes { get; set; }

    [FirestoreProperty("addedBy")]
    public string AddedBy { get; set; }

    [FirestoreProperty("addedByName")]
    public string AddedByName { get; set; }

    [FirestoreProperty("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class BoardSectionTemplateFirestoreDocument : IFirestoreObject
{
    [FirestoreDocumentId]
    public string Id { get; set; }

    [FirestoreProperty("sectionNames")]
    public IList<string> SectionNames { get; set; } = new List<string>();

    [FirestoreProperty("autoApply")]
    public bool AutoApply { get; set; }

    [FirestoreProperty("applyFrequency")]
    public string ApplyFrequency { get; set; }

    [FirestoreProperty("anchorDate")]
    public DateTimeOffset? AnchorDate { get; set; }

    [FirestoreProperty("sectionSort")]
    public string SectionSort { get; set; }

    [FirestoreProperty("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [FirestoreProperty("updatedBy")]
    public string UpdatedBy { get; set; }
}

public sealed class FcmTokenFirestoreDocument : IFirestoreObject
{
    [FirestoreDocumentId]
    public string Id { get; set; }

    [FirestoreProperty("token")]
    public string Token { get; set; }

    [FirestoreProperty("platform")]
    public string Platform { get; set; }

    [FirestoreProperty("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class BoardNotificationFirestoreDocument : IFirestoreObject
{
    [FirestoreDocumentId]
    public string Id { get; set; }

    [FirestoreProperty("type")]
    public string Type { get; set; }

    [FirestoreProperty("groupId")]
    public string GroupId { get; set; }

    [FirestoreProperty("listId")]
    public string ListId { get; set; }

    [FirestoreProperty("groupName")]
    public string GroupName { get; set; }

    [FirestoreProperty("updatedBy")]
    public string UpdatedBy { get; set; }

    [FirestoreProperty("updatedByName")]
    public string UpdatedByName { get; set; }

    [FirestoreProperty("changeAction")]
    public string ChangeAction { get; set; }

    [FirestoreProperty("newHymnCount")]
    public long NewHymnCount { get; set; }

    [FirestoreProperty("deletedHymns")]
    public IList<BoardDeletedHymnFirestoreDocument> DeletedHymns { get; set; }

    [FirestoreProperty("title")]
    public string Title { get; set; }

    [FirestoreProperty("body")]
    public string Body { get; set; }

    /// <summary>True when the recipient muted global/group notifications — tray must stay silent.</summary>
    [FirestoreProperty("suppressPush")]
    public bool SuppressPush { get; set; }

    [FirestoreProperty("read")]
    public bool Read { get; set; }

    [FirestoreProperty("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BoardDeletedHymnFirestoreDocument : IFirestoreObject
{
    [FirestoreProperty("id")]
    public string Id { get; set; }

    [FirestoreProperty("hymnNumber")]
    public string HymnNumber { get; set; }

    [FirestoreProperty("notes")]
    public string Notes { get; set; }

    [FirestoreProperty("sortOrder")]
    public long SortOrder { get; set; }

    [FirestoreProperty("addedByName")]
    public string AddedByName { get; set; }
}

/// <summary>Bookmark / history item stored under users/{uid}/appData/settings.</summary>
/// Must implement <see cref="IFirestoreObject"/> so nested list items serialize on Android/iOS.
public sealed class ShortHymnFirestoreDocument : IFirestoreObject
{
    [FirestoreProperty("number")]
    public string Number { get; set; }

    [FirestoreProperty("line")]
    public string Line { get; set; }

    [FirestoreProperty("timeStamp")]
    public DateTimeOffset TimeStamp { get; set; }

    [FirestoreProperty("bookmarkGroup")]
    public string BookmarkGroup { get; set; }
}

/// <summary>Synced reader settings, bookmarks, and recent history.</summary>
public sealed class UserSettingsFirestoreDocument : IFirestoreObject
{
    [FirestoreDocumentId]
    public string Id { get; set; }

    [FirestoreProperty("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [FirestoreProperty("hymnInputType")]
    public int HymnInputType { get; set; }

    [FirestoreProperty("lastHymnNumber")]
    public string LastHymnNumber { get; set; }

    [FirestoreProperty("activeReadTheme")]
    public string ActiveReadTheme { get; set; }

    [FirestoreProperty("activeAlignment")]
    public int ActiveAlignment { get; set; }

    [FirestoreProperty("activeFontSize")]
    public double ActiveFontSize { get; set; }

    [FirestoreProperty("activeFont")]
    public string ActiveFont { get; set; }

    [FirestoreProperty("activeLetterSpacing")]
    public double ActiveLetterSpacing { get; set; }

    [FirestoreProperty("activeLineSpacing")]
    public double ActiveLineSpacing { get; set; }

    [FirestoreProperty("darkMode")]
    public bool DarkMode { get; set; }

    [FirestoreProperty("keepAwake")]
    public bool KeepAwake { get; set; }

    [FirestoreProperty("isOrientationLocked")]
    public bool IsOrientationLocked { get; set; }

    [FirestoreProperty("agentMode")]
    public int AgentMode { get; set; }

    [FirestoreProperty("agentChatLimit")]
    public int AgentChatLimit { get; set; }

    [FirestoreProperty("history")]
    public IList<ShortHymnFirestoreDocument> History { get; set; } = new List<ShortHymnFirestoreDocument>();

    [FirestoreProperty("bookmarks")]
    public IList<ShortHymnFirestoreDocument> Bookmarks { get; set; } = new List<ShortHymnFirestoreDocument>();

    [FirestoreProperty("searches")]
    public IList<string> Searches { get; set; } = new List<string>();
}
