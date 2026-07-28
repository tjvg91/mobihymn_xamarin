using System;
using FontAwesome;
using MobiHymn4.Utils;
using MobiHymn4.ViewModels;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MobiHymn4.Views;

[QueryProperty(nameof(AuthMode), "mode")]
public partial class LoginPage : ContentPage
{
    string authMode;

    public string AuthMode
    {
        get => authMode;
        set
        {
            authMode = value;
            ApplyAuthModeFromNavigation();
        }
    }

    public LoginPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyAuthModeFromNavigation();
        SyncViewportHeight();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        SyncViewportHeight();
    }

    void RootLayout_SizeChanged(object sender, EventArgs e) => SyncViewportHeight();

    void SyncViewportHeight()
    {
        // ScrollView content must be at least the viewport height so VerticalOptions=Center
        // places the form in the middle of the screen (not the top).
        if (viewportHost == null)
            return;

        var target = 0.0;
        if (loginScroll?.Height > 0)
            target = loginScroll.Height;
        else if (rootLayout?.Height > 0)
            target = rootLayout.Height;
        else if (Height > 0)
            target = Height;

        if (target <= 0)
            return;

        if (Math.Abs(viewportHost.HeightRequest - target) > 0.5)
            viewportHost.HeightRequest = target;
    }

    public void ApplyAuthModeFromNavigation()
    {
        if (BindingContext is not AuthViewModel vm)
            return;

        var signUp = AuthNavigationHelper.ConsumePendingSignUpMode()
            || string.Equals(authMode, "signup", StringComparison.OrdinalIgnoreCase);

        vm.IsSignUpMode = signUp;
        if (!signUp)
            authMode = null;
    }

    void TogglePassword_Clicked(object sender, EventArgs e) =>
        TogglePasswordVisibility(entryPassword, passwordVisibilityIcon);

    void ToggleConfirmPassword_Clicked(object sender, EventArgs e) =>
        TogglePasswordVisibility(entryConfirmPassword, confirmPasswordVisibilityIcon);

    static void TogglePasswordVisibility(Entry entry, FontImageSource icon)
    {
        if (entry == null || icon == null)
            return;

        entry.IsPassword = !entry.IsPassword;
        icon.Glyph = entry.IsPassword ? FontAwesomeIcons.Eye : FontAwesomeIcons.EyeSlash;
    }

    void Entry_Focused(object sender, FocusEventArgs e)
    {
        if (sender is Entry entry && FindAncestorBorder(entry) is Border border)
            border.Stroke = new SolidColorBrush(Color.FromArgb("#F5D200"));
    }

    void Entry_Unfocused(object sender, FocusEventArgs e)
    {
        if (sender is Entry entry && FindAncestorBorder(entry) is Border border)
        {
            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            border.Stroke = new SolidColorBrush(Color.FromArgb(isDark ? "#888888" : "#d0d0d0"));
        }
    }

    static Border FindAncestorBorder(Element element)
    {
        while (element != null)
        {
            if (element is Border border)
                return border;
            element = element.Parent;
        }

        return null;
    }

    async void Close_Clicked(object sender, EventArgs e)
    {
        // When presented as a modal, this page is on Shell.Current.Navigation.ModalStack.
        // Checking Navigation.ModalStack on the modal page itself is always empty.
        var shellNav = Shell.Current?.Navigation;
        if (shellNav?.ModalStack.LastOrDefault() == this)
        {
            await shellNav.PopModalAsync();
            return;
        }

        if (Shell.Current == null)
            return;

        try
        {
            await Shell.Current.GoToAsync("..");
        }
        catch
        {
            await Shell.Current.GoToAsync($"//{Routes.READ}");
        }
    }
}
