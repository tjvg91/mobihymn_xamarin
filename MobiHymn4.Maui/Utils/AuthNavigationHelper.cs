using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MobiHymn4.Services;
using MobiHymn4.Views;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace MobiHymn4.Utils;

public static class AuthNavigationHelper
{
    static bool pendingSignUpMode;

    public static void SetPendingSignUpMode(bool value) => pendingSignUpMode = value;

    public static bool ConsumePendingSignUpMode()
    {
        if (!pendingSignUpMode)
            return false;

        pendingSignUpMode = false;
        return true;
    }

    public static async Task SignOutAndNavigateAsync()
    {
        try
        {
            // Flush this account's pending backup before uid becomes null.
            await ServiceHelper.Get<IUserSettingsSyncService>().PushAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Sign-out settings flush skipped: {ex.Message}");
        }

        await ServiceHelper.Get<IAuthService>().SignOutAsync();
        await NavigateForAuthStateAsync();
    }

    public static async Task NavigateForAuthStateAsync()
    {
        if (Shell.Current == null)
            return;

        await DismissLoginModalIfPresentAsync();

        var auth = ServiceHelper.Get<IAuthService>();
        if (!auth.IsSignedIn)
        {
            await NavigateToLoginAsync(signUpMode: false);
            return;
        }

        var profile = ServiceHelper.Get<IProfileService>();
        await profile.RefreshCurrentProfileAsync();

        if (!auth.IsEmailVerified)
        {
            await Shell.Current.GoToAsync(Routes.VERIFY_EMAIL);
            return;
        }

        if (!profile.HasCompleteProfile)
        {
            await Shell.Current.GoToAsync(Routes.PROFILE_SETUP);
            return;
        }

        await Shell.Current.GoToAsync($"//{Routes.HOME}");
    }

    /// <summary>
    /// Navigate to the Login page, optionally as a modal.
    /// Everything that touches UI objects runs on the main thread.
    /// </summary>
    public static Task<bool> NavigateToLoginAsync(bool signUpMode = false, bool asModal = false)
    {
        // Set the flag before jumping to the main thread so it is visible
        // when LoginPage.OnAppearing fires during PushModalAsync.
        pendingSignUpMode = signUpMode;

        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (Shell.Current == null)
                return false;

            // If we're already on the login page, just update mode.
            if (Shell.Current.CurrentPage is LoginPage existingLogin)
            {
                existingLogin.ApplyAuthModeFromNavigation();
                return true;
            }

            if (asModal)
            {
                try
                {
                    await Shell.Current.Navigation.PushModalAsync(new LoginPage());
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"NavigateToLoginAsync PushModalAsync failed: {ex.Message}");
                    // Fall through to Shell routing.
                }
            }

            return await NavigateToLoginShellAsync(signUpMode);
        });
    }

    static async Task<bool> NavigateToLoginShellAsync(bool signUpMode)
    {
        if (Shell.Current == null)
            return false;

        pendingSignUpMode = signUpMode;
        var query = signUpMode ? "?mode=signup" : string.Empty;

        try
        {
            await Shell.Current.GoToAsync($"{Routes.LOGIN}{query}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NavigateToLoginAsync GoToAsync relative failed: {ex.Message}");
        }

        try
        {
            await Shell.Current.GoToAsync($"//{Routes.READ}/{Routes.LOGIN}{query}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NavigateToLoginAsync GoToAsync stacked failed: {ex.Message}");
            return false;
        }
    }

    public static async Task DismissLoginModalIfPresentAsync()
    {
        if (!MainThread.IsMainThread)
        {
            await MainThread.InvokeOnMainThreadAsync(DismissLoginModalIfPresentAsync);
            return;
        }

        var nav = Shell.Current?.Navigation;
        if (nav?.ModalStack.Count > 0 && nav.ModalStack.LastOrDefault() is LoginPage)
            await nav.PopModalAsync();
    }
}
