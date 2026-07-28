using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;

namespace MobiHymn4.Models;

public class BoardHymnEntry : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool IsSection { get; set; }

    string sectionName = string.Empty;
    public string SectionName
    {
        get => sectionName;
        set => SetField(ref sectionName, value);
    }

    string hymnNumber = string.Empty;
    public string HymnNumber
    {
        get => hymnNumber;
        set => SetField(ref hymnNumber, value);
    }
    public int SortOrder { get; set; }

    public bool IsHymn => !IsSection;
    public bool HasSectionName => !string.IsNullOrWhiteSpace(SectionName);

    public string ChevronGlyph => IsCollapsed ? "\uf077" : "\uf078";

    int sectionHymnCount;
    public int SectionHymnCount
    {
        get => sectionHymnCount;
        set
        {
            if (SetField(ref sectionHymnCount, value))
                OnPropertyChanged(nameof(SectionHymnCountText));
        }
    }

    bool isCollapsed;
    public bool IsCollapsed
    {
        get => isCollapsed;
        set
        {
            if (SetField(ref isCollapsed, value))
                OnPropertyChanged(nameof(ChevronGlyph));
        }
    }

    bool isRowVisible = true;
    public bool IsRowVisible
    {
        get => isRowVisible;
        set => SetField(ref isRowVisible, value);
    }

    bool showDragHandle;
    public bool ShowDragHandle
    {
        get => showDragHandle;
        set => SetField(ref showDragHandle, value);
    }

    bool isSectionSaved;
    /// <summary>UI-only: whether this section is in the saved template.</summary>
    public bool IsSectionSaved
    {
        get => isSectionSaved;
        set
        {
            if (SetField(ref isSectionSaved, value))
            {
                OnPropertyChanged(nameof(ShowSectionSaveAction));
                OnPropertyChanged(nameof(ShowSectionUnsaveAction));
            }
        }
    }

    bool showSectionSaveAction;
    public bool ShowSectionSaveAction
    {
        get => showSectionSaveAction;
        set => SetField(ref showSectionSaveAction, value);
    }

    bool showSectionUnsaveAction;
    public bool ShowSectionUnsaveAction
    {
        get => showSectionUnsaveAction;
        set => SetField(ref showSectionUnsaveAction, value);
    }

    bool isDragOver;
    public bool IsDragOver
    {
        get => isDragOver;
        set => SetField(ref isDragOver, value);
    }

    bool showDropLineAbove;
    /// <summary>UI-only: insertion line above this row while dragging.</summary>
    public bool ShowDropLineAbove
    {
        get => showDropLineAbove;
        set => SetField(ref showDropLineAbove, value);
    }

    bool showDropLineBelow;
    /// <summary>UI-only: insertion line below this row while dragging.</summary>
    public bool ShowDropLineBelow
    {
        get => showDropLineBelow;
        set => SetField(ref showDropLineBelow, value);
    }

    public string SectionHymnCountText => SectionHymnCount.ToString();
    public string Notes { get; set; } = string.Empty;
    public string AddedBy { get; set; } = string.Empty;
    public string AddedByName { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

    bool showNotes;
    /// <summary>UI-only: whether notes are visible for the current viewer's role.</summary>
    public bool ShowNotes
    {
        get => showNotes;
        set => SetField(ref showNotes, value);
    }

    /// <summary>UI-only: first line of the hymn, resolved from the local hymn list. Not persisted.</summary>
    string firstLine = string.Empty;
    public string FirstLine
    {
        get => firstLine;
        set
        {
            if (SetField(ref firstLine, value ?? string.Empty))
                OnPropertyChanged(nameof(HasFirstLine));
        }
    }

    public bool HasFirstLine => !string.IsNullOrWhiteSpace(FirstLine);

    bool isNew;
    /// <summary>UI-only: hymn was added/updated since the viewer last opened this list.</summary>
    public bool IsNew
    {
        get => isNew;
        set
        {
            if (SetField(ref isNew, value))
                OnPropertyChanged(nameof(ShowNewChip));
        }
    }

    bool isDeleted;
    /// <summary>UI-only: tombstone for a hymn deleted by another member (session).</summary>
    public bool IsDeleted
    {
        get => isDeleted;
        set
        {
            if (SetField(ref isDeleted, value))
            {
                OnPropertyChanged(nameof(ShowNewChip));
                OnPropertyChanged(nameof(ShowDeletedChip));
                OnPropertyChanged(nameof(IsActiveHymn));
                OnPropertyChanged(nameof(ActorLabel));
                OnPropertyChanged(nameof(HymnTitleDecorations));
            }
        }
    }

    public bool IsActiveHymn => IsHymn && !IsDeleted;
    public bool ShowNewChip => IsNew && !IsDeleted;
    public bool ShowDeletedChip => IsDeleted;

    public string ActorLabel => IsDeleted
        ? (string.IsNullOrWhiteSpace(AddedByName) ? "Deleted" : $"Deleted by {AddedByName}")
        : AddedByName ?? string.Empty;

    public TextDecorations HymnTitleDecorations =>
        IsDeleted ? TextDecorations.Strikethrough : TextDecorations.None;

    public string AddedAtText
    {
        get
        {
            var local = UpdatedAt.Kind == DateTimeKind.Utc ? UpdatedAt.ToLocalTime() : UpdatedAt;
            var now = DateTime.Now;
            if (local.Date == now.Date)
                return local.ToString("h:mm tt");
            if (local.Year == now.Year)
                return local.ToString("MMM d");
            return local.ToString("MMM d, yyyy");
        }
    }

    bool isEditing;
    /// <summary>UI-only: whether the inline edit container is expanded. Not persisted.</summary>
    public bool IsEditing
    {
        get => isEditing;
        set
        {
            if (SetField(ref isEditing, value))
                OnPropertyChanged(nameof(IsNotEditing));
        }
    }

    public bool IsNotEditing => !isEditing;

    string editText = string.Empty;
    /// <summary>UI-only: working value for the inline edit input. Not persisted.</summary>
    public string EditText
    {
        get => editText;
        set => SetField(ref editText, value);
    }

    string editNote = string.Empty;
    /// <summary>UI-only: working value for the inline edit note input. Not persisted.</summary>
    public string EditNote
    {
        get => editNote;
        set => SetField(ref editNote, value);
    }

    bool showEditNote;
    /// <summary>UI-only: whether the inline edit note field is revealed. Not persisted.</summary>
    public bool ShowEditNote
    {
        get => showEditNote;
        set => SetField(ref showEditNote, value);
    }

    /// <summary>UI-only: live suggestions for the inline edit input. Not persisted.</summary>
    public ObservableCollection<HymnSuggestion> EditSuggestions { get; } = new();

    bool hasEditSuggestions;
    public bool HasEditSuggestions
    {
        get => hasEditSuggestions;
        set => SetField(ref hasEditSuggestions, value);
    }

    public event PropertyChangedEventHandler PropertyChanged;

    void OnPropertyChanged([CallerMemberName] string name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    bool SetField<T>(ref T field, T value, [CallerMemberName] string name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    public static BoardHymnEntry FromDictionary(IDictionary<string, object> data)
    {
        var entry = new BoardHymnEntry();
        if (data == null)
            return entry;

        if (data.TryGetValue("id", out var id))
            entry.Id = id?.ToString() ?? entry.Id;
        if (data.TryGetValue("isSection", out var isSection))
            entry.IsSection = Convert.ToBoolean(isSection);
        if (data.TryGetValue("sectionName", out var sectionName))
            entry.SectionName = sectionName?.ToString() ?? string.Empty;
        if (data.TryGetValue("hymnNumber", out var number))
            entry.HymnNumber = number?.ToString() ?? string.Empty;
        if (data.TryGetValue("sortOrder", out var sortOrder))
            entry.SortOrder = Convert.ToInt32(sortOrder);
        if (data.TryGetValue("notes", out var notes))
            entry.Notes = notes?.ToString() ?? string.Empty;
        if (data.TryGetValue("addedBy", out var addedBy))
            entry.AddedBy = addedBy?.ToString() ?? string.Empty;
        if (data.TryGetValue("addedByName", out var addedByName))
            entry.AddedByName = addedByName?.ToString() ?? string.Empty;
        if (data.TryGetValue("updatedAt", out var updatedAt) && updatedAt is DateTime dt)
            entry.UpdatedAt = dt;

        return entry;
    }

    public Dictionary<string, object> ToDictionary()
    {
        return new Dictionary<string, object>
        {
            ["id"] = Id,
            ["isSection"] = IsSection,
            ["sectionName"] = SectionName ?? string.Empty,
            ["hymnNumber"] = HymnNumber,
            ["sortOrder"] = SortOrder,
            ["notes"] = Notes ?? string.Empty,
            ["addedBy"] = AddedBy ?? string.Empty,
            ["addedByName"] = AddedByName ?? string.Empty,
            ["updatedAt"] = UpdatedAt,
        };
    }
}
