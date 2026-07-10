using System;
using System.Collections.Generic;
using System.Linq;
using FontAwesome;
using MobiHymn4.Models;
using Microsoft.Maui.Graphics;

namespace MobiHymn4.Utils;

public static class UserRoleExtensions
{
    public static string ToStorageKey(this UserRole role) => role switch
    {
        UserRole.Pastor => "pastor",
        UserRole.WorshipLeader => "worshipLeader",
        UserRole.Projector => "projector",
        UserRole.Accompaniment => "accompaniment",
        _ => "congregant",
    };

    public static string ToDisplayName(this UserRole role) => role switch
    {
        UserRole.Pastor => "Preacher",
        UserRole.WorshipLeader => "Worship Leader",
        UserRole.Projector => "Projector",
        UserRole.Accompaniment => "Accompaniment",
        _ => "Congregant",
    };

    public static UserRole? Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim().ToLowerInvariant() switch
        {
            "pastor" or "preacher" => UserRole.Pastor,
            "worshipleader" or "worship_leader" or "worship leader" => UserRole.WorshipLeader,
            "projector" => UserRole.Projector,
            "accompaniment" => UserRole.Accompaniment,
            "congregant" => UserRole.Congregant,
            _ => null,
        };
    }
}

public static class RolePermissions
{
    public static bool HasLeadershipRole(IEnumerable<UserRole> roles) =>
        roles != null && roles.Any(r => r != UserRole.Congregant);

    public static UserRole GetPrimaryRole(IEnumerable<UserRole> roles)
    {
        if (roles == null || !roles.Any())
            return UserRole.Congregant;

        var ordered = new[] { UserRole.Pastor, UserRole.WorshipLeader, UserRole.Projector, UserRole.Accompaniment, UserRole.Congregant };
        return ordered.FirstOrDefault(r => roles.Contains(r));
    }
}

public static class RoleAvatarHelper
{
    public static string GetIconGlyph(UserRole role) => FontAwesomeIcons.User;

    public static string GetIconGlyph(IEnumerable<UserRole> roles) => FontAwesomeIcons.User;

    public static Color GetTintColor(UserRole role)
    {
        var resources = Application.Current?.Resources;
        if (resources == null)
            return Colors.Gray;

        return role switch
        {
            UserRole.Pastor => (Color)(resources["Primary"] ?? Colors.Goldenrod),
            UserRole.WorshipLeader => Color.FromArgb("#3B82F6"),
            UserRole.Projector => Color.FromArgb("#6B7280"),
            UserRole.Accompaniment => Color.FromArgb("#8B5CF6"),
            _ => Color.FromArgb("#10B981"),
        };
    }

    public static Color GetTintColor(IEnumerable<UserRole> roles) =>
        GetTintColor(RolePermissions.GetPrimaryRole(roles));
}
