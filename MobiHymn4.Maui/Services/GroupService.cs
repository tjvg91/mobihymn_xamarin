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
    readonly IFirebaseFirestoreAccessor firebase;
    readonly IAuthService auth;
    readonly IProfileService profileService;

    public GroupService(IFirebaseFirestoreAccessor firebase, IAuthService auth, IProfileService profileService)
    {
        this.firebase = firebase;
        this.auth = auth;
        this.profileService = profileService;
    }

    public async Task<IReadOnlyList<WorshipGroup>> GetMyGroupsAsync()
    {
        var profile = profileService.CurrentProfile;
        if (profile?.GroupIds == null || profile.GroupIds.Count == 0)
            return Array.Empty<WorshipGroup>();

        var groupIds = profile.GroupIds.Distinct().ToList();
        var snapshots = await Task.WhenAll(groupIds.Select(LoadGroupAsync));
        return snapshots
            .Where(g => g != null)
            .OrderBy(g => g.Name)
            .ToList();
    }

    async Task<WorshipGroup> LoadGroupAsync(string groupId)
    {
        try
        {
            var snapshot = await firebase.Firestore
                .GetCollection(FirestorePaths.Groups)
                .GetDocument(groupId)
                .GetDocumentSnapshotAsync<GroupFirestoreDocument>();

            return snapshot?.Data != null
                ? FirestoreMappers.ToWorshipGroup(snapshot.Data)
                : null;
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

        return FirestoreMappers.ToWorshipGroup(groupDoc);
    }

    public async Task<WorshipGroup> JoinGroupByCodeAsync(string joinCode)
    {
        EnsureSignedIn();
        var normalized = joinCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("Enter a group code.");

        var query = await firebase.Firestore
            .GetCollection(FirestorePaths.Groups)
            .WhereEqualsTo("joinCode", normalized)
            .GetDocumentsAsync<GroupFirestoreDocument>();

        var match = query?.Documents?.FirstOrDefault();
        if (match?.Data == null)
            throw new InvalidOperationException("No group found with that code.");

        var group = FirestoreMappers.ToWorshipGroup(match.Data);
        await AddCurrentUserToGroupAsync(group);
        return group;
    }

    public async Task<WorshipGroup> JoinGroupByIdAsync(string groupId)
    {
        EnsureSignedIn();
        var normalized = groupId?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("Enter a group ID.");

        try
        {
            var snapshot = await firebase.Firestore
                .GetCollection(FirestorePaths.Groups)
                .GetDocument(normalized)
                .GetDocumentSnapshotAsync<GroupFirestoreDocument>();

            if (snapshot?.Data == null)
                throw new InvalidOperationException("No group found with that ID.");

            var group = FirestoreMappers.ToWorshipGroup(snapshot.Data);
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
                || ex.Message?.Contains("permission", StringComparison.OrdinalIgnoreCase) == true)
            {
                throw new InvalidOperationException(
                    "Could not access that group. Make sure your email is verified and Firestore rules are up to date.");
            }

            throw new InvalidOperationException("Unable to join group. Check the ID and try again.");
        }
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
            var snapshot = await firebase.Firestore
                .GetCollection(FirestorePaths.Invites)
                .WhereEqualsTo("email", email)
                .GetDocumentsAsync<InviteFirestoreDocument>();

            if (snapshot?.Documents == null)
                return joined;

            foreach (var doc in snapshot.Documents.Where(d => string.Equals(d.Data?.Status, "pending", StringComparison.OrdinalIgnoreCase)))
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
