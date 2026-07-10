using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Storage;
using MobiHymn4.Models;
using MobiHymn4.Utils;
using Newtonsoft.Json;

namespace MobiHymn4.Services;

public sealed class BoardNavigationContext
{
    sealed class PersistedState
    {
        public string GroupId { get; set; }
        public string ListId { get; set; }
        public string[] HymnNumbers { get; set; }
        public int CurrentIndex { get; set; }
    }

    public string GroupId { get; private set; }
    public string ListId { get; private set; }
    public IReadOnlyList<string> OrderedHymnNumbers { get; private set; } = Array.Empty<string>();
    public int CurrentIndex { get; private set; }

    public bool IsActive => !string.IsNullOrWhiteSpace(GroupId) && OrderedHymnNumbers.Count > 0;

    public event EventHandler Changed;

    public BoardNavigationContext()
    {
        RestoreFromPreferences();
    }

    public void Set(string groupId, string listId, IList<string> hymnNumbers, int currentIndex)
    {
        GroupId = groupId;
        ListId = listId ?? string.Empty;
        OrderedHymnNumbers = hymnNumbers?.ToArray() ?? Array.Empty<string>();
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

    public bool TryApplyCurrentHymnToReader()
    {
        if (!IsActive)
            return false;

        var index = Math.Clamp(CurrentIndex, 0, OrderedHymnNumbers.Count - 1);
        var number = OrderedHymnNumbers[index];
        if (string.IsNullOrWhiteSpace(number))
            return false;

        var list = Globals.Instance.HymnList;
        if (list == null || list.Count == 0)
            return false;

        var hymn = list.FirstOrDefault(h =>
            string.Equals(h?.Number, number, StringComparison.OrdinalIgnoreCase));
        if (hymn == null)
            return false;

        Globals.Instance.ActiveHymn = hymn;
        return true;
    }

    public void SyncCurrentIndexFromHymnNumber(string hymnNumber)
    {
        if (!IsActive || string.IsNullOrWhiteSpace(hymnNumber))
            return;

        var index = -1;
        for (var i = 0; i < OrderedHymnNumbers.Count; i++)
        {
            if (string.Equals(OrderedHymnNumbers[i], hymnNumber, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        if (index < 0 || index == CurrentIndex)
            return;

        CurrentIndex = index;
        SaveToPreferences();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        GroupId = null;
        ListId = null;
        OrderedHymnNumbers = Array.Empty<string>();
        CurrentIndex = 0;
        Preferences.Remove(PreferencesVar.BOARD_NAV_STATE);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static void ClearExternalNavigation() =>
        ServiceHelper.Get<BoardNavigationContext>().Clear();

    void SaveToPreferences()
    {
        if (!IsActive)
        {
            Preferences.Remove(PreferencesVar.BOARD_NAV_STATE);
            return;
        }

        var state = new PersistedState
        {
            GroupId = GroupId,
            ListId = ListId ?? string.Empty,
            HymnNumbers = OrderedHymnNumbers.ToArray(),
            CurrentIndex = CurrentIndex
        };

        Preferences.Set(PreferencesVar.BOARD_NAV_STATE, JsonConvert.SerializeObject(state));
    }

    void RestoreFromPreferences()
    {
        var json = Preferences.Get(PreferencesVar.BOARD_NAV_STATE, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            var state = JsonConvert.DeserializeObject<PersistedState>(json);
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
            Preferences.Remove(PreferencesVar.BOARD_NAV_STATE);
        }
    }
}
