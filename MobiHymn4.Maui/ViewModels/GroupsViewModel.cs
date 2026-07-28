using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MobiHymn4.Models;
using MobiHymn4.Services;
using MobiHymn4.Utils;
using MvvmHelpers;
using Microsoft.Maui.Controls;

namespace MobiHymn4.ViewModels;

public class GroupsViewModel : BaseViewModel
{
    readonly IGroupService groupService;

    string joinCode = string.Empty;
    string statusMessage = string.Empty;
    ObservableRangeCollection<WorshipGroup> groups = new();

    public GroupsViewModel()
    {
        groupService = ServiceHelper.Get<IGroupService>();
        Title = "Groups";

        RefreshCommand = new Command(async () => await LoadGroupsAsync());
        CreateGroupCommand = new Command(async () => await CreateGroupAsync(), () => !IsBusy);
        JoinGroupCommand = new Command(async () => await JoinGroupAsync(), () => !IsBusy && !string.IsNullOrWhiteSpace(JoinCode));
        OpenGroupCommand = new Command<WorshipGroup>(async group => await OpenGroupAsync(group));
        LeaveGroupCommand = new Command<WorshipGroup>(async group => await LeaveGroupAsync(group), _ => !IsBusy);

        _ = LoadGroupsAsync();
    }

    public ObservableRangeCollection<WorshipGroup> Groups
    {
        get => groups;
        set => SetProperty(ref groups, value);
    }

    public string JoinCode
    {
        get => joinCode;
        set
        {
            if (SetProperty(ref joinCode, value))
                (JoinGroupCommand as Command)?.ChangeCanExecute();
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

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool HasGroups => Groups != null && Groups.Count > 0;

    public ICommand RefreshCommand { get; }
    public ICommand CreateGroupCommand { get; }
    public ICommand JoinGroupCommand { get; }
    public ICommand OpenGroupCommand { get; }
    public ICommand LeaveGroupCommand { get; }

    async Task LoadGroupsAsync()
    {
        try
        {
            IsBusy = true;
            RefreshCommands();
            StatusMessage = string.Empty;
            var items = await groupService.GetMyGroupsAsync();
            Groups.ReplaceRange(items);
            OnPropertyChanged(nameof(HasGroups));
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

    async Task CreateGroupAsync()
    {
        var page = Shell.Current?.CurrentPage;
        if (page == null)
            return;

        var name = await page.DisplayPromptAsync("Create group", "Enter a name for your worship group:", "Create", "Cancel");
        if (string.IsNullOrWhiteSpace(name))
            return;

        try
        {
            IsBusy = true;
            RefreshCommands();
            StatusMessage = string.Empty;
            var group = await groupService.CreateGroupAsync(name.Trim());
            await LoadGroupsAsync();
            StatusMessage = "Group created.";
            await GroupJoinWelcomePresenter.ShowIfNeededAsync(page, group, isCreator: true);
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

    async Task JoinGroupAsync()
    {
        if (string.IsNullOrWhiteSpace(JoinCode))
            return;

        try
        {
            IsBusy = true;
            RefreshCommands();
            StatusMessage = string.Empty;
            var group = await groupService.JoinGroupByCodeAsync(JoinCode.Trim());
            JoinCode = string.Empty;
            await LoadGroupsAsync();
            StatusMessage = "Joined group.";
            await GroupJoinWelcomePresenter.ShowIfNeededAsync(Shell.Current?.CurrentPage, group);
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

    async Task OpenGroupAsync(WorshipGroup group)
    {
        if (group == null || string.IsNullOrWhiteSpace(group.Id))
            return;

        await Shell.Current.GoToAsync($"{Routes.GROUP_MANAGE}?groupId={Uri.EscapeDataString(group.Id)}");
    }

    public async Task LeaveGroupAsync(WorshipGroup group)
    {
        if (group == null || string.IsNullOrWhiteSpace(group.Id))
            return;

        var page = Shell.Current?.CurrentPage;
        if (page == null)
            return;

        var groupLabel = string.IsNullOrWhiteSpace(group.Name) ? "this group" : group.Name;
        var confirm = await page.DisplayAlert(
            "Leave group",
            $"Leave {groupLabel}? You can rejoin later with the group code or ID.",
            "Leave",
            "Cancel");
        if (!confirm)
            return;

        try
        {
            IsBusy = true;
            RefreshCommands();
            StatusMessage = string.Empty;
            await groupService.LeaveGroupAsync(group.Id);
            await LoadGroupsAsync();
            StatusMessage = "Left group.";
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

    void RefreshCommands()
    {
        (CreateGroupCommand as Command)?.ChangeCanExecute();
        (JoinGroupCommand as Command)?.ChangeCanExecute();
        (LeaveGroupCommand as Command)?.ChangeCanExecute();
    }
}
