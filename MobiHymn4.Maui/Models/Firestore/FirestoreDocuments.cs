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
