using System;
using System.Threading.Tasks;
using System.Windows.Input;
using MobiHymn4.Services;
using MobiHymn4.Utils;
using MvvmHelpers;
using Microsoft.Maui.Controls;

namespace MobiHymn4.ViewModels;

public class VerifyEmailViewModel : BaseViewModel
{
    readonly IAuthService auth;

    string statusMessage = string.Empty;

    public VerifyEmailViewModel()
    {
        auth = ServiceHelper.Get<IAuthService>();
        Title = "Verify Email";
        ResendCommand = new Command(async () => await ResendAsync(), () => !IsBusy);
        RefreshCommand = new Command(async () => await RefreshAsync(), () => !IsBusy);
    }

    public string Email => auth.CurrentEmail;

    public string StatusMessage
    {
        get => statusMessage;
        set
        {
            if (SetProperty(ref statusMessage, value))
                OnPropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public ICommand ResendCommand { get; }
    public ICommand RefreshCommand { get; }

    void RefreshCommands()
    {
        (ResendCommand as Command)?.ChangeCanExecute();
        (RefreshCommand as Command)?.ChangeCanExecute();
    }

    async Task ResendAsync()
    {
        try
        {
            IsBusy = true;
            RefreshCommands();
            StatusMessage = string.Empty;
            await auth.SendEmailVerificationAsync();
            StatusMessage = "Verification email sent. Check your inbox.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            RefreshCommands();
        }
    }

    async Task RefreshAsync()
    {
        try
        {
            IsBusy = true;
            RefreshCommands();
            StatusMessage = string.Empty;
            await auth.RefreshEmailVerificationStatusAsync();

            if (auth.IsEmailVerified)
            {
                await AuthNavigationHelper.NavigateForAuthStateAsync();
                return;
            }

            StatusMessage = "Email not verified yet. Open the link in your inbox, then tap Refresh.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            RefreshCommands();
        }
    }
}
