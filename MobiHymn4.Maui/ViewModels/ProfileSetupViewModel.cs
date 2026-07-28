using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MobiHymn4.Models;
using MobiHymn4.Services;
using MobiHymn4.Utils;
using MvvmHelpers;
using Microsoft.Maui.Controls;

namespace MobiHymn4.ViewModels;

public class RoleChipItem : ObservableObject
{
    bool isSelected;
    bool isDisabled;

    public UserRole Role { get; set; }
    public string Name { get; set; }

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }

    public bool IsDisabled
    {
        get => isDisabled;
        set => SetProperty(ref isDisabled, value);
    }
}

public class ProfileSetupViewModel : BaseViewModel
{
    readonly IAuthService auth;
    readonly IProfileService profileService;

    string firstName = string.Empty;
    string lastName = string.Empty;
    string nickname = string.Empty;
    string email = string.Empty;
    string statusMessage = string.Empty;
    bool isEditing;
    ObservableCollection<UserRole> selectedRoles = new();

    public ProfileSetupViewModel()
    {
        auth = ServiceHelper.Get<IAuthService>();
        profileService = ServiceHelper.Get<IProfileService>();
        Title = "Your Profile";

        RoleOptions = new ObservableCollection<RoleChipItem>(
            Enum.GetValues(typeof(UserRole)).Cast<UserRole>().Select(role => new RoleChipItem
            {
                Role = role,
                Name = role.ToDisplayName(),
            }));

        ToggleRoleCommand = new Command<RoleChipItem>(ToggleRole);
        SaveCommand = new Command(async () => await SaveAsync(), () => CanSave);
    }

    public ObservableCollection<RoleChipItem> RoleOptions { get; }

    public IList<UserRole> SelectedRoles => selectedRoles;

    public string FirstName
    {
        get => firstName;
        set
        {
            if (SetProperty(ref firstName, value))
                RefreshSaveCommand();
        }
    }

    public string LastName
    {
        get => lastName;
        set
        {
            if (SetProperty(ref lastName, value))
                RefreshSaveCommand();
        }
    }

    public string Nickname
    {
        get => nickname;
        set => SetProperty(ref nickname, value);
    }

    public string Email
    {
        get => email;
        private set => SetProperty(ref email, value);
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

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public ICommand ToggleRoleCommand { get; }
    public ICommand SaveCommand { get; }

    bool CanSave => !IsBusy
        && !string.IsNullOrWhiteSpace(FirstName)
        && !string.IsNullOrWhiteSpace(LastName)
        && selectedRoles.Count > 0;

    void ToggleRole(RoleChipItem item)
    {
        if (item == null || item.IsDisabled)
            return;

        if (item.IsSelected)
        {
            selectedRoles.Remove(item.Role);

            if (item.Role != UserRole.Congregant
                && !HasLeadershipRoles()
                && selectedRoles.Contains(UserRole.Congregant))
            {
                selectedRoles.Remove(UserRole.Congregant);
            }
        }
        else if (item.Role == UserRole.Congregant)
        {
            selectedRoles.Clear();
            selectedRoles.Add(UserRole.Congregant);
        }
        else
        {
            if (IsCongregantOnlyMode())
                selectedRoles.Clear();

            if (!selectedRoles.Contains(item.Role))
                selectedRoles.Add(item.Role);
        }

        SyncRoleChipState();
        OnPropertyChanged(nameof(SelectedRoles));
        RefreshSaveCommand();
    }

    bool HasLeadershipRoles() =>
        selectedRoles.Any(r => r != UserRole.Congregant);

    bool IsCongregantOnlyMode() =>
        selectedRoles.Count == 1 && selectedRoles[0] == UserRole.Congregant;

    void NormalizeSelectedRoles()
    {
        if (HasLeadershipRoles())
        {
            if (!selectedRoles.Contains(UserRole.Congregant))
                selectedRoles.Add(UserRole.Congregant);
            return;
        }

        if (!IsCongregantOnlyMode())
            selectedRoles.Remove(UserRole.Congregant);
    }

    void SyncRoleChipState()
    {
        NormalizeSelectedRoles();

        var congregantOnly = IsCongregantOnlyMode();
        var leadership = HasLeadershipRoles();

        foreach (var chip in RoleOptions)
        {
            chip.IsSelected = selectedRoles.Contains(chip.Role);
            chip.IsDisabled = chip.Role == UserRole.Congregant
                ? leadership
                : congregantOnly;
        }
    }

    void RefreshSaveCommand()
    {
        (SaveCommand as Command)?.ChangeCanExecute();
    }

    public async Task LoadOnAppearAsync()
    {
        if (auth.IsSignedIn)
            await profileService.RefreshCurrentProfileAsync();

        LoadFromProfile();
    }

    public void DiscardUnsavedChanges()
    {
        StatusMessage = string.Empty;
        LoadFromProfile();
    }

    void LoadFromProfile()
    {
        var profile = profileService.CurrentProfile;
        isEditing = profile?.IsComplete ?? false;

        if (profile == null)
        {
            Email = auth.CurrentEmail ?? string.Empty;
            FirstName = string.Empty;
            LastName = string.Empty;
            Nickname = string.Empty;
            selectedRoles.Clear();
            SyncRoleChipState();
            OnPropertyChanged(nameof(SelectedRoles));
            RefreshSaveCommand();
            return;
        }

        Email = !string.IsNullOrWhiteSpace(profile.Email) ? profile.Email : auth.CurrentEmail ?? string.Empty;
        FirstName = profile.FirstName ?? string.Empty;
        LastName = profile.LastName ?? string.Empty;
        Nickname = profile.Nickname ?? string.Empty;

        selectedRoles.Clear();
        if (profile.Roles != null)
        {
            foreach (var role in profile.Roles)
                selectedRoles.Add(role);
        }

        SyncRoleChipState();
        OnPropertyChanged(nameof(SelectedRoles));
        RefreshSaveCommand();
    }

    async Task SaveAsync()
    {
        if (!CanSave)
            return;

        try
        {
            IsBusy = true;
            RefreshSaveCommand();
            StatusMessage = string.Empty;

            var profile = profileService.CurrentProfile ?? new UserProfile
            {
                Uid = auth.CurrentUserId,
                Email = auth.CurrentEmail,
            };

            profile.FirstName = FirstName.Trim();
            profile.LastName = LastName.Trim();
            profile.Nickname = Nickname?.Trim() ?? string.Empty;
            profile.Roles = selectedRoles.ToList();

            await profileService.SaveProfileAsync(profile);

            // Clear busy before navigation — Shell GoToAsync/Pop can hang and would leave the spinner forever.
            IsBusy = false;
            RefreshSaveCommand();

            if (isEditing)
            {
                await NavigateBackAfterEditAsync();
                return;
            }

            await AuthNavigationHelper.DismissLoginModalIfPresentAsync();
            await Shell.Current.GoToAsync($"//{Routes.READ}");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            if (IsBusy)
            {
                IsBusy = false;
                RefreshSaveCommand();
            }
        }
    }

    static async Task NavigateBackAfterEditAsync()
    {
        try
        {
            var nav = Shell.Current?.Navigation;
            if (nav != null && nav.NavigationStack.Count > 1)
            {
                await nav.PopAsync();
                return;
            }

            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NavigateBackAfterEditAsync failed: {ex.Message}");
            try
            {
                await Shell.Current.GoToAsync($"//{Routes.READ}");
            }
            catch
            {
            }
        }
    }
}
