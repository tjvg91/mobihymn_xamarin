using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MobiHymn4.Models;
using MobiHymn4.Models.Firestore;
using MobiHymn4.Utils;
using Plugin.Firebase.Firestore;

namespace MobiHymn4.Services;

public sealed class GroupService : IGroupService
{
    static readonly TimeSpan GroupsCacheTtl = TimeSpan.FromMinutes(3);
    static readonly TimeSpan PendingInvitesCheckInterval = TimeSpan.FromHours(12);

    readonly IFirebaseFirestoreAccessor firebase;
    readonly IAuthService auth;
    readonly IProfileService profileService;
    IReadOnlyList<WorshipGroup> groupsCache;
    string groupsCacheKey;
    DateTime groupsCacheAt;

    public GroupService(IFirebaseFirestoreAccessor firebase, IAuthService auth, IProfileService profileService)
    {
        this.firebase = firebase;
        this.auth = auth;
        this.profileService = profileService;
        profileService.ProfileChanged += (_, _) => InvalidateGroupsCache();
    }

    public async Task<IReadOnlyList<WorshipGroup>> GetMyGroupsAsync()
    {
        try
        {
            await profileService.RefreshCurrentProfileAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetMyGroupsAsync profile refresh: {ex.Message}");
        }

        var profile = profileService.CurrentProfile;
        if (profile == null || string.IsNullOrWhiteSpace(profile.Uid))
            return Array.Empty<WorshipGroup>();

        var uid = profile.Uid;
        var idSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in profile.GroupIds ?? new List<string>())
        {
            var normalized = NormalizeGroupId(id);
            if (!string.IsNullOrEmpty(normalized))
                idSet.Add(normalized);
        }

        // Membership docs are the source of truth (same as PWA). profile.groupIds can be stale.
        var emailForDiscovery = !string.IsNullOrWhiteSpace(profile.Email)
            ? profile.Email
            : auth.CurrentEmail;
        foreach (var gid in await DiscoverGroupIdsFromMembershipsAsync(emailForDiscovery))
            idSet.Add(gid);

        if (idSet.Count == 0)
            return Array.Empty<WorshipGroup>();

        var cacheKey = $"{uid}|{string.Join(",", idSet.OrderBy(x => x))}";
        if (groupsCache != null
            && string.Equals(groupsCacheKey, cacheKey, StringComparison.Ordinal)
            && DateTime.UtcNow - groupsCacheAt < GroupsCacheTtl)
        {
            return groupsCache;
        }

        var groups = new List<WorshipGroup>();
        foreach (var groupId in idSet)
        {
            try
            {
                if (!await IsCurrentUserMemberAsync(groupId, uid))
                    continue;

                var group = await LoadGroupAsync(groupId);
                if (group != null)
                    groups.Add(group);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetMyGroupsAsync skip {groupId}: {ex.Message}");
            }
        }

        groups = groups.OrderBy(g => g.Name).ToList();
        await RepairGroupIdsAsync(groups.Select(g => g.Id).ToList());

        groupsCache = groups;
        groupsCacheKey = cacheKey;
        groupsCacheAt = DateTime.UtcNow;
        return groups;
    }

    async Task<IReadOnlyList<string>> DiscoverGroupIdsFromMembershipsAsync(string email)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);

        // Prefer email field — collection-group documentId filters are unreliable on some SDKs.
        var normalizedEmail = email?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(normalizedEmail))
        {
            try
            {
                var byEmail = await firebase.Firestore
                    .GetCollectionGroup(FirestorePaths.Members)
                    .WhereEqualsTo("email", normalizedEmail)
                    .LimitedTo(40)
                    .GetDocumentsAsync<MemberFirestoreDocument>();

                foreach (var doc in byEmail?.Documents ?? Array.Empty<IDocumentSnapshot<MemberFirestoreDocument>>())
                {
                    var gid = ExtractGroupIdFromMemberPath(doc);
                    if (!string.IsNullOrEmpty(gid))
                        found.Add(gid);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Membership discovery by email failed: {ex.Message}");
            }
        }

        return found.ToList();
    }

    static string ExtractGroupIdFromMemberPath(IDocumentSnapshot<MemberFirestoreDocument> doc)
    {
        try
        {
            // groups/{groupId}/members/{uid}
            var parent = doc?.Reference?.Parent?.Parent;
            var gid = parent?.Id;
            return string.IsNullOrWhiteSpace(gid) ? null : NormalizeGroupId(gid);
        }
        catch
        {
            return null;
        }
    }

    async Task<bool> IsCurrentUserMemberAsync(string groupId, string uid)
    {
        try
        {
            var snap = await firebase.Firestore
                .GetCollection(FirestorePaths.Groups)
                .GetDocument(groupId)
                .GetCollection(FirestorePaths.Members)
                .GetDocument(uid)
                .GetDocumentSnapshotAsync<MemberFirestoreDocument>();
            return snap?.Data != null;
        }
        catch (Exception ex)
        {
            // Fail open: transient SDK errors must not hide groups that PWA can still see.
            Debug.WriteLine($"IsCurrentUserMemberAsync {groupId}: {ex.Message}");
            return true;
        }
    }

    async Task RepairGroupIdsAsync(IReadOnlyList<string> confirmedIds)
    {
        var profile = profileService.CurrentProfile;
        if (profile == null || !auth.IsSignedIn)
            return;

        var normalizedConfirmed = confirmedIds
            .Select(NormalizeGroupId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var current = profile.GroupIds ?? new List<string>();
        // Never wipe all groupIds on a transient membership false-negative.
        if (normalizedConfirmed.Count == 0 && current.Count > 0)
        {
            Debug.WriteLine("RepairGroupIdsAsync skipped: would clear all groupIds.");
            return;
        }

        if (current.Count == normalizedConfirmed.Count
            && normalizedConfirmed.All(id => current.Contains(id, StringComparer.Ordinal)))
            return;

        try
        {
            profile.GroupIds = normalizedConfirmed;
            await profileService.SaveProfileAsync(profile);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"RepairGroupIdsAsync failed: {ex.Message}");
        }
    }

    void InvalidateGroupsCache()
    {
        groupsCache = null;
        groupsCacheKey = null;
        groupsCacheAt = default;
    }

    async Task<WorshipGroup> LoadGroupAsync(string groupId)
    {
        try
        {
            var snapshot = await firebase.Firestore
                .GetCollection(FirestorePaths.Groups)
                .GetDocument(groupId)
                .GetDocumentSnapshotAsync<GroupFirestoreDocument>();

            if (snapshot?.Data == null)
                return null;

            var group = FirestoreMappers.ToWorshipGroup(snapshot.Data);
            if (string.IsNullOrWhiteSpace(group.Id))
                group.Id = snapshot.Reference?.Id ?? groupId;
            return group;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetMyGroupsAsync failed for {groupId}: {ex.Message}");
            return null;
        }
    }

    public async Task<WorshipGroup> CreateGroupAsync(string name)
    {
        EnsureLeadership();
        var profile = profileService.CurrentProfile;
        var groupId = Guid.NewGuid().ToString("N");
        var joinCode = GenerateJoinCode();

        var groupDoc = new GroupFirestoreDocument
        {
            Id = groupId,
            Name = name?.Trim() ?? "Group",
            JoinCode = joinCode,
            CreatedBy = profile.Uid,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var memberDoc = CreateMemberDoc(profile, profile.Uid, isAdmin: true);

        await firebase.Firestore.GetCollection(FirestorePaths.Groups).GetDocument(groupId).SetDataAsync(groupDoc);
        await firebase.Firestore.GetCollection(FirestorePaths.Groups).GetDocument(groupId)
            .GetCollection(FirestorePaths.Members).GetDocument(profile.Uid).SetDataAsync(memberDoc);

        profile.GroupIds ??= new List<string>();
        if (!profile.GroupIds.Contains(groupId))
            profile.GroupIds.Add(groupId);
        await profileService.SaveProfileAsync(profile);

        InvalidateGroupsCache();
        return FirestoreMappers.ToWorshipGroup(groupDoc);
    }

    public async Task<WorshipGroup> JoinGroupByCodeAsync(string joinCode)
    {
        EnsureSignedIn();
        var normalized = NormalizeJoinCode(joinCode);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("Enter a group code.");

        var query = await firebase.Firestore
            .GetCollection(FirestorePaths.Groups)
            .WhereEqualsTo("joinCode", normalized)
            .GetDocumentsAsync<GroupFirestoreDocument>();

        var match = query?.Documents?.FirstOrDefault();
        if (match?.Data == null)
            throw new InvalidOperationException("No group found with that code.");

        var group = ToWorshipGroup(match);
        await AddCurrentUserToGroupAsync(group);
        return group;
    }

    public async Task<WorshipGroup> JoinGroupByIdAsync(string groupId)
    {
        EnsureSignedIn();
        var raw = groupId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("Enter a group ID.");

        // Short codes are often pasted into the ID field — resolve by join code first.
        if (LooksLikeJoinCode(raw))
            return await JoinGroupByCodeAsync(raw);

        var normalized = NormalizeGroupId(raw);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("Enter a group ID.");

        // Firestore rules / token claims need a fresh ID token on device.
        try
        {
            await auth.RefreshEmailVerificationStatusAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"JoinGroupByIdAsync token refresh: {ex.Message}");
        }

        try
        {
            var docRef = firebase.Firestore
                .GetCollection(FirestorePaths.Groups)
                .GetDocument(normalized);

            IDocumentSnapshot<GroupFirestoreDocument> snapshot = null;
            try
            {
                snapshot = await docRef.GetDocumentSnapshotAsync<GroupFirestoreDocument>(Source.Server);
            }
            catch (Exception serverEx)
            {
                Debug.WriteLine($"JoinGroupByIdAsync server get failed: {serverEx.Message}");
                snapshot = await docRef.GetDocumentSnapshotAsync<GroupFirestoreDocument>();
            }

            if (snapshot?.Data == null)
            {
                // Typed deserialize can fail; try untyped then map.
                var loose = await TryReadGroupLooseAsync(docRef, normalized);
                if (loose != null)
                {
                    await AddCurrentUserToGroupAsync(loose);
                    return loose;
                }

                Debug.WriteLine($"JoinGroupByIdAsync miss for id='{normalized}' len={normalized.Length}");
                throw new InvalidOperationException(
                    normalized.Length == 32
                        ? "No group found with that ID. If this ID is correct, ask a leader for the 6-letter join code, or confirm Firestore rules are deployed."
                        : $"No group found with that ID ({normalized.Length} chars; expected 32). Copy the document ID again, or use the join code.");
            }

            var group = ToWorshipGroup(snapshot, normalized);
            await AddCurrentUserToGroupAsync(group);
            return group;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"JoinGroupByIdAsync failed: {ex.Message}");
            if (ex.Message?.Contains("PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase) == true
                || ex.Message?.Contains("permission", StringComparison.OrdinalIgnoreCase) == true
                || ex.Message?.Contains("Missing or insufficient", StringComparison.OrdinalIgnoreCase) == true)
            {
                throw new InvalidOperationException(
                    "Could not access that group. Verify your email, then try again. If it still fails, deploy updated Firestore rules.");
            }

            throw new InvalidOperationException("Unable to join group. Check the ID and try again.");
        }
    }

    async Task<WorshipGroup> TryReadGroupLooseAsync(IDocumentReference docRef, string fallbackId)
    {
        try
        {
            var snapshot = await docRef.GetDocumentSnapshotAsync<Dictionary<string, object>>(Source.Server);
            var data = snapshot?.Data;
            if (data == null || data.Count == 0)
                return null;

            return new WorshipGroup
            {
                Id = snapshot.Reference?.Id ?? fallbackId,
                Name = data.TryGetValue("name", out var name) ? name?.ToString() ?? string.Empty : string.Empty,
                JoinCode = data.TryGetValue("joinCode", out var code) ? code?.ToString() ?? string.Empty : string.Empty,
                CreatedBy = data.TryGetValue("createdBy", out var by) ? by?.ToString() ?? string.Empty : string.Empty,
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TryReadGroupLooseAsync failed: {ex.Message}");
            return null;
        }
    }

    static WorshipGroup ToWorshipGroup(IDocumentSnapshot<GroupFirestoreDocument> snapshot, string fallbackId = null)
    {
        var group = FirestoreMappers.ToWorshipGroup(snapshot?.Data);
        if (string.IsNullOrWhiteSpace(group.Id))
            group.Id = snapshot?.Reference?.Id ?? fallbackId ?? string.Empty;
        return group;
    }

    static string NormalizeGroupId(string groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId))
            return string.Empty;

        // Strip paste artifacts (spaces, dashes, zero-width chars).
        var chars = groupId.Trim()
            .Where(c => !char.IsWhiteSpace(c)
                && !char.IsControl(c)
                && c != '-'
                && c != '\u200B'
                && c != '\u200C'
                && c != '\u200D'
                && c != '\uFEFF'
                && c != '\u00A0')
            .ToArray();
        var normalized = new string(chars);

        // Document ids from CreateGroup are Guid "N" (lowercase hex).
        if (normalized.Length == 32 && normalized.All(Uri.IsHexDigit))
            return normalized.ToLowerInvariant();

        return normalized;
    }

    static string NormalizeJoinCode(string joinCode) =>
        joinCode?.Trim().ToUpperInvariant() ?? string.Empty;

    static bool LooksLikeJoinCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var code = NormalizeJoinCode(value);
        // Join codes are 6 chars from a limited alphabet; group ids are 32 hex chars.
        return code.Length is >= 4 and <= 8
            && code.All(c => char.IsLetterOrDigit(c));
    }

    public async Task InviteByEmailAsync(string groupId, string email)
    {
        EnsureLeadership();
        var normalizedEmail = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
            throw new InvalidOperationException("Enter an email address.");

        var inviteId = Guid.NewGuid().ToString("N");
        var invite = new InviteFirestoreDocument
        {
            Id = inviteId,
            GroupId = groupId,
            Email = normalizedEmail,
            InvitedBy = auth.CurrentUserId,
            Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await firebase.Firestore.GetCollection(FirestorePaths.Invites).GetDocument(inviteId).SetDataAsync(invite);
    }

    public async Task<IReadOnlyList<GroupMember>> GetMembersAsync(string groupId)
    {
        var normalized = groupId?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return Array.Empty<GroupMember>();

        var snapshot = await firebase.Firestore
            .GetCollection(FirestorePaths.Groups)
            .GetDocument(normalized)
            .GetCollection(FirestorePaths.Members)
            .GetDocumentsAsync<MemberFirestoreDocument>();

        var members = snapshot?.Documents?
            .Select(d => FirestoreMappers.ToGroupMember(d.Data))
            .OrderBy(m => m.DisplayName)
            .ToList() ?? new List<GroupMember>();

        await EnsureCreatorAdminFlagAsync(normalized, members);
        return members;
    }

    public async Task SetMemberAdminAsync(string groupId, string memberId, bool isAdmin)
    {
        EnsureSignedIn();
        var normalized = groupId?.Trim();
        var uid = memberId?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || string.IsNullOrWhiteSpace(uid))
            throw new InvalidOperationException("Member not found.");

        await EnsureCurrentUserIsAdminAsync(normalized);

        await firebase.Firestore
            .GetCollection(FirestorePaths.Groups)
            .GetDocument(normalized)
            .GetCollection(FirestorePaths.Members)
            .GetDocument(uid)
            .UpdateDataAsync(("isAdmin", isAdmin));
    }

    public async Task RemoveMemberAsync(string groupId, string memberId)
    {
        EnsureSignedIn();
        var normalized = groupId?.Trim();
        var uid = memberId?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || string.IsNullOrWhiteSpace(uid))
            throw new InvalidOperationException("Member not found.");

        if (string.Equals(uid, auth.CurrentUserId, StringComparison.Ordinal))
            throw new InvalidOperationException("Use Leave group to remove yourself.");

        await EnsureCurrentUserIsAdminAsync(normalized);

        var members = (await GetMembersAsync(normalized)).ToList();
        var target = members.FirstOrDefault(m => string.Equals(m.Uid, uid, StringComparison.Ordinal));
        if (target == null)
            throw new InvalidOperationException("Member not found.");

        await PromoteSuccessorIfNeededAsync(normalized, members, uid);

        await firebase.Firestore
            .GetCollection(FirestorePaths.Groups)
            .GetDocument(normalized)
            .GetCollection(FirestorePaths.Members)
            .GetDocument(uid)
            .DeleteDocumentAsync();
    }

    public async Task<bool> IsGroupNotificationsMutedAsync(string groupId)
    {
        EnsureSignedIn();
        var normalized = groupId?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        var prefKey = GroupMutePrefKey(normalized);
        if (Preferences.Default.ContainsKey(prefKey))
            return Preferences.Default.Get(prefKey, false);

        var uid = auth.CurrentUserId;
        var snap = await firebase.Firestore
            .GetCollection(FirestorePaths.Groups)
            .GetDocument(normalized)
            .GetCollection(FirestorePaths.Members)
            .GetDocument(uid)
            .GetDocumentSnapshotAsync<MemberFirestoreDocument>();

        var muted = snap?.Data?.NotificationsMuted == true;
        Preferences.Default.Set(prefKey, muted);
        return muted;
    }

    public async Task SetGroupNotificationsMutedAsync(string groupId, bool muted)
    {
        EnsureSignedIn();
        var normalized = groupId?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("Group not found.");

        var uid = auth.CurrentUserId;
        await firebase.Firestore
            .GetCollection(FirestorePaths.Groups)
            .GetDocument(normalized)
            .GetCollection(FirestorePaths.Members)
            .GetDocument(uid)
            .UpdateDataAsync(("notificationsMuted", muted));

        Preferences.Default.Set(GroupMutePrefKey(normalized), muted);
    }

    static string GroupMutePrefKey(string groupId) =>
        PreferencesVar.GROUP_NOTIFICATIONS_MUTED_PREFIX + groupId;

    public async Task LeaveGroupAsync(string groupId)
    {
        EnsureSignedIn();
        var profile = profileService.CurrentProfile;
        if (profile == null)
            throw new InvalidOperationException("Complete your profile before leaving a group.");

        var normalized = groupId?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("Group not found.");

        profile.GroupIds ??= new List<string>();
        if (!profile.GroupIds.Contains(normalized))
            throw new InvalidOperationException("You are not a member of this group.");

        var members = (await GetMembersAsync(normalized)).ToList();
        await PromoteSuccessorIfNeededAsync(normalized, members, profile.Uid);

        await firebase.Firestore
            .GetCollection(FirestorePaths.Groups)
            .GetDocument(normalized)
            .GetCollection(FirestorePaths.Members)
            .GetDocument(profile.Uid)
            .DeleteDocumentAsync();

        profile.GroupIds.Remove(normalized);
        await profileService.SaveProfileAsync(profile);
        InvalidateGroupsCache();
        Preferences.Default.Remove(GroupMutePrefKey(normalized));

        var boardContext = ServiceHelper.Get<BoardContext>();
        if (string.Equals(boardContext.ActiveGroupId, normalized, StringComparison.Ordinal))
        {
            boardContext.ActiveGroupId = string.Empty;
            boardContext.ActiveListId = string.Empty;
            ServiceHelper.Get<BoardNavigationContext>().Clear();
        }
    }

    public async Task<IReadOnlyList<WorshipGroup>> AcceptPendingInvitesAsync()
    {
        if (!auth.IsSignedIn || string.IsNullOrWhiteSpace(auth.CurrentEmail))
            return Array.Empty<WorshipGroup>();

        var joined = new List<WorshipGroup>();
        try
        {
            var email = auth.CurrentEmail.Trim().ToLowerInvariant();
            var lastCheckedTicks = Preferences.Default.Get(PreferencesVar.PENDING_INVITES_CHECKED_AT, 0L);
            if (lastCheckedTicks > 0)
            {
                var lastChecked = new DateTime(lastCheckedTicks, DateTimeKind.Utc);
                if (DateTime.UtcNow - lastChecked < PendingInvitesCheckInterval)
                    return joined;
            }

            var snapshot = await firebase.Firestore
                .GetCollection(FirestorePaths.Invites)
                .WhereEqualsTo("email", email)
                .LimitedTo(40)
                .GetDocumentsAsync<InviteFirestoreDocument>();

            Preferences.Default.Set(PreferencesVar.PENDING_INVITES_CHECKED_AT, DateTime.UtcNow.Ticks);

            if (snapshot?.Documents == null)
                return joined;

            var pending = snapshot.Documents
                .Where(d => string.Equals(d.Data?.Status, "pending", StringComparison.OrdinalIgnoreCase))
                .Take(10)
                .ToList();

            foreach (var doc in pending)
            {
                var invite = doc.Data;
                if (invite == null || string.IsNullOrWhiteSpace(invite.GroupId))
                    continue;

                var groupSnapshot = await firebase.Firestore
                    .GetCollection(FirestorePaths.Groups)
                    .GetDocument(invite.GroupId)
                    .GetDocumentSnapshotAsync<GroupFirestoreDocument>();

                if (groupSnapshot?.Data == null)
                    continue;

                var group = FirestoreMappers.ToWorshipGroup(groupSnapshot.Data);
                await AddCurrentUserToGroupAsync(group);
                joined.Add(group);
                await firebase.Firestore.GetCollection(FirestorePaths.Invites).GetDocument(invite.Id)
                    .UpdateDataAsync(("status", "accepted"));
            }

            if (joined.Count > 0)
                InvalidateGroupsCache();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AcceptPendingInvitesAsync failed: {ex.Message}");
        }

        return joined;
    }

    async Task AddCurrentUserToGroupAsync(WorshipGroup group)
    {
        var profile = profileService.CurrentProfile;
        if (profile == null)
            throw new InvalidOperationException("Complete your profile before joining a group.");

        var memberDoc = CreateMemberDoc(profile, auth.CurrentUserId, isAdmin: false);
        await firebase.Firestore.GetCollection(FirestorePaths.Groups).GetDocument(group.Id)
            .GetCollection(FirestorePaths.Members).GetDocument(profile.Uid).SetDataAsync(memberDoc);

        profile.GroupIds ??= new List<string>();
        if (!profile.GroupIds.Contains(group.Id))
            profile.GroupIds.Add(group.Id);
        await profileService.SaveProfileAsync(profile);
    }

    static MemberFirestoreDocument CreateMemberDoc(UserProfile profile, string invitedBy, bool isAdmin = false)
    {
        return new MemberFirestoreDocument
        {
            Id = profile.Uid,
            Email = profile.Email?.Trim().ToLowerInvariant() ?? string.Empty,
            FirstName = profile.FirstName,
            LastName = profile.LastName,
            Nickname = profile.Nickname,
            Roles = profile.Roles?.Select(r => r.ToStorageKey()).ToList() ?? new List<string>(),
            JoinedAt = DateTimeOffset.UtcNow,
            InvitedBy = invitedBy ?? string.Empty,
            IsAdmin = isAdmin,
        };
    }

    async Task EnsureCurrentUserIsAdminAsync(string groupId)
    {
        var members = await GetMembersAsync(groupId);
        var me = members.FirstOrDefault(m => string.Equals(m.Uid, auth.CurrentUserId, StringComparison.Ordinal));
        if (me?.IsAdmin == true)
            return;

        var createdBy = await GetGroupCreatedByAsync(groupId);
        if (string.Equals(createdBy, auth.CurrentUserId, StringComparison.Ordinal))
            return;

        throw new InvalidOperationException("Only group admins can manage members.");
    }

    async Task EnsureCreatorAdminFlagAsync(string groupId, List<GroupMember> members)
    {
        if (members == null || members.Count == 0)
            return;

        if (members.Any(m => m.IsAdmin))
            return;

        var createdBy = await GetGroupCreatedByAsync(groupId);
        if (string.IsNullOrWhiteSpace(createdBy))
            return;

        var creator = members.FirstOrDefault(m => string.Equals(m.Uid, createdBy, StringComparison.Ordinal));
        if (creator == null)
            return;

        creator.IsAdmin = true;
        try
        {
            await firebase.Firestore
                .GetCollection(FirestorePaths.Groups)
                .GetDocument(groupId)
                .GetCollection(FirestorePaths.Members)
                .GetDocument(creator.Uid)
                .UpdateDataAsync(("isAdmin", true));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"EnsureCreatorAdminFlagAsync failed: {ex.Message}");
        }
    }

    async Task PromoteSuccessorIfNeededAsync(string groupId, IList<GroupMember> members, string departingUid)
    {
        var remaining = members
            .Where(m => !string.Equals(m.Uid, departingUid, StringComparison.Ordinal))
            .ToList();
        if (remaining.Count == 0)
            return;

        var departing = members.FirstOrDefault(m => string.Equals(m.Uid, departingUid, StringComparison.Ordinal));
        var createdBy = await GetGroupCreatedByAsync(groupId);
        var departingIsAdmin = departing?.IsAdmin == true
            || string.Equals(departingUid, createdBy, StringComparison.Ordinal);
        if (!departingIsAdmin)
            return;

        if (remaining.Any(m => m.IsAdmin || string.Equals(m.Uid, createdBy, StringComparison.Ordinal)))
            return;

        var successor = remaining
            .OrderBy(m => m.JoinedAt == default ? DateTime.MaxValue : m.JoinedAt)
            .ThenBy(m => m.Uid, StringComparer.Ordinal)
            .First();

        try
        {
            await firebase.Firestore
                .GetCollection(FirestorePaths.Groups)
                .GetDocument(groupId)
                .GetCollection(FirestorePaths.Members)
                .GetDocument(successor.Uid)
                .UpdateDataAsync(("isAdmin", true));
            successor.IsAdmin = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PromoteSuccessorIfNeededAsync failed: {ex.Message}");
            throw new InvalidOperationException("Could not assign a new group admin before leaving.", ex);
        }
    }

    async Task<string> GetGroupCreatedByAsync(string groupId)
    {
        try
        {
            var snap = await firebase.Firestore
                .GetCollection(FirestorePaths.Groups)
                .GetDocument(groupId)
                .GetDocumentSnapshotAsync<GroupFirestoreDocument>();
            return snap?.Data?.CreatedBy?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    void EnsureSignedIn()
    {
        if (!auth.IsSignedIn)
            throw new InvalidOperationException("Sign in to continue.");
        if (!auth.IsEmailVerified)
            throw new InvalidOperationException("Verify your email to continue.");
    }

    void EnsureLeadership()
    {
        EnsureSignedIn();
        if (!RolePermissions.HasLeadershipRole(profileService.CurrentProfile?.Roles))
            throw new InvalidOperationException("Your role cannot manage groups.");
    }

    static string GenerateJoinCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var random = Random.Shared;
        return new string(Enumerable.Range(0, 6).Select(_ => chars[random.Next(chars.Length)]).ToArray());
    }
}
