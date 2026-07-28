using System;
using System.Collections.Generic;
using System.Linq;
using MobiHymn4.Utils;
using Microsoft.Maui.Graphics;

namespace MobiHymn4.Models;

public class GroupMemberDisplayItem
{
    public GroupMember Member { get; init; }

    public string DisplayName => Member?.DisplayName ?? string.Empty;

    public string Initials => Member?.GetInitials() ?? "?";

    public Color AvatarTintColor { get; init; } = Colors.Gray;

    public Color AvatarBackgroundColor { get; init; } = Colors.Gray.WithAlpha(0.15f);

    public string Email => Member?.Email ?? string.Empty;

    public string VisibleRolesSummary { get; init; } = string.Empty;

    public bool ShowVisibleRoles { get; init; } = true;

    public bool HasVisibleRoles => !string.IsNullOrWhiteSpace(VisibleRolesSummary);

    public bool ShowRoleSummary => ShowVisibleRoles && HasVisibleRoles;

    public static GroupMemberDisplayItem FromMember(GroupMember member, bool showRoles = true)
    {
        var tint = RoleAvatarHelper.GetTintColor(member?.Roles);
        return new()
        {
            Member = member,
            VisibleRolesSummary = member?.FormatVisibleRoles() ?? string.Empty,
            ShowVisibleRoles = showRoles,
            AvatarTintColor = tint,
            AvatarBackgroundColor = tint.WithAlpha(0.15f),
        };
    }
}

public class GroupMemberRoleSection : List<GroupMemberDisplayItem>
{
    public GroupMemberRoleSection(string title) => Title = title;

    public string Title { get; }
}

public static class GroupMemberRoleExtensions
{
    static readonly UserRole[] RoleDisplayOrder =
    {
        UserRole.Pastor,
        UserRole.WorshipLeader,
        UserRole.Projector,
        UserRole.Accompaniment,
    };

    public static IEnumerable<UserRole> GetVisibleRoles(this GroupMember member) =>
        member?.Roles?
            .Where(r => r != UserRole.Congregant)
            .OrderBy(r => RoleDisplayOrder.Contains(r) ? Array.IndexOf(RoleDisplayOrder, r) : int.MaxValue)
            ?? Enumerable.Empty<UserRole>();

    public static string FormatVisibleRoles(this GroupMember member)
    {
        var roles = member.GetVisibleRoles().Select(r => r.ToDisplayName()).ToList();
        return roles.Count == 0 ? string.Empty : string.Join(", ", roles);
    }

    public static IEnumerable<UserRole> GetFilterableRoles() => RoleDisplayOrder;

    public static string GetInitials(this GroupMember member)
    {
        if (member == null)
            return "?";

        var first = member.FirstName?.Trim() ?? string.Empty;
        var last = member.LastName?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(last))
            return $"{char.ToUpperInvariant(first[0])}{char.ToUpperInvariant(last[0])}";

        if (!string.IsNullOrEmpty(first))
            return first.Length >= 2
                ? first[..2].ToUpperInvariant()
                : char.ToUpperInvariant(first[0]).ToString();

        if (!string.IsNullOrEmpty(last))
            return last.Length >= 2
                ? last[..2].ToUpperInvariant()
                : char.ToUpperInvariant(last[0]).ToString();

        var display = member.DisplayName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(display))
            return "?";

        var parts = display.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";

        return display.Length >= 2
            ? display[..2].ToUpperInvariant()
            : char.ToUpperInvariant(display[0]).ToString();
    }
}
