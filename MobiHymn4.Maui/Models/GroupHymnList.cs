using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MobiHymn4.Models;

public class GroupHymnList
{
    public string Id { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public List<BoardHymnEntry> Hymns { get; set; } = new();
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

public class GroupHymnListSummary : INotifyPropertyChanged
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public int HymnCount
    {
        get => hymnCount;
        set
        {
            if (hymnCount == value)
                return;
            hymnCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Subtitle));
        }
    }

    public int EntryCount
    {
        get => entryCount;
        set
        {
            if (entryCount == value)
                return;
            entryCount = value;
            OnPropertyChanged();
        }
    }

    int hymnCount;
    int entryCount;

    public void SetCounts(int hymns, int entries)
    {
        HymnCount = hymns;
        EntryCount = entries;
    }

    public string Subtitle => $"{HymnCount} {(HymnCount == 1 ? "hymn" : "hymns")}";
    public bool CanManage
    {
        get => canManage;
        set
        {
            if (canManage == value)
                return;
            canManage = value;
            OnPropertyChanged();
        }
    }

    bool canManage;

    int unreadCount;
    /// <summary>New hymn updates since last visit (from unread notifications).</summary>
    public int UnreadCount
    {
        get => unreadCount;
        set
        {
            if (unreadCount == value)
                return;
            unreadCount = value < 0 ? 0 : value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasUnread));
            OnPropertyChanged(nameof(UnreadBadgeText));
        }
    }

    public bool HasUnread => unreadCount > 0;

    public string UnreadBadgeText =>
        unreadCount > 99 ? "99+" : unreadCount.ToString();

    public event PropertyChangedEventHandler PropertyChanged;

    void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class BoardListsPage
{
    public const int DefaultPageSize = 30;

    public static BoardListsPage Empty { get; } = new();

    public IReadOnlyList<GroupHymnListSummary> Items { get; init; } = Array.Empty<GroupHymnListSummary>();
    public bool HasMore { get; init; }
}
