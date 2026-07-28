using System;
using CommunityToolkit.Maui.Views;
using FontAwesome;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace MobiHymn4.Views.Popups;

public partial class ChangePasswordPopup : Popup
{
    public ChangePasswordPopup()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            var display = DeviceDisplay.MainDisplayInfo;
            Size = new Size(display.Width / display.Density, display.Height / display.Density);
        };
    }

    void Overlay_Tapped(object sender, TappedEventArgs e) => Close(null);

    void Cancel_Clicked(object sender, EventArgs e) => Close(null);

    void ToggleNewPassword_Clicked(object sender, EventArgs e) =>
        TogglePasswordVisibility(entryNewPassword, newPasswordVisibilityIcon);

    void ToggleConfirmPassword_Clicked(object sender, EventArgs e) =>
        TogglePasswordVisibility(entryConfirmPassword, confirmPasswordVisibilityIcon);

    static void TogglePasswordVisibility(Entry entry, FontImageSource icon)
    {
        if (entry == null || icon == null)
            return;

        entry.IsPassword = !entry.IsPassword;
        icon.Glyph = entry.IsPassword ? FontAwesomeIcons.Eye : FontAwesomeIcons.EyeSlash;
    }

    void Save_Clicked(object sender, EventArgs e)
    {
        var password = entryNewPassword.Text?.Trim() ?? string.Empty;
        var confirm = entryConfirmPassword.Text?.Trim() ?? string.Empty;

        if (password.Length < 6)
        {
            ShowError("Password must be at least 6 characters.");
            return;
        }

        if (!string.Equals(password, confirm, StringComparison.Ordinal))
        {
            ShowError("Passwords do not match.");
            return;
        }

        Close(password);
    }

    void ShowError(string message)
    {
        lblError.Text = message;
        lblError.IsVisible = true;
    }

    public static async System.Threading.Tasks.Task<string> PromptAsync(Page host)
    {
        if (host == null)
            return null;

        var popup = new ChangePasswordPopup();
        var result = await host.ShowPopupAsync(popup);
        return result as string;
    }
}
