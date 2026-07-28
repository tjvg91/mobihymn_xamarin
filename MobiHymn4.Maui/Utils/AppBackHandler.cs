using System.Threading.Tasks;
using MobiHymn4.Views;
using Microsoft.Maui.Controls;

namespace MobiHymn4.Utils;

/// <summary>
/// Central Android/hardware back handling: flyout → modal/nav stack → page overlays → shell history.
/// </summary>
public static class AppBackHandler
{
    public static bool TryHandle()
    {
        var shell = Shell.Current;
        if (shell == null)
            return false;

        if (shell.FlyoutIsPresented)
        {
            shell.FlyoutIsPresented = false;
            return true;
        }

        var nav = shell.Navigation;
        if (nav?.ModalStack?.Count > 0)
        {
            _ = nav.PopModalAsync();
            return true;
        }

        if (nav?.NavigationStack?.Count > 1)
        {
            _ = nav.PopAsync();
            return true;
        }

        if (shell.CurrentPage is ReadPage readPage && readPage.TryHandleBack())
            return true;

        if (ShellNavigationHistory.Instance.TryGoBack())
            return true;

        return false;
    }
}
