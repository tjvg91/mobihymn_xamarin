using System;
using CommunityToolkit.Maui.Views;
using MobiHymn4.Models;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace MobiHymn4.Views.Popups;

public partial class GroupJoinWelcomePopup : Popup
{
    public const string ResultDismissed = "dismissed";

    public GroupJoinWelcomePopup(WorshipGroup group, bool isCreator = false)
    {
        InitializeComponent();
        CanBeDismissedByTappingOutsideOfPopup = false;

        var groupLabel = string.IsNullOrWhiteSpace(group?.Name) ? "your group" : group.Name.Trim();
        lblGroupName.Text = groupLabel;
        lblMessage.Text = isCreator
            ? "Your worship group is ready. Invite your team and start planning hymn lists together."
            : $"You've joined {groupLabel}. Plan hymns together, share lists, and stay in sync with your worship team.";

        Opened += (_, _) =>
        {
            var display = DeviceDisplay.MainDisplayInfo;
            Size = new Size(display.Width / display.Density, display.Height / display.Density);
            welcomeAnimation.ForceReload();
        };
    }

    void btnGetStarted_Clicked(object sender, EventArgs e) => Close(ResultDismissed);
}
