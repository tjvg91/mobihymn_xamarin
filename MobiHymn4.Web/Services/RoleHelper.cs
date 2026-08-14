using MobiHymn4.Shared.Models;

namespace MobiHymn4.Web.Services;

/// <summary>Profile / member role helpers shared by board UI chrome.</summary>
public static class RoleHelper
{
    public static bool IsCongregant(string? role) =>
        !string.IsNullOrWhiteSpace(role)
        && string.Equals(role.Trim(), "congregant", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the user has any non-congregant role (can edit boards / add hymns).</summary>
    public static bool HasLeadershipRole(IEnumerable<string>? roles) =>
        roles != null && roles.Any(r => !string.IsNullOrWhiteSpace(r) && !IsCongregant(r));

    /// <summary>Add-to-board is leadership-only (not congregant-only accounts).</summary>
    public static bool CanAddToBoard(UserProfileDoc? profile) =>
        HasLeadershipRole(profile?.Roles);
}
