using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Maui.Views;
using MobiHymn4.Utils;
using MobiHymn4.Views;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace MobiHymn4.Views.Popups;

public partial class CommunitySignInPopup : Popup
{
    public const string ResultSignUp = "signup";
    public const string ResultLater = "later";

    bool navigating;

    public CommunitySignInPopup()
    {
        InitializeComponent();
        Opened += CommunitySignInPopup_Opened;
        CanBeDismissedByTappingOutsideOfPopup = false;
    }

    void CommunitySignInPopup_Opened(object sender, EventArgs e)
    {
        var display = DeviceDisplay.MainDisplayInfo;
        Size = new Size(display.Width / display.Density, display.Height / display.Density);
        communityAnimation.ForceReload();
    }

    void btnLater_Clicked(object sender, EventArgs e) => Close(ResultLater);

    async void btnSignUp_Clicked(object sender, EventArgs e)
    {
        if (navigating)
            return;

        navigating = true;
        btnSignUp.IsEnabled = false;

        // Set sign-up mode before closing so LoginPage.OnAppearing picks it up.
        AuthNavigationHelper.SetPendingSignUpMode(true);

        // Close the popup first, then push the login modal after a short delay
        // so the dismiss animation has time to finish.
        Close(ResultSignUp);

        await Task.Delay(350);

        try
        {
            var nav = Application.Current?.MainPage?.Navigation
                      ?? Shell.Current?.Navigation;

            if (nav != null)
                await nav.PushModalAsync(new LoginPage());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CommunitySignInPopup SignUp navigation failed: {ex.Message}");
            AuthNavigationHelper.SetPendingSignUpMode(false);
        }
    }
}
