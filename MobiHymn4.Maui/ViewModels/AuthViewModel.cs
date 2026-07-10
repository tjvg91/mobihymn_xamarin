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

    public AuthViewModel()
    {
        auth = ServiceHelper.Get<IAuthService>();
        SignInCommand = new Command(async () => await SignInAsync(), () => CanSubmit);
        SignUpCommand = new Command(async () => await SignUpAsync(), () => CanSubmit);
        GoogleSignInCommand = new Command(async () => await GoogleSignInAsync(), () => !IsBusy);
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
                OnPropertyChanged(nameof(ToggleLinkText));
            }
        }
    }

    public string ModeTitle => IsSignUpMode ? "Create Account" : "Welcome Back";
    public string ModeSubtitle => IsSignUpMode
        ? "Sign up with email or Google to join your worship community."
        : "Sign in to access groups, boards, and your profile.";
    public string PrimaryActionText => IsSignUpMode ? "Sign Up" : "Sign In";
    public bool ShowSignInButton => !IsSignUpMode;
    public bool ShowSignUpButton => IsSignUpMode;
    public bool ShowConfirmPassword => IsSignUpMode;
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
    public string ToggleLinkText => IsSignUpMode
        ? "Already have an account? Sign in"
        : "Need an account? Sign up";

    public ICommand SignInCommand { get; }
    public ICommand SignUpCommand { get; }
    public ICommand GoogleSignInCommand { get; }
    public ICommand ToggleModeCommand { get; }

    bool CanSubmit => !IsBusy
        && !string.IsNullOrWhiteSpace(Email)
        && !string.IsNullOrWhiteSpace(Password)
        && (!IsSignUpMode || !string.IsNullOrWhiteSpace(ConfirmPassword));

    void ToggleMode()
    {
        IsSignUpMode = !IsSignUpMode;
        StatusMessage = string.Empty;
    }

    void RefreshCommands()
    {
        (SignInCommand as Command)?.ChangeCanExecute();
        (SignUpCommand as Command)?.ChangeCanExecute();
        (GoogleSignInCommand as Command)?.ChangeCanExecute();
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
            StatusMessage = ex.Message;
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
            StatusMessage = "Passwords do not match.";
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
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            RefreshCommands();
        }
    }

    async Task GoogleSignInAsync()
    {
        try
        {
            IsBusy = true;
            RefreshCommands();
            StatusMessage = string.Empty;
            await auth.SignInWithGoogleAsync();
            await AuthNavigationHelper.NavigateForAuthStateAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = FriendlyMessage(ex.Message);
        }
        finally
        {
            IsBusy = false;
            RefreshCommands();
        }
    }

    // Strip bare numeric status codes that leak from the platform layer.
    static string FriendlyMessage(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "An error occurred.";

        var code = raw.Split(':')[0].Trim();
        if (int.TryParse(code, out _))
        {
            return code switch
            {
                "10"    => "Google Sign-In is not configured for this build.",
                "7"     => "Network error. Check your connection.",
                "12501" => "Sign-in was cancelled.",
                _       => $"Sign-in error (code {code})."
            };
        }

        return raw;
    }
}
