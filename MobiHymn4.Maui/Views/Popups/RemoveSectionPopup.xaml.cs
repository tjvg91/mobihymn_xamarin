using CommunityToolkit.Maui.Views;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MobiHymn4.Views.Popups;

public sealed class RemoveSectionPopupResult
{
    public bool DeleteHymnsInSection { get; init; }
    public bool CurrentDateOnly { get; init; }
}

public partial class RemoveSectionPopup : Popup
{
    public RemoveSectionPopup(string sectionName, int hymnCount, bool isSaved)
    {
        InitializeComponent();
        lblTitle.Text = $"Remove \"{sectionName}\"?";
        lblMessage.Text = isSaved
            ? "Remove this saved section from the board?"
            : "Remove this section from the board?";

        if (hymnCount > 0)
        {
            deleteHymnsRow.IsVisible = true;
            lblDeleteHymns.Text = hymnCount == 1
                ? "Also delete 1 hymn in this section"
                : $"Also delete {hymnCount} hymns in this section";
        }

        if (isSaved)
        {
            currentDateOnlyRow.IsVisible = true;
            chkCurrentDateOnly.IsChecked = true;
        }

        Opened += RemoveSectionPopup_Opened;
    }

    void RemoveSectionPopup_Opened(object sender, System.EventArgs e)
    {
        var display = DeviceDisplay.MainDisplayInfo;
        Size = new Size(display.Width / display.Density, display.Height / display.Density);
    }

    void DeleteHymnsLabel_Tapped(object sender, TappedEventArgs e) =>
        chkDeleteHymns.IsChecked = !chkDeleteHymns.IsChecked;

    void CurrentDateOnlyLabel_Tapped(object sender, TappedEventArgs e) =>
        chkCurrentDateOnly.IsChecked = !chkCurrentDateOnly.IsChecked;

    void Overlay_Tapped(object sender, TappedEventArgs e) => Close(null);

    void Cancel_Clicked(object sender, System.EventArgs e) => Close(null);

    void Remove_Clicked(object sender, System.EventArgs e) =>
        Close(new RemoveSectionPopupResult
        {
            DeleteHymnsInSection = chkDeleteHymns.IsChecked,
            CurrentDateOnly = chkCurrentDateOnly.IsChecked,
        });

    public static async Task<RemoveSectionPopupResult> ConfirmAsync(
        Page host,
        string sectionName,
        int hymnCount,
        bool isSaved)
    {
        if (host == null)
            return null;

        var popup = new RemoveSectionPopup(sectionName, hymnCount, isSaved);
        var result = await host.ShowPopupAsync(popup);
        return result as RemoveSectionPopupResult;
    }
}
