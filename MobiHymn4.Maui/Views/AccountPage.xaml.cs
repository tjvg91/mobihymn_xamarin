using System;
using FontAwesome;
using MobiHymn4.Utils;
using MobiHymn4.ViewModels;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MobiHymn4.Views;

public partial class AccountPage : ContentPage
{
    bool windowResumeHooked;

    public AccountPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Shell.SetFlyoutBehavior(this, FlyoutBehavior.Disabled);
        UpdateBackIcon();
        HookWindowResumed();

        if (BindingContext is AccountViewModel vm)
            _ = vm.RefreshOnAppearAsync();
    }

    protected override void OnDisappearing()
    {
        UnhookWindowResumed();
        Shell.SetFlyoutBehavior(this, FlyoutBehavior.Flyout);
        base.OnDisappearing();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler != null)
            HookWindowResumed();
        else
            UnhookWindowResumed();
    }

    void HookWindowResumed()
    {
        if (windowResumeHooked || Window == null)
            return;

        Window.Resumed += Window_Resumed;
        windowResumeHooked = true;
    }

    void UnhookWindowResumed()
    {
        if (!windowResumeHooked || Window == null)
            return;

        Window.Resumed -= Window_Resumed;
        windowResumeHooked = false;
    }

    async void Window_Resumed(object sender, EventArgs e)
    {
        if (BindingContext is AccountViewModel vm)
            await vm.RefreshOnAppearAsync();
    }

    void UpdateBackIcon()
    {
        if (btnBack == null)
            return;

        btnBack.Source = new FontImageSource
        {
            FontFamily = "FAS",
            Glyph = FontAwesomeIcons.ArrowLeft,
            Size = 20,
            Color = (Color)Application.Current.Resources["PrimaryText"],
        };
    }

    async void btnBack_Clicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync($"//{Routes.READ}");
    }

    async void NotificationsSwitch_Toggled(object sender, ToggledEventArgs e)
    {
        if (BindingContext is AccountViewModel vm)
            await vm.OnNotificationsEnabledToggledAsync(e.Value);
    }
}
