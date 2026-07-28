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
        var profile = profileService.CurrentProfile;
        if (profile?.GroupIds == null || profile.GroupIds.Count == 0)
            return Array.Empty<WorshipGroup>();

        var groupIds = profile.GroupIds.Distinct().ToList();
        var cacheKey = $"{profile.Uid}|{string.Join(",", groupIds)}";
        if (groupsCache != null
            && string.Equals(groupsCacheKey, cacheKey, StringComparison.Ordinal)
            && DateTime.UtcNow - groupsCacheAt < GroupsCacheTtl)
        {
            return groupsCache;
        }

        var snapshots = await Task.WhenAll(groupIds.Select(LoadGroupAsync));
        var groups = snapshots
            .Where(g => g != null)
            .OrderBy(g => g.Name)
            .ToList();

        groupsCache = groups;
        groupsCacheKey = cacheKey;
        groupsCacheAt = DateTime.UtcNow;
        return groups;
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

        var memberDoc = CreateMemberDoc(profile, profile.Uid);

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
        var snapshot = await firebase.Firestore
            .GetCollection(FirestorePaths.Groups)
            .GetDocument(groupId)
            .GetCollection(FirestorePaths.Members)
            .GetDocumentsAsync<MemberFirestoreDocument>();

        return snapshot?.Documents?
            .Select(d => FirestoreMappers.ToGroupMember(d.Data))
            .OrderBy(m => m.DisplayName)
            .ToList() ?? new List<GroupMember>();
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

        var memberDoc = CreateMemberDoc(profile, auth.CurrentUserId);
        await firebase.Firestore.GetCollection(FirestorePaths.Groups).GetDocument(group.Id)
            .GetCollection(FirestorePaths.Members).GetDocument(profile.Uid).SetDataAsync(memberDoc);

        profile.GroupIds ??= new List<string>();
        if (!profile.GroupIds.Contains(group.Id))
            profile.GroupIds.Add(group.Id);
        await profileService.SaveProfileAsync(profile);
    }

    static MemberFirestoreDocument CreateMemberDoc(UserProfile profile, string invitedBy)
    {
        return new MemberFirestoreDocument
        {
            Id = profile.Uid,
            Email = profile.Email,
            FirstName = profile.FirstName,
            LastName = profile.LastName,
            Nickname = profile.Nickname,
            Roles = profile.Roles?.Select(r => r.ToStorageKey()).ToList() ?? new List<string>(),
            JoinedAt = DateTimeOffset.UtcNow,
            InvitedBy = invitedBy ?? string.Empty,
        };
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
