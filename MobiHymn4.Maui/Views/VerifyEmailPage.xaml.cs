using System;
using System.Threading.Tasks;
using MobiHymn4.ViewModels;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;

namespace MobiHymn4.Views;

[XamlCompilation(XamlCompilationOptions.Compile)]
public partial class VerifyEmailPage : ContentPage
{
    bool windowResumeHooked;

    public VerifyEmailPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        HookWindowResumed();
        // Also refresh when navigating back to this page.
        _ = RefreshVerificationQuietAsync();
    }

    protected override void OnDisappearing()
    {
        UnhookWindowResumed();
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

    // Fires when the user comes back to the app from the background
    // (e.g. after tapping the verification link in the browser).
    async void Window_Resumed(object sender, EventArgs e) =>
        await RefreshVerificationQuietAsync(delayMs: 800);

    public Task RefreshOnAppResumeAsync() =>
        RefreshVerificationQuietAsync(delayMs: 800);

    async Task RefreshVerificationQuietAsync(int delayMs = 0)
    {
        if (delayMs > 0)
            await Task.Delay(delayMs);

        if (BindingContext is not VerifyEmailViewModel vm || vm.IsBusy)
            return;

        await vm.RefreshAsync(fromResume: true);
    }
}
