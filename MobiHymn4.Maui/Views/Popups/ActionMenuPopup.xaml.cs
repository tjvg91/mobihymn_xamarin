using System;
using System.Collections.Generic;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MobiHymn4.Views.Popups;

public partial class ActionMenuPopup : Popup
{
    public ActionMenuPopup(string title, IReadOnlyList<string> options, string cancelLabel = "Cancel")
    {
        InitializeComponent();
        lblTitle.Text = title;
        btnCancel.Text = cancelLabel;

        foreach (var option in options)
            optionsStack.Children.Add(CreateOptionRow(option));

        Opened += ActionMenuPopup_Opened;
    }

    void ActionMenuPopup_Opened(object sender, System.EventArgs e)
    {
        var display = DeviceDisplay.MainDisplayInfo;
        Size = new Size(display.Width / display.Density, display.Height / display.Density);
    }

    View CreateOptionRow(string option)
    {
        var isDestructive = string.Equals(option, "Leave group", StringComparison.OrdinalIgnoreCase);
        var label = new Label
        {
            Text = option,
            FontSize = 15,
            Padding = new Thickness(0, 12),
            TextColor = isDestructive
                ? (Color)Application.Current.Resources["Red"]
                : Application.Current?.RequestedTheme == AppTheme.Dark
                    ? (Color)Application.Current.Resources["GrayLight"]
                    : (Color)Application.Current.Resources["Gray"],
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => Close(option);
        label.GestureRecognizers.Add(tap);
        return label;
    }

    void Overlay_Tapped(object sender, TappedEventArgs e) => Close(null);

    void Cancel_Clicked(object sender, System.EventArgs e) => Close(null);

    public static async System.Threading.Tasks.Task<string> PickAsync(
        Page host,
        string title,
        IReadOnlyList<string> options,
        string cancelLabel = "Cancel")
    {
        if (host == null)
            return null;

        var popup = new ActionMenuPopup(title, options, cancelLabel);
        var result = await host.ShowPopupAsync(popup);
        return result as string;
    }
}
