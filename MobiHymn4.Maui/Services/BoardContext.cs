using System;
using Microsoft.Maui.Storage;
using MobiHymn4.Utils;

namespace MobiHymn4.Services;

public sealed class BoardContext
{
    public string ActiveGroupId
    {
        get => Preferences.Get(PreferencesVar.ACTIVE_GROUP_ID, string.Empty);
        set
        {
            var normalized = value ?? string.Empty;
            var current = Preferences.Get(PreferencesVar.ACTIVE_GROUP_ID, string.Empty);
            if (string.Equals(current, normalized, StringComparison.Ordinal))
                return;

            Preferences.Set(PreferencesVar.ACTIVE_GROUP_ID, normalized);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public string ActiveListId
    {
        get => Preferences.Get(PreferencesVar.ACTIVE_LIST_ID, string.Empty);
        set
        {
            var normalized = value ?? string.Empty;
            var current = ActiveListId;
            if (string.Equals(current, normalized, StringComparison.Ordinal))
                return;

            Preferences.Set(PreferencesVar.ACTIVE_LIST_ID, normalized);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler Changed;
}
