using System;
using System.Threading.Tasks;
using System.Windows.Input;
using MobiHymn4.Services;
using MobiHymn4.Utils;
using MvvmHelpers;
using Microsoft.Maui.Controls;

namespace MobiHymn4.ViewModels;

public class AuthViewModel : BaseViewModel
{
    readonly IAuthService auth;

    string email = string.Empty;
    string password = string.Empty;
    string confirmPassword = string.Empty;
    string statusMessage = string.Empty;
    bool isSignUpMode;
    bool statusIsError = true;

    public AuthViewModel()
    {
        auth = ServiceHelper.Get<IAuthService>();
        SignInCommand = new Command(async () => await SignInAsync(), () => CanSubmit);
        SignUpCommand = new Command(async () => await SignUpAsync(), () => CanSubmit);
        ForgotPasswordCommand = new Command(async () => await ForgotPasswordAsync(), () => CanForgotPassword);
        ToggleModeCommand = new Command(ToggleMode);
    }

    public string Email
    {
        get => email;
        set
        {
            if (SetProperty(ref email, value))
                RefreshCommands();
        }
    }

    public string Password
    {
        get => password;
        set
        {
            if (SetProperty(ref password, value))
                RefreshCommands();
        }
    }

    public string ConfirmPassword
    {
        get => confirmPassword;
        set
        {
            if (SetProperty(ref confirmPassword, value))
                RefreshCommands();
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        set
        {
            if (SetProperty(ref statusMessage, value))
                OnPropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool StatusIsError
    {
        get => statusIsError;
        set => SetProperty(ref statusIsError, value);
    }

    public bool IsSignUpMode
    {
        get => isSignUpMode;
        set
        {
            if (SetProperty(ref isSignUpMode, value))
            {
                ConfirmPassword = string.Empty;
                StatusMessage = string.Empty;
                OnPropertyChanged(nameof(ModeTitle));
                OnPropertyChanged(nameof(ModeSubtitle));
                OnPropertyChanged(nameof(PrimaryActionText));
                OnPropertyChanged(nameof(ShowSignInButton));
                OnPropertyChanged(nameof(ShowSignUpButton));
                OnPropertyChanged(nameof(ShowConfirmPassword));
                OnPropertyChanged(nameof(ShowForgotPassword));
                OnPropertyChanged(nameof(ToggleLinkText));
                RefreshCommands();
            }
        }
    }

    public string ModeTitle => IsSignUpMode ? "Create Account" : "Welcome Back";
    public string ModeSubtitle => IsSignUpMode
        ? "Sign up with email to join your worship community."
        : "Sign in to access groups, boards, and your profile.";
    public string PrimaryActionText => IsSignUpMode ? "Sign Up" : "Sign In";
    public bool ShowSignInButton => !IsSignUpMode;
    public bool ShowSignUpButton => IsSignUpMode;
    public bool ShowConfirmPassword => IsSignUpMode;
    public bool ShowForgotPassword => !IsSignUpMode;
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
    public string ToggleLinkText => IsSignUpMode
        ? "Already have an account? Sign in"
        : "Need an account? Sign up";

    public ICommand SignInCommand { get; }
    public ICommand SignUpCommand { get; }
    public ICommand ForgotPasswordCommand { get; }
    public ICommand ToggleModeCommand { get; }

    bool CanSubmit => !IsBusy
        && !string.IsNullOrWhiteSpace(Email)
        && !string.IsNullOrWhiteSpace(Password)
        && (!IsSignUpMode || !string.IsNullOrWhiteSpace(ConfirmPassword));

    bool CanForgotPassword => !IsBusy && !IsSignUpMode && !string.IsNullOrWhiteSpace(Email);

    void ToggleMode()
    {
        IsSignUpMode = !IsSignUpMode;
        StatusMessage = string.Empty;
    }

    void RefreshCommands()
    {
        (SignInCommand as Command)?.ChangeCanExecute();
        (SignUpCommand as Command)?.ChangeCanExecute();
        (ForgotPasswordCommand as Command)?.ChangeCanExecute();
    }

    void SetStatus(string message, bool isError)
    {
        StatusIsError = isError;
        StatusMessage = message;
    }

    async Task SignInAsync()
    {
        if (!CanSubmit)
            return;

        try
        {
            IsBusy = true;
            RefreshCommands();
            StatusMessage = string.Empty;
            await auth.SignInWithEmailAsync(Email.Trim(), Password);
            await AuthNavigationHelper.NavigateForAuthStateAsync();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
        finally
        {
            IsBusy = false;
            RefreshCommands();
        }
    }

    async Task SignUpAsync()
    {
        if (!CanSubmit)
            return;

        if (Password != ConfirmPassword)
        {
            SetStatus("Passwords do not match.", isError: true);
            return;
        }

        try
        {
            IsBusy = true;
            RefreshCommands();
            StatusMessage = string.Empty;
            await auth.SignUpWithEmailAsync(Email.Trim(), Password);
            await AuthNavigationHelper.NavigateForAuthStateAsync();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
        finally
        {
            IsBusy = false;
            RefreshCommands();
        }
    }

    async Task ForgotPasswordAsync()
    {
        if (!CanForgotPassword)
            return;

        try
        {
            IsBusy = true;
            RefreshCommands();
            StatusMessage = string.Empty;
            await auth.SendPasswordResetEmailAsync(Email.Trim());
            SetStatus(
                "Password reset email sent. Check your inbox for a link to choose a new password.",
                isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(FriendlyAuthMessage(ex.Message), isError: true);
        }
        finally
        {
            IsBusy = false;
            RefreshCommands();
        }
    }

    static string FriendlyAuthMessage(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "An error occurred.";

        var lower = raw.ToLowerInvariant();
        if (lower.Contains("user-not-found") || lower.Contains("no user"))
            return "No account found for that email.";
        if (lower.Contains("invalid-email"))
            return "Enter a valid email address.";
        if (lower.Contains("network"))
            return "Network error. Check your connection.";

        return raw;
    }
}
