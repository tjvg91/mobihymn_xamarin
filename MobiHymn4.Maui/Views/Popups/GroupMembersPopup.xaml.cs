using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Maui.Views;
using MobiHymn4.Models;
using MobiHymn4.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MobiHymn4.Views.Popups;

public partial class GroupMembersPopup : Popup
{
    readonly IGroupService groupService;
    readonly string groupId;
    readonly ObservableCollection<GroupMember> members = new();

    public GroupMembersPopup(IGroupService groupService, string groupId, string groupName)
    {
        this.groupService = groupService;
        this.groupId = groupId;
        InitializeComponent();
        lblGroupName.Text = groupName ?? string.Empty;
        membersList.ItemsSource = members;
        Opened += GroupMembersPopup_Opened;
    }

    void GroupMembersPopup_Opened(object sender, EventArgs e)
    {
        var display = DeviceDisplay.MainDisplayInfo;
        Size = new Size(display.Width / display.Density, display.Height / display.Density);
        _ = LoadMembersAsync();
    }

    async Task LoadMembersAsync()
    {
        loader.IsVisible = true;
        membersList.IsVisible = false;
        lblError.IsVisible = false;

        try
        {
            var memberList = await groupService.GetMembersAsync(groupId);
            members.Clear();
            foreach (var member in memberList)
                members.Add(member);

            loader.IsVisible = false;
            membersList.IsVisible = true;
        }
        catch (Exception ex)
        {
            loader.IsVisible = false;
            lblError.Text = ex.Message;
            lblError.IsVisible = true;
        }
    }

    void Overlay_Tapped(object sender, TappedEventArgs e) => Close();

    void Close_Clicked(object sender, EventArgs e) => Close();

    public static Task ShowAsync(Page host, IGroupService groupService, string groupId, string groupName)
    {
        if (host == null)
            return Task.CompletedTask;

        var popup = new GroupMembersPopup(groupService, groupId, groupName);
        return host.ShowPopupAsync(popup);
    }
}
