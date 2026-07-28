using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using FontAwesome;
using MobiHymn4.Models;
using MobiHymn4.Services;
using MobiHymn4.Utils;
using MobiHymn4.Views.Popups;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using MvvmHelpers;

namespace MobiHymn4.ViewModels;

public class AccountViewModel : BaseViewModel
{
    readonly IAuthService auth;
    readonly IProfileService profileService;

    string statusMessage = string.Empty;
    bool notificationsEnabled;
    bool isEmailVerified;

    public AccountViewModel()
    {
        auth = ServiceHelper.Get<IAuthService>();
        profileService = ServiceHelper.Get<IProfileService>();
        Title = "Account";

        profileService.ProfileChanged += (_, _) => LoadFromProfile();
        auth.AuthStateChanged += (_, _) => LoadFromProfile();

        OpenGroupsCommand = new Command(async () => await Shell.Current.GoToAsync(Routes.GROUPS));
        EditProfileCommand = new Command(async () => await Shell.Current.GoToAsync(Routes.PROFILE_SETUP));
        PasswordOptionsCommand = new Command(async () => await ShowPasswordOptionsAsync(), () => !IsBusy && CanManagePassword);
        ResendVerificationCommand = new Command(async () => await ResendVerificationAsync(), () => !IsBusy);
        RefreshVerificationCommand = new Command(async () => await RefreshVerificationAsync(), () => !IsBusy);

        LoadFromProfile();
    }

    public string DisplayName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string RolesSummary { get; private set; } = string.Empty;
    public IList<UserRole> Roles { get; private set; } = new List<UserRole>();

    public bool CanManagePassword => auth.IsSignedIn && !auth.SignedInWithGoogle;

    public bool IsEmailVerified
    {
        get => isEmailVerified;
        private set
        {
            if (SetProperty(ref isEmailVerified, value))
            {
                OnPropertyChanged(nameof(VerificationStatusText));
                OnPropertyChanged(nameof(VerificationBadgeText));
                OnPropertyChanged(nameof(VerificationIconGlyph));
                OnPropertyChanged(nameof(VerificationColor));
                OnPropertyChanged(nameof(ShowVerificationActions));
            }
        }
    }

    public string VerificationStatusText =>
        IsEmailVerified
            ? "Your email address is verified."
            : "Verify your email to unlock community features.";

    public string VerificationBadgeText => IsEmailVerified ? "Verified" : "Unverified";

    public string VerificationIconGlyph =>
        IsEmailVerified ? FontAwesomeIcons.CircleCheck : FontAwesomeIcons.TriangleExclamation;

    public Color VerificationColor =>
        IsEmailVerified ? Color.FromArgb("#2E7D32") : Color.FromArgb("#F59E0B");

    public bool ShowVerificationActions => !IsEmailVerified;

    public bool NotificationsEnabled
    {
        get => notificationsEnabled;
        set
        {
            if (SetProperty(ref notificationsEnabled, value))
            {
                OnPropertyChanged(nameof(NotificationsToggleTitle));
                OnPropertyChanged(nameof(NotificationsToggleSubtitle));
            }
        }
    }

    public string NotificationsToggleTitle =>
        NotificationsEnabled ? "Board notifications" : "Board notifications muted";

    public string NotificationsToggleSubtitle =>
        NotificationsEnabled
            ? "You will get alerts when your group’s hymn list changes. Turn off to mute."
            : "Notifications are off. Turn on to hear when your group’s hymn list changes.";

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

    public ICommand OpenGroupsCommand { get; }
    public ICommand EditProfileCommand { get; }
    public ICommand PasswordOptionsCommand { get; }
    public ICommand ResendVerificationCommand { get; }
    public ICommand RefreshVerificationCommand { get; }

    public async Task RefreshOnAppearAsync()
    {
        try
        {
            await auth.RefreshEmailVerificationStatusAsync();
        }
        catch
        {
        }

        LoadFromProfile();
    }

    public async Task OnNotificationsEnabledToggledAsync(bool enabled)
    {
        var muted = !enabled;
        if (profileService.CurrentProfile?.NotificationsMuted == muted
            && profileService.CurrentProfile.NotificationsPreferenceSet)
            return;

        try
        {
            IsBusy = true;
            NotificationsEnabled = enabled;
            await profileService.SetNotificationsMutedAsync(muted);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            NotificationsEnabled = !(profileService.CurrentProfile?.NotificationsMuted ?? true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    async Task ShowPasswordOptionsAsync()
    {
        var page = Shell.Current?.CurrentPage;
        if (page == null || !CanManagePassword)
            return;

        await ChangePasswordAsync(page);
    }

    async Task ChangePasswordAsync(Page page)
    {
        var newPassword = await ChangePasswordPopup.PromptAsync(page);
        if (string.IsNullOrEmpty(newPassword))
            return;

        try
        {
            IsBusy = true;
            StatusMessage = string.Empty;
            await auth.UpdatePasswordAsync(newPassword);
            StatusMessage = "Password updated.";
        }
        catch (Exception ex)
        {
            var message = ex.Message ?? string.Empty;
            if (message.Contains("requires-recent-login", StringComparison.OrdinalIgnoreCase)
                || message.Contains("recent", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "For security, sign out and use Forgot password on the sign-in screen.";
            }
            else
            {
                StatusMessage = message;
            }
        }
        finally
        {
            IsBusy = false;
            (PasswordOptionsCommand as Command)?.ChangeCanExecute();
        }
    }

    async Task ResendVerificationAsync()
    {
        try
        {
            IsBusy = true;
            RefreshVerificationCommands();
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
            RefreshVerificationCommands();
        }
    }

    async Task RefreshVerificationAsync()
    {
        try
        {
            IsBusy = true;
            RefreshVerificationCommands();
            StatusMessage = string.Empty;
            await auth.RefreshEmailVerificationStatusAsync();
            LoadFromProfile();

            StatusMessage = IsEmailVerified
                ? "Email verified."
                : "Email not verified yet. Open the link in your inbox, then refresh again.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            RefreshVerificationCommands();
        }
    }

    void LoadFromProfile()
    {
        var profile = profileService.CurrentProfile;
        DisplayName = profile?.DisplayName ?? auth.CurrentEmail;
        Email = profile?.Email ?? auth.CurrentEmail;
        Roles = profile?.Roles?.ToList() ?? new List<UserRole>();
        RolesSummary = Roles.Count == 0
            ? "No roles selected"
            : string.Join(", ", Roles.Select(r => r.ToDisplayName()));

        var muted = profile == null
            ? true
            : profile.NotificationsPreferenceSet
                ? profile.NotificationsMuted
                : RolePermissions.GetDefaultNotificationsMuted(profile.Roles);
        NotificationsEnabled = !muted;
        IsEmailVerified = auth.IsEmailVerified;

        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Email));
        OnPropertyChanged(nameof(Roles));
        OnPropertyChanged(nameof(RolesSummary));
        OnPropertyChanged(nameof(CanManagePassword));
        OnPropertyChanged(nameof(NotificationsToggleTitle));
        OnPropertyChanged(nameof(NotificationsToggleSubtitle));
        (PasswordOptionsCommand as Command)?.ChangeCanExecute();
    }

    void RefreshVerificationCommands()
    {
        (ResendVerificationCommand as Command)?.ChangeCanExecute();
        (RefreshVerificationCommand as Command)?.ChangeCanExecute();
        (PasswordOptionsCommand as Command)?.ChangeCanExecute();
    }
}
