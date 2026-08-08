namespace MobiHymn4.Shared.Models;

public class UserProfileDoc
{
    public string Id { get; set; } = "";
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<string> Roles { get; set; } = new();
    public List<string> GroupIds { get; set; } = new();
    public bool NotificationsMuted { get; set; }
    public bool NotificationsPreferenceSet { get; set; }
}

public class BoardNotificationDoc
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string GroupId { get; set; } = "";
    public string ListId { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string? UpdatedBy { get; set; }
    public string? UpdatedByName { get; set; }
    public string ChangeAction { get; set; } = "";
    public int NewHymnCount { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public bool Read { get; set; }
    public bool SuppressPush { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class BoardListUnreadInfo
{
    public int NewHymnCount { get; set; }
    public DateTime OldestCreatedAtUtc { get; set; }
}

public class WorshipGroupDoc
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string JoinCode { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public class GroupMemberDoc
{
    public string Id { get; set; } = "";
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Nickname { get; set; } = "";
    public List<string> Roles { get; set; } = new();
    public DateTimeOffset JoinedAt { get; set; }
    /// <summary>Group admin — distinct from ministry roles in <see cref="Roles"/>.</summary>
    public bool IsAdmin { get; set; }
}

public class BoardListDoc
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public List<BoardHymnDoc> Hymns { get; set; } = new();
}

public class BoardHymnDoc
{
    public string Id { get; set; } = "";
    public bool IsSection { get; set; }
    public string? SectionName { get; set; }
    public string? HymnNumber { get; set; }
    public long SortOrder { get; set; }
    public string? Notes { get; set; }
    public string? AddedBy { get; set; }
    public string? AddedByName { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class UserSettingsCloudDoc
{
    public DateTimeOffset UpdatedAt { get; set; }
    public string LastHymnNumber { get; set; } = "";
    public string ActiveReadTheme { get; set; } = "#FFFFFF";
    public int ActiveAlignment { get; set; }
    public double ActiveFontSize { get; set; } = 20;
    public string ActiveFont { get; set; } = "SFPro";
    public double ActiveLetterSpacing { get; set; }
    public double ActiveLineSpacing { get; set; } = 1;
    public bool DarkMode { get; set; }
    public bool KeepAwake { get; set; }
    /// <summary>0 = Grid, 1 = Numpad, 2 = Voice (mirrors MAUI InputType).</summary>
    public int HymnInputType { get; set; } = 1;
    public int AgentMode { get; set; }
    public int AgentChatLimit { get; set; } = 10;
    public List<ShortHymnCloudDoc> History { get; set; } = new();
    public List<ShortHymnCloudDoc> Bookmarks { get; set; } = new();
    public List<string> Searches { get; set; } = new();
}

public class ShortHymnCloudDoc
{
    public string Number { get; set; } = "";
    public string Line { get; set; } = "";
    public DateTimeOffset TimeStamp { get; set; }
    public string BookmarkGroup { get; set; } = "General";
}
