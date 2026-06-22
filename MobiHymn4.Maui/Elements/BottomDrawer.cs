using Microsoft.Maui.Controls;

namespace MobiHymn4.Elements;

/// <summary>
/// Replaces Xam.Plugin.SimpleBottomDrawer for bookmark group selection panels.
/// </summary>
public class BottomDrawer : ContentView
{
    public static readonly BindableProperty ExpandedPercentageProperty =
        BindableProperty.Create(nameof(ExpandedPercentage), typeof(double), typeof(BottomDrawer), 90.0);

    public static readonly BindableProperty IsExpandedProperty =
        BindableProperty.Create(nameof(IsExpanded), typeof(bool), typeof(BottomDrawer), true);

    public static readonly BindableProperty LockStatesProperty =
        BindableProperty.Create(nameof(LockStates), typeof(object), typeof(BottomDrawer), null);

    public double ExpandedPercentage
    {
        get => (double)GetValue(ExpandedPercentageProperty);
        set => SetValue(ExpandedPercentageProperty, value);
    }

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public object LockStates
    {
        get => GetValue(LockStatesProperty);
        set => SetValue(LockStatesProperty, value);
    }
}
