using System.Collections.Generic;
using System.Linq;
using MobiHymn4.Utils;

namespace MobiHymn4.Models;

public class UserProfile
{
    public string Uid { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public List<UserRole> Roles { get; set; } = new();
    public bool NotificationsMuted { get; set; }
    /// <summary>True once the user explicitly mutes/unmutes on Account.</summary>
    public bool NotificationsPreferenceSet { get; set; }
    public List<string> GroupIds { get; set; } = new();

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Nickname)
            ? Nickname.Trim()
            : $"{FirstName} {LastName}".Trim();

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(FirstName)
        && !string.IsNullOrWhiteSpace(LastName)
        && Roles != null
        && Roles.Count > 0;

    public void ApplyDefaultNotificationPreferenceIfNeeded()
    {
        if (NotificationsPreferenceSet)
            return;

        NotificationsMuted = RolePermissions.GetDefaultNotificationsMuted(Roles);
    }

    public static UserProfile FromFirestore(string uid, IDictionary<string, object> data)
    {
        var profile = new UserProfile { Uid = uid };
        if (data == null)
            return profile;

        if (data.TryGetValue("email", out var email))
            profile.Email = email?.ToString() ?? string.Empty;
        if (data.TryGetValue("firstName", out var firstName))
            profile.FirstName = firstName?.ToString() ?? string.Empty;
        if (data.TryGetValue("lastName", out var lastName))
            profile.LastName = lastName?.ToString() ?? string.Empty;
        if (data.TryGetValue("nickname", out var nickname))
            profile.Nickname = nickname?.ToString() ?? string.Empty;
        if (data.TryGetValue("notificationsMuted", out var muted) && muted is bool b)
            profile.NotificationsMuted = b;
        if (data.TryGetValue("notificationsPreferenceSet", out var prefSet) && prefSet is bool pref)
            profile.NotificationsPreferenceSet = pref;
        if (data.TryGetValue("roles", out var rolesObj) && rolesObj is IEnumerable<object> roleItems)
            profile.Roles = roleItems.Select(r => UserRoleExtensions.Parse(r?.ToString())).Where(r => r.HasValue).Select(r => r.Value).ToList();
        if (data.TryGetValue("groupIds", out var groupIdsObj) && groupIdsObj is IEnumerable<object> groupItems)
            profile.GroupIds = groupItems.Select(g => g?.ToString()).Where(g => !string.IsNullOrWhiteSpace(g)).ToList();

        return profile;
    }

    public Dictionary<string, object> ToFirestore()
    {
        return new Dictionary<string, object>
        {
            ["email"] = Email ?? string.Empty,
            ["firstName"] = FirstName?.Trim() ?? string.Empty,
            ["lastName"] = LastName?.Trim() ?? string.Empty,
            ["nickname"] = Nickname?.Trim() ?? string.Empty,
            ["displayName"] = DisplayName,
            ["roles"] = Roles?.Select(r => r.ToStorageKey()).ToList() ?? new List<string>(),
            ["notificationsMuted"] = NotificationsMuted,
            ["notificationsPreferenceSet"] = NotificationsPreferenceSet,
            ["groupIds"] = GroupIds ?? new List<string>(),
        };
    }
}
