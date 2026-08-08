using System.Text.Json;
using MobiHymn4.Shared;
using MobiHymn4.Shared.Models;
using MobiHymn4.Shared.Services;

namespace MobiHymn4.Web.Services;

public sealed class FirebaseProfileService : IProfileService
{
    readonly FirebaseJs firebase;
    readonly IAuthService auth;

    public FirebaseProfileService(FirebaseJs firebase, IAuthService auth)
    {
        this.firebase = firebase;
        this.auth = auth;
        auth.AuthStateChanged += async (_, _) =>
        {
            if (auth.IsSignedIn)
                await RefreshCurrentProfileAsync();
            else
            {
                CurrentProfile = null;
                ProfileChanged?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    public event EventHandler? ProfileChanged;

    public UserProfileDoc? CurrentProfile { get; private set; }

    public bool HasCompleteProfile =>
        CurrentProfile != null
        && !string.IsNullOrWhiteSpace(CurrentProfile.FirstName)
        && !string.IsNullOrWhiteSpace(CurrentProfile.LastName)
        && CurrentProfile.Roles is { Count: > 0 };

    public async Task RefreshCurrentProfileAsync()
    {
        if (!auth.IsSignedIn)
        {
            CurrentProfile = null;
            ProfileChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        await firebase.EnsureReadyAsync();
        var doc = await firebase.GetDocAsync($"{FirestorePaths.Users}/{auth.CurrentUserId}");
        CurrentProfile = MapProfile(doc, auth.CurrentUserId, auth.CurrentEmail);
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveProfileAsync(UserProfileDoc profile)
    {
        await firebase.EnsureReadyAsync();
        profile.Id = auth.CurrentUserId;
        profile.Email = auth.CurrentEmail;
        profile.DisplayName = string.IsNullOrWhiteSpace(profile.Nickname)
            ? $"{profile.FirstName} {profile.LastName}".Trim()
            : profile.Nickname;

        await firebase.SetDocAsync($"{FirestorePaths.Users}/{auth.CurrentUserId}", new
        {
            email = profile.Email,
            firstName = profile.FirstName,
            lastName = profile.LastName,
            nickname = profile.Nickname,
            displayName = profile.DisplayName,
            roles = profile.Roles,
            groupIds = profile.GroupIds,
            notificationsMuted = profile.NotificationsMuted,
            notificationsPreferenceSet = profile.NotificationsPreferenceSet
        });
        CurrentProfile = profile;
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetNotificationsMutedAsync(bool muted)
    {
        if (!auth.IsSignedIn) return;
        await firebase.EnsureReadyAsync();
        await firebase.SetDocAsync($"{FirestorePaths.Users}/{auth.CurrentUserId}", new
        {
            notificationsMuted = muted,
            notificationsPreferenceSet = true
        });

        if (CurrentProfile != null)
        {
            CurrentProfile.NotificationsMuted = muted;
            CurrentProfile.NotificationsPreferenceSet = true;
        }

        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    static UserProfileDoc MapProfile(JsonElement? doc, string uid, string email)
    {
        if (doc == null || doc.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new UserProfileDoc { Id = uid, Email = email };
        }

        var d = doc.Value;
        return new UserProfileDoc
        {
            Id = uid,
            Email = GetString(d, "email") ?? email,
            FirstName = GetString(d, "firstName") ?? "",
            LastName = GetString(d, "lastName") ?? "",
            Nickname = GetString(d, "nickname") ?? "",
            DisplayName = GetString(d, "displayName") ?? "",
            Roles = GetStringList(d, "roles"),
            GroupIds = GetStringList(d, "groupIds"),
            NotificationsMuted = d.TryGetProperty("notificationsMuted", out var m) && m.ValueKind == JsonValueKind.True,
            NotificationsPreferenceSet = d.TryGetProperty("notificationsPreferenceSet", out var p) && p.ValueKind == JsonValueKind.True
        };
    }

    internal static string? GetString(JsonElement d, string name) =>
        d.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    internal static List<string> GetStringList(JsonElement d, string name)
    {
        if (!d.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Array)
            return new List<string>();
        return p.EnumerateArray()
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToList();
    }
}

public sealed class FirebaseGroupService : IGroupService
{
    readonly FirebaseJs firebase;
    readonly IAuthService auth;
    readonly IProfileService profiles;
    readonly IAppPreferences prefs;

    public FirebaseGroupService(FirebaseJs firebase, IAuthService auth, IProfileService profiles, IAppPreferences prefs)
    {
        this.firebase = firebase;
        this.auth = auth;
        this.profiles = profiles;
        this.prefs = prefs;
    }

    public async Task<IReadOnlyList<WorshipGroupDoc>> GetMyGroupsAsync()
    {
        await profiles.RefreshCurrentProfileAsync();
        var uid = auth.CurrentUserId;
        var idSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in profiles.CurrentProfile?.GroupIds ?? new List<string>())
        {
            var normalized = NormalizeGroupId(id);
            if (!string.IsNullOrEmpty(normalized))
                idSet.Add(normalized);
        }

        // Membership docs are the source of truth for board access. groupIds can be stale/incomplete.
        try
        {
            await firebase.EnsureReadyAsync();
            var memberships = await firebase.QueryCollectionGroupAsync(FirestorePaths.Members, uid);
            foreach (var row in memberships ?? Array.Empty<JsonElement>())
            {
                var gid = FirebaseProfileService.GetString(row, "groupId");
                if (string.IsNullOrEmpty(gid) && row.TryGetProperty("_path", out var pathEl))
                {
                    var parts = (pathEl.GetString() ?? "").Split('/');
                    if (parts.Length >= 2 && parts[0] == FirestorePaths.Groups)
                        gid = parts[1];
                }

                gid = NormalizeGroupId(gid);
                if (!string.IsNullOrEmpty(gid))
                    idSet.Add(gid);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Membership discovery failed: {ex.Message}");
        }

        var list = new List<WorshipGroupDoc>();
        foreach (var id in idSet)
        {
            try
            {
                // Only groups where this account actually has a members/{uid} doc can read boards.
                var member = await firebase.GetDocAsync($"{FirestorePaths.Groups}/{id}/{FirestorePaths.Members}/{uid}");
                if (member == null || member.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    continue;

                var doc = await firebase.GetDocAsync($"{FirestorePaths.Groups}/{id}");
                if (doc == null || doc.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    continue;
                list.Add(MapGroup(id, doc.Value));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetMyGroups skip {id}: {ex.Message}");
            }
        }

        await RepairGroupIdsAsync(list.Select(g => g.Id).ToList());
        return list.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    async Task RepairGroupIdsAsync(IReadOnlyList<string> confirmedIds)
    {
        var profile = profiles.CurrentProfile;
        if (profile == null || !auth.IsSignedIn)
            return;

        var current = profile.GroupIds ?? new List<string>();
        var normalizedConfirmed = confirmedIds
            .Select(NormalizeGroupId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (current.Count == normalizedConfirmed.Count
            && normalizedConfirmed.All(id => current.Contains(id, StringComparer.Ordinal)))
            return;

        try
        {
            profile.GroupIds = normalizedConfirmed;
            await profiles.SaveProfileAsync(profile);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RepairGroupIds failed: {ex.Message}");
        }
    }

    static string NormalizeGroupId(string? groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId))
            return "";

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
        if (normalized.Length == 32 && normalized.All(Uri.IsHexDigit))
            return normalized.ToLowerInvariant();
        return normalized;
    }

    public async Task<WorshipGroupDoc> CreateGroupAsync(string name)
    {
        await firebase.EnsureReadyAsync();
        var id = Guid.NewGuid().ToString("N")[..20];
        var code = Random.Shared.Next(100000, 999999).ToString();
        var group = new WorshipGroupDoc
        {
            Id = id,
            Name = name.Trim(),
            JoinCode = code,
            CreatedBy = auth.CurrentUserId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await firebase.SetDocAsync($"{FirestorePaths.Groups}/{id}", new
        {
            name = group.Name,
            joinCode = group.JoinCode,
            createdBy = group.CreatedBy,
            createdAt = group.CreatedAt
        });

        var profile = profiles.CurrentProfile ?? new UserProfileDoc { Id = auth.CurrentUserId, Email = auth.CurrentEmail };
        if (!profile.GroupIds.Contains(id))
            profile.GroupIds.Add(id);
        await profiles.SaveProfileAsync(profile);

        await firebase.SetDocAsync($"{FirestorePaths.Groups}/{id}/{FirestorePaths.Members}/{auth.CurrentUserId}", new
        {
            email = auth.CurrentEmail,
            firstName = profile.FirstName,
            lastName = profile.LastName,
            nickname = profile.Nickname,
            roles = profile.Roles,
            joinedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            isAdmin = true
        });

        return group;
    }

    public async Task<WorshipGroupDoc> JoinGroupAsync(string joinCode)
    {
        await firebase.EnsureReadyAsync();
        var matches = await firebase.QueryCollectionAsync(FirestorePaths.Groups, "joinCode", "==", joinCode.Trim());
        if (matches.Length == 0)
            throw new InvalidOperationException("No group found for that join code.");

        var doc = matches[0];
        var id = FirebaseProfileService.GetString(doc, "id") ?? FirebaseProfileService.GetString(doc, "Id") ?? "";
        if (string.IsNullOrEmpty(id) && doc.TryGetProperty("_id", out var idProp))
            id = idProp.GetString() ?? "";

        // JS helper should include id field
        if (string.IsNullOrEmpty(id))
            throw new InvalidOperationException("Group document missing id.");

        var group = MapGroup(id, doc);
        var profile = profiles.CurrentProfile ?? new UserProfileDoc { Id = auth.CurrentUserId, Email = auth.CurrentEmail };
        if (!profile.GroupIds.Contains(id))
            profile.GroupIds.Add(id);
        await profiles.SaveProfileAsync(profile);

        await firebase.SetDocAsync($"{FirestorePaths.Groups}/{id}/{FirestorePaths.Members}/{auth.CurrentUserId}", new
        {
            email = auth.CurrentEmail,
            firstName = profile.FirstName,
            lastName = profile.LastName,
            nickname = profile.Nickname,
            roles = profile.Roles,
            joinedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            isAdmin = false
        });

        return group;
    }

    public async Task LeaveGroupAsync(string groupId)
    {
        await firebase.EnsureReadyAsync();
        var members = (await GetMembersAsync(groupId)).ToList();
        await PromoteSuccessorIfNeededAsync(groupId, members, auth.CurrentUserId);

        await firebase.DeleteDocAsync($"{FirestorePaths.Groups}/{groupId}/{FirestorePaths.Members}/{auth.CurrentUserId}");
        var profile = profiles.CurrentProfile;
        if (profile != null)
        {
            profile.GroupIds = profile.GroupIds.Where(g => g != groupId).ToList();
            await profiles.SaveProfileAsync(profile);
        }
    }

    public async Task<IReadOnlyList<GroupMemberDoc>> GetMembersAsync(string groupId)
    {
        await firebase.EnsureReadyAsync();
        var rows = await firebase.QueryCollectionAsync($"{FirestorePaths.Groups}/{groupId}/{FirestorePaths.Members}");
        var members = rows.Select(MapMember).ToList();
        await EnsureCreatorAdminFlagAsync(groupId, members);
        return members;
    }

    public async Task SetMemberAdminAsync(string groupId, string memberId, bool isAdmin)
    {
        await firebase.EnsureReadyAsync();
        var gid = groupId?.Trim() ?? "";
        var uid = memberId?.Trim() ?? "";
        if (string.IsNullOrEmpty(gid) || string.IsNullOrEmpty(uid))
            throw new InvalidOperationException("Member not found.");

        await EnsureCurrentUserIsAdminAsync(gid);
        await firebase.SetDocAsync(
            $"{FirestorePaths.Groups}/{gid}/{FirestorePaths.Members}/{uid}",
            new { isAdmin });
    }

    public async Task RemoveMemberAsync(string groupId, string memberId)
    {
        await firebase.EnsureReadyAsync();
        var gid = groupId?.Trim() ?? "";
        var uid = memberId?.Trim() ?? "";
        if (string.IsNullOrEmpty(gid) || string.IsNullOrEmpty(uid))
            throw new InvalidOperationException("Member not found.");

        if (string.Equals(uid, auth.CurrentUserId, StringComparison.Ordinal))
            throw new InvalidOperationException("Use Leave group to remove yourself.");

        await EnsureCurrentUserIsAdminAsync(gid);
        var members = (await GetMembersAsync(gid)).ToList();
        if (!members.Any(m => string.Equals(m.Id, uid, StringComparison.Ordinal)))
            throw new InvalidOperationException("Member not found.");

        await PromoteSuccessorIfNeededAsync(gid, members, uid);
        await firebase.DeleteDocAsync($"{FirestorePaths.Groups}/{gid}/{FirestorePaths.Members}/{uid}");
    }

    public async Task<bool> IsGroupNotificationsMutedAsync(string groupId)
    {
        var id = groupId?.Trim() ?? "";
        if (string.IsNullOrEmpty(id)) return false;

        var key = PrefKeys.GroupMutePrefix + id;
        if (!string.IsNullOrEmpty(prefs.Get(key, "")))
            return prefs.GetBool(key);

        try
        {
            await firebase.EnsureReadyAsync();
            var doc = await firebase.GetDocAsync($"{FirestorePaths.Groups}/{id}/{FirestorePaths.Members}/{auth.CurrentUserId}");
            var muted = doc != null
                && doc.Value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
                && doc.Value.TryGetProperty("notificationsMuted", out var m)
                && m.ValueKind == JsonValueKind.True;
            prefs.SetBool(key, muted);
            return muted;
        }
        catch
        {
            return false;
        }
    }

    public async Task SetGroupNotificationsMutedAsync(string groupId, bool muted)
    {
        var id = groupId?.Trim() ?? "";
        if (string.IsNullOrEmpty(id))
            throw new InvalidOperationException("Group not found.");

        await firebase.EnsureReadyAsync();
        await firebase.SetDocAsync(
            $"{FirestorePaths.Groups}/{id}/{FirestorePaths.Members}/{auth.CurrentUserId}",
            new { notificationsMuted = muted });
        prefs.SetBool(PrefKeys.GroupMutePrefix + id, muted);
    }

    async Task EnsureCurrentUserIsAdminAsync(string groupId)
    {
        var members = await GetMembersAsync(groupId);
        var me = members.FirstOrDefault(m => string.Equals(m.Id, auth.CurrentUserId, StringComparison.Ordinal));
        if (me?.IsAdmin == true)
            return;

        var createdBy = await GetGroupCreatedByAsync(groupId);
        if (string.Equals(createdBy, auth.CurrentUserId, StringComparison.Ordinal))
            return;

        throw new InvalidOperationException("Only group admins can manage members.");
    }

    async Task EnsureCreatorAdminFlagAsync(string groupId, List<GroupMemberDoc> members)
    {
        if (members.Count == 0 || members.Any(m => m.IsAdmin))
            return;

        var createdBy = await GetGroupCreatedByAsync(groupId);
        if (string.IsNullOrWhiteSpace(createdBy))
            return;

        var creator = members.FirstOrDefault(m => string.Equals(m.Id, createdBy, StringComparison.Ordinal));
        if (creator == null)
            return;

        creator.IsAdmin = true;
        try
        {
            await firebase.SetDocAsync(
                $"{FirestorePaths.Groups}/{groupId}/{FirestorePaths.Members}/{creator.Id}",
                new { isAdmin = true });
        }
        catch
        {
            // Best-effort migration for older groups.
        }
    }

    async Task PromoteSuccessorIfNeededAsync(string groupId, IList<GroupMemberDoc> members, string departingUid)
    {
        var remaining = members
            .Where(m => !string.Equals(m.Id, departingUid, StringComparison.Ordinal))
            .ToList();
        if (remaining.Count == 0)
            return;

        var departing = members.FirstOrDefault(m => string.Equals(m.Id, departingUid, StringComparison.Ordinal));
        var createdBy = await GetGroupCreatedByAsync(groupId);
        var departingIsAdmin = departing?.IsAdmin == true
            || string.Equals(departingUid, createdBy, StringComparison.Ordinal);
        if (!departingIsAdmin)
            return;

        if (remaining.Any(m => m.IsAdmin || string.Equals(m.Id, createdBy, StringComparison.Ordinal)))
            return;

        var successor = remaining
            .OrderBy(m => m.JoinedAt == default ? DateTimeOffset.MaxValue : m.JoinedAt)
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .First();

        await firebase.SetDocAsync(
            $"{FirestorePaths.Groups}/{groupId}/{FirestorePaths.Members}/{successor.Id}",
            new { isAdmin = true });
        successor.IsAdmin = true;
    }

    async Task<string> GetGroupCreatedByAsync(string groupId)
    {
        try
        {
            var doc = await firebase.GetDocAsync($"{FirestorePaths.Groups}/{groupId}");
            if (doc == null || doc.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return "";
            return FirebaseProfileService.GetString(doc.Value, "createdBy") ?? "";
        }
        catch
        {
            return "";
        }
    }

    static GroupMemberDoc MapMember(JsonElement r)
    {
        var joined = DateTimeOffset.UtcNow;
        if (r.TryGetProperty("joinedAt", out var j))
        {
            if (j.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(j.GetString(), out var parsed))
                joined = parsed;
            else if (j.ValueKind == JsonValueKind.Number && j.TryGetInt64(out var ms))
                joined = DateTimeOffset.FromUnixTimeMilliseconds(ms);
        }

        return new GroupMemberDoc
        {
            Id = FirebaseProfileService.GetString(r, "id") ?? FirebaseProfileService.GetString(r, "_id") ?? "",
            Email = FirebaseProfileService.GetString(r, "email") ?? "",
            FirstName = FirebaseProfileService.GetString(r, "firstName") ?? "",
            LastName = FirebaseProfileService.GetString(r, "lastName") ?? "",
            Nickname = FirebaseProfileService.GetString(r, "nickname") ?? "",
            Roles = FirebaseProfileService.GetStringList(r, "roles"),
            JoinedAt = joined,
            IsAdmin = r.TryGetProperty("isAdmin", out var a) && a.ValueKind == JsonValueKind.True
        };
    }

    static WorshipGroupDoc MapGroup(string id, JsonElement d) => new()
    {
        Id = id,
        Name = FirebaseProfileService.GetString(d, "name") ?? "",
        JoinCode = FirebaseProfileService.GetString(d, "joinCode") ?? "",
        CreatedBy = FirebaseProfileService.GetString(d, "createdBy") ?? ""
    };
}
