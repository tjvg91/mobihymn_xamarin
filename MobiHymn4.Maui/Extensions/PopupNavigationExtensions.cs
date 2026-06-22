using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls;

namespace MobiHymn4.Extensions;

public static class PopupNavigationExtensions
{
    public static void ShowPopup(this INavigation navigation, Popup popup)
    {
        var page = navigation as Page ?? Shell.Current?.CurrentPage ?? Application.Current?.MainPage;
        page?.ShowPopupAsync(popup);
    }

    public static void ShowPopup(this Page page, Popup popup) =>
        _ = page.ShowPopupAsync(popup);
}
