using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace MobiHymn4.Utils;

/// <summary>
/// Tracks top-level Shell flyout navigations so Android back can return to the previous screen.
/// Absolute <c>//route</c> switches do not create a platform back stack on their own.
/// </summary>
public sealed class ShellNavigationHistory
{
    public static ShellNavigationHistory Instance { get; } = new();

    readonly List<string> stack = new();
    string current;
    bool suppressRecord;
    const int MaxDepth = 32;

    public void Record(string location)
    {
        var normalized = Normalize(location);
        if (string.IsNullOrEmpty(normalized))
            return;

        if (suppressRecord)
        {
            suppressRecord = false;
            current = normalized;
            return;
        }

        if (string.Equals(current, normalized, StringComparison.OrdinalIgnoreCase))
            return;

        if (!string.IsNullOrEmpty(current))
        {
            stack.Add(current);
            if (stack.Count > MaxDepth)
                stack.RemoveAt(0);
        }

        current = normalized;
    }

    public bool CanGoBack => stack.Count > 0;

    public bool TryGoBack()
    {
        if (stack.Count == 0 || Shell.Current == null)
            return false;

        var previous = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        suppressRecord = true;
        current = previous;
        _ = GoAsync(previous);
        return true;
    }

    public void Clear()
    {
        stack.Clear();
        current = null;
        suppressRecord = false;
    }

    static async Task GoAsync(string location)
    {
        try
        {
            await Shell.Current.GoToAsync(location);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ShellNavigationHistory.GoBack failed: {ex.Message}");
            Instance.suppressRecord = false;
        }
    }

    public static string Normalize(string location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return null;

        var value = location.Trim();
        var queryIndex = value.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex >= 0)
            value = value[..queryIndex];

        value = value.TrimEnd('/');
        if (!value.StartsWith("//", StringComparison.Ordinal))
        {
            value = value.StartsWith("/", StringComparison.Ordinal)
                ? "/" + value
                : "//" + value;
        }

        return value.ToLowerInvariant();
    }
}
