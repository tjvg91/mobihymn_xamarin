using System;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;

namespace MobiHymn4.Views;

[XamlCompilation(XamlCompilationOptions.Compile)]
public partial class GroupManagePage : ContentPage
{
    public GroupManagePage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ViewModels.GroupManageViewModel vm)
            vm.RefreshCommand.Execute(null);
    }

    async void NotificationsSwitch_Toggled(object sender, ToggledEventArgs e)
    {
        if (BindingContext is not ViewModels.GroupManageViewModel vm)
            return;

        await vm.OnNotificationsEnabledToggledAsync(e.Value);
    }
}
