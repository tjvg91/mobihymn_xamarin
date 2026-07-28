using MobiHymn4.Utils;
using MobiHymn4.ViewModels;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;

namespace MobiHymn4.Views;

[XamlCompilation(XamlCompilationOptions.Compile)]
public partial class ProfileSetupPage : ContentPage
{
    public ProfileSetupPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (Shell.Current != null)
            Shell.Current.Navigating += Shell_Navigating;

        if (BindingContext is ProfileSetupViewModel vm)
            _ = vm.LoadOnAppearAsync();
    }

    protected override void OnDisappearing()
    {
        if (Shell.Current != null)
            Shell.Current.Navigating -= Shell_Navigating;
        base.OnDisappearing();
    }

    void Shell_Navigating(object sender, ShellNavigatingEventArgs e)
    {
        var current = Shell.Current?.CurrentState?.Location?.OriginalString ?? string.Empty;
        if (!current.Contains(Routes.PROFILE_SETUP, StringComparison.OrdinalIgnoreCase))
            return;

        var target = e.Target?.Location?.OriginalString ?? string.Empty;
        if (target.Contains(Routes.PROFILE_SETUP, StringComparison.OrdinalIgnoreCase))
            return;

        if (BindingContext is ProfileSetupViewModel vm)
            vm.DiscardUnsavedChanges();
    }

    void RoleChip_Tapped(object sender, TappedEventArgs e)
    {
        if (sender is not BindableObject bindable)
            return;

        if (bindable.BindingContext is RoleChipItem item
            && BindingContext is ProfileSetupViewModel vm
            && !item.IsDisabled)
            vm.ToggleRoleCommand.Execute(item);
    }
}
