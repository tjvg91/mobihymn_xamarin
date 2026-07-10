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

[QueryProperty(nameof(GroupId), "groupId")]
public class GroupManageViewModel : BaseViewModel
{
    readonly IGroupService groupService;

    string groupId = string.Empty;
    string groupName = string.Empty;
    string joinCode = string.Empty;
    string inviteEmail = string.Empty;
    string statusMessage = string.Empty;
    ObservableRangeCollection<GroupMember> members = new();

    public GroupManageViewModel()
    {
        groupService = ServiceHelper.Get<IGroupService>();
        Title = "Manage Group";

        RefreshCommand = new Command(async () => await LoadAsync());
        InviteCommand = new Command(async () => await InviteAsync(), () => !IsBusy && !string.IsNullOrWhiteSpace(InviteEmail));
        LeaveGroupCommand = new Command(async () => await LeaveGroupAsync(), () => !IsBusy && !string.IsNullOrWhiteSpace(GroupId));
    }

    public string GroupId
    {
        get => groupId;
        set
        {
            groupId = value;
            _ = LoadAsync();
        }
    }

    public string GroupName
    {
        get => groupName;
        set => SetProperty(ref groupName, value);
    }

    public string JoinCode
    {
        get => joinCode;
        set => SetProperty(ref joinCode, value);
    }

    public string InviteEmail
    {
        get => inviteEmail;
        set
        {
            if (SetProperty(ref inviteEmail, value))
                (InviteCommand as Command)?.ChangeCanExecute();
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

    public ObservableRangeCollection<GroupMember> Members
    {
        get => members;
        set => SetProperty(ref members, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand InviteCommand { get; }
    public ICommand LeaveGroupCommand { get; }

    async Task LoadAsync()
    {
        if (string.IsNullOrWhiteSpace(GroupId))
            return;

        try
        {
            IsBusy = true;
            (InviteCommand as Command)?.ChangeCanExecute();
            StatusMessage = string.Empty;

            var groups = await groupService.GetMyGroupsAsync();
            var group = groups.FirstOrDefault(g => g.Id == GroupId);
            if (group != null)
            {
                GroupName = group.Name;
                JoinCode = group.JoinCode;
                Title = group.Name;
            }

            var memberList = await groupService.GetMembersAsync(GroupId);
            Members.ReplaceRange(memberList);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            (InviteCommand as Command)?.ChangeCanExecute();
            (LeaveGroupCommand as Command)?.ChangeCanExecute();
        }
    }

    async Task InviteAsync()
    {
        if (string.IsNullOrWhiteSpace(InviteEmail) || string.IsNullOrWhiteSpace(GroupId))
            return;

        try
        {
            IsBusy = true;
            (InviteCommand as Command)?.ChangeCanExecute();
            StatusMessage = string.Empty;
            await groupService.InviteByEmailAsync(GroupId, InviteEmail.Trim());
            InviteEmail = string.Empty;
            StatusMessage = "Invitation sent.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            (InviteCommand as Command)?.ChangeCanExecute();
            (LeaveGroupCommand as Command)?.ChangeCanExecute();
        }
    }

    async Task LeaveGroupAsync()
    {
        if (string.IsNullOrWhiteSpace(GroupId))
            return;

        var page = Shell.Current?.CurrentPage;
        if (page == null)
            return;

        var groupLabel = string.IsNullOrWhiteSpace(GroupName) ? "this group" : GroupName;
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
            (InviteCommand as Command)?.ChangeCanExecute();
            (LeaveGroupCommand as Command)?.ChangeCanExecute();
            StatusMessage = string.Empty;
            await groupService.LeaveGroupAsync(GroupId);
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            (InviteCommand as Command)?.ChangeCanExecute();
            (LeaveGroupCommand as Command)?.ChangeCanExecute();
        }
    }
}
