using System;
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

    void Entry_Focused(object sender, FocusEventArgs e)
    {
        if (sender is Entry entry && entry.Parent is Border border)
            border.Stroke = new SolidColorBrush(Color.FromArgb("#F5D200"));
    }

    void Entry_Unfocused(object sender, FocusEventArgs e)
    {
        if (sender is Entry entry && entry.Parent is Border border)
        {
            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            border.Stroke = new SolidColorBrush(Color.FromArgb(isDark ? "#888888" : "#d0d0d0"));
        }
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
