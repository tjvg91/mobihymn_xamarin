using System;
using System.Threading.Tasks;
using MobiHymn4.ViewModels;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;

namespace MobiHymn4.Views;

[XamlCompilation(XamlCompilationOptions.Compile)]
public partial class VerifyEmailPage : ContentPage
{
    public VerifyEmailPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (Window != null)
            Window.Resumed += Window_Resumed;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (Window != null)
            Window.Resumed -= Window_Resumed;
    }

    // Fires when the user comes back to the app from the background
    // (e.g. after tapping the verification link in the browser).
    async void Window_Resumed(object sender, EventArgs e)
    {
        await Task.Delay(1500); // Let Firebase propagate the verified status
        if (BindingContext is VerifyEmailViewModel vm && !vm.IsBusy)
            vm.RefreshCommand.Execute(null);
    }
}
