using System.Text.Json;
using MobiHymn4.Shared;
using MobiHymn4.Shared.Services;

namespace MobiHymn4.Web.Services;

/// <summary>Web port of MAUI BoardNavigationContext — prev/next through a setlist.</summary>
public sealed class BoardNavigationContext
{
    sealed class PersistedState
    {
        public string? GroupId { get; set; }
        public string? ListId { get; set; }
        public string[]? HymnNumbers { get; set; }
        public int CurrentIndex { get; set; }
    }

    readonly IAppPreferences prefs;

    public BoardNavigationContext(IAppPreferences prefs)
    {
        this.prefs = prefs;
        RestoreFromPreferences();
    }

    public string? GroupId { get; private set; }
    public string? ListId { get; private set; }
    public IReadOnlyList<string> OrderedHymnNumbers { get; private set; } = Array.Empty<string>();
    public int CurrentIndex { get; private set; }

    public bool IsActive =>
        !string.IsNullOrWhiteSpace(GroupId) && OrderedHymnNumbers.Count > 0;

    public bool CanGoPrev => IsActive && CurrentIndex > 0;
    public bool CanGoNext => IsActive && CurrentIndex < OrderedHymnNumbers.Count - 1;

    public event EventHandler? Changed;

    public void Set(string groupId, string? listId, IList<string> hymnNumbers, int currentIndex)
    {
        GroupId = groupId;
        ListId = listId ?? string.Empty;
        OrderedHymnNumbers = hymnNumbers?
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .ToArray()
            ?? Array.Empty<string>();
        CurrentIndex = Math.Clamp(currentIndex, 0, Math.Max(0, OrderedHymnNumbers.Count - 1));
        SaveToPreferences();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetCurrentIndex(int index)
    {
        if (!IsActive)
            return;

        CurrentIndex = Math.Clamp(index, 0, OrderedHymnNumbers.Count - 1);
        SaveToPreferences();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public string? TryGetNumberAt(int index)
    {
        if (!IsActive || index < 0 || index >= OrderedHymnNumbers.Count)
            return null;
        return OrderedHymnNumbers[index];
    }

    /// <summary>
    /// Keep the index aligned with the open hymn. Clears context if the hymn
    /// is no longer part of the setlist (opened from search, etc.).
    /// </summary>
    public void SyncFromHymnNumber(string? hymnNumber)
    {
        if (!IsActive)
            return;

        if (string.IsNullOrWhiteSpace(hymnNumber))
        {
            Clear();
            return;
        }

        var index = -1;
        for (var i = 0; i < OrderedHymnNumbers.Count; i++)
        {
            if (string.Equals(OrderedHymnNumbers[i], hymnNumber, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            Clear();
            return;
        }

        if (index == CurrentIndex)
            return;

        CurrentIndex = index;
        SaveToPreferences();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (!IsActive && GroupId == null && OrderedHymnNumbers.Count == 0)
            return;

        GroupId = null;
        ListId = null;
        OrderedHymnNumbers = Array.Empty<string>();
        CurrentIndex = 0;
        prefs.Remove(PrefKeys.BoardNavState);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    void SaveToPreferences()
    {
        if (!IsActive)
        {
            prefs.Remove(PrefKeys.BoardNavState);
            return;
        }

        var state = new PersistedState
        {
            GroupId = GroupId,
            ListId = ListId ?? string.Empty,
            HymnNumbers = OrderedHymnNumbers.ToArray(),
            CurrentIndex = CurrentIndex
        };
        prefs.Set(PrefKeys.BoardNavState, JsonSerializer.Serialize(state));
    }

    void RestoreFromPreferences()
    {
        var json = prefs.Get(PrefKeys.BoardNavState, "");
        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            var state = JsonSerializer.Deserialize<PersistedState>(json);
            if (state == null
                || string.IsNullOrWhiteSpace(state.GroupId)
                || state.HymnNumbers == null
                || state.HymnNumbers.Length == 0)
                return;

            GroupId = state.GroupId;
            ListId = state.ListId ?? string.Empty;
            OrderedHymnNumbers = state.HymnNumbers;
            CurrentIndex = Math.Clamp(state.CurrentIndex, 0, Math.Max(0, OrderedHymnNumbers.Count - 1));
        }
        catch
        {
            prefs.Remove(PrefKeys.BoardNavState);
        }
    }
}
