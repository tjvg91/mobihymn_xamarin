using CommunityToolkit.Maui.Views;
using MobiHymn4.Utils;
using MobiHymn4.ViewModels;
using Microsoft.Maui.Controls;

namespace MobiHymn4.Views.Popups;

public partial class SyncPopup : Popup
{
    public object SyncResult { get; private set; }

    public SyncPopup()
    {
        InitializeComponent();

        if (BindingContext is SettingsViewModel model)
            model.EnsureResyncInitialized();

        var count = Globals.Instance.ResyncDetails?.Count ?? 0;
        var listHeight = Math.Min(220, Math.Max(56, count * 34));
        changesList.HeightRequest = listHeight;

        var mainDisplayInfo = DeviceDisplay.MainDisplayInfo;
        var width = Math.Min(450, (mainDisplayInfo.Width / mainDisplayInfo.Density) - (mainDisplayInfo.Width * 0.1));
        var height = 160 + listHeight;
        Size = new Size(width, height);
    }

    void btnLater_Clicked(object sender, EventArgs e)
    {
        SyncResult = null;
        Close(null);
    }

    void btnResync_Clicked(object sender, EventArgs e)
    {
        SyncResult = "sync";
        Close("sync");
    }
}
