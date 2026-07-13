using FontAwesome;
using MobiHymn4.Utils;
using MobiHymn4.ViewModels;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MobiHymn4.Views;

public partial class AccountPage : ContentPage
{
    public AccountPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Shell.SetFlyoutBehavior(this, FlyoutBehavior.Disabled);
        UpdateBackIcon();

        if (BindingContext is AccountViewModel vm)
            _ = vm.RefreshOnAppearAsync();
    }

    protected override void OnDisappearing()
    {
        Shell.SetFlyoutBehavior(this, FlyoutBehavior.Flyout);
        base.OnDisappearing();
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
