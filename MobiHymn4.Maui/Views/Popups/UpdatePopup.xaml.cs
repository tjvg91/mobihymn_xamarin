using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls;

namespace MobiHymn4.Views.Popups;

public partial class UpdatePopup : Popup
{
    public const string ResultDownload = "download";
    public const string ResultLater = "later";

    public UpdatePopup()
    {
        InitializeComponent();
        Opened += UpdatePopup_Opened;
    }

    public void Configure(string title, string message, bool mandatory, string downloadText = "Download", string laterText = "Later")
    {
        lblTitle.Text = title;
        lblMessage.Text = message;
        btnDownload.Text = downloadText;
        btnLater.Text = laterText;
        btnLater.IsVisible = !mandatory;
        CanBeDismissedByTappingOutsideOfPopup = !mandatory;

        overlay.GestureRecognizers.Clear();
        if (!mandatory)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += Overlay_Tapped;
            overlay.GestureRecognizers.Add(tap);
        }

        if (mandatory)
        {
            btnDownload.SetValue(Grid.ColumnProperty, 0);
            btnDownload.SetValue(Grid.ColumnSpanProperty, 2);
        }
        else
        {
            btnDownload.SetValue(Grid.ColumnProperty, 1);
            btnDownload.SetValue(Grid.ColumnSpanProperty, 1);
        }
    }

    void UpdatePopup_Opened(object sender, EventArgs e)
    {
        var display = DeviceDisplay.MainDisplayInfo;
        Size = new Size(display.Width / display.Density, display.Height / display.Density);
        ApplyUpdateAnimation();
    }

    void ApplyUpdateAnimation()
    {
        var source = Application.Current?.UserAppTheme == AppTheme.Dark
            ? "update-dark"
            : "update";

        if (updateAnimation.Source != source)
            updateAnimation.Source = source;
        else
            updateAnimation.ForceReload();
    }

    void Overlay_Tapped(object sender, TappedEventArgs e) => Close(ResultLater);

    void btnLater_Clicked(object sender, EventArgs e) => Close(ResultLater);

    void btnDownload_Clicked(object sender, EventArgs e) => Close(ResultDownload);
}
