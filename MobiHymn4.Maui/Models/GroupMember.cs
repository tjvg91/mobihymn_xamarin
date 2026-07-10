using System;
using System.Collections.Generic;
using System.Linq;
using MobiHymn4.Utils;

namespace MobiHymn4.Models;

public class GroupMember
{
    public string Uid { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public List<UserRole> Roles { get; set; } = new();
    public DateTime JoinedAt { get; set; }
    public string InvitedBy { get; set; } = string.Empty;

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Nickname)
            ? Nickname.Trim()
            : $"{FirstName} {LastName}".Trim();

    public static GroupMember FromFirestore(string uid, IDictionary<string, object> data)
    {
        var member = new GroupMember { Uid = uid };
        if (data == null)
            return member;

        if (data.TryGetValue("email", out var email))
            member.Email = email?.ToString() ?? string.Empty;
        if (data.TryGetValue("firstName", out var firstName))
            member.FirstName = firstName?.ToString() ?? string.Empty;
        if (data.TryGetValue("lastName", out var lastName))
            member.LastName = lastName?.ToString() ?? string.Empty;
        if (data.TryGetValue("nickname", out var nickname))
            member.Nickname = nickname?.ToString() ?? string.Empty;
        if (data.TryGetValue("roles", out var rolesObj) && rolesObj is IEnumerable<object> roleItems)
            member.Roles = roleItems.Select(r => UserRoleExtensions.Parse(r?.ToString())).Where(r => r.HasValue).Select(r => r.Value).ToList();
        if (data.TryGetValue("joinedAt", out var joinedAt) && joinedAt is DateTime dt)
            member.JoinedAt = dt;
        if (data.TryGetValue("invitedBy", out var invitedBy))
            member.InvitedBy = invitedBy?.ToString() ?? string.Empty;

        return member;
    }

    public Dictionary<string, object> ToFirestore()
    {
        return new Dictionary<string, object>
        {
            ["email"] = Email ?? string.Empty,
            ["firstName"] = FirstName?.Trim() ?? string.Empty,
            ["lastName"] = LastName?.Trim() ?? string.Empty,
            ["nickname"] = Nickname?.Trim() ?? string.Empty,
            ["roles"] = Roles?.Select(r => r.ToStorageKey()).ToList() ?? new List<string>(),
            ["joinedAt"] = JoinedAt == default ? DateTime.UtcNow : JoinedAt,
            ["invitedBy"] = InvitedBy ?? string.Empty,
        };
    }
}
