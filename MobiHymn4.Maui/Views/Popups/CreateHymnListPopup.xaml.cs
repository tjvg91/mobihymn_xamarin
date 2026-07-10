using System;
using System.Threading.Tasks;
using CommunityToolkit.Maui.Views;
using MobiHymn4.Utils;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MobiHymn4.Views.Popups;

public partial class CreateHymnListPopup : Popup
{
    readonly Func<DateTime, Task<bool>> isDateTakenAsync;

    public CreateHymnListPopup(
        DateTime initialDate,
        Func<DateTime, Task<bool>> isDateTakenAsync,
        string title = "New hymn list",
        string message = "Choose a date for this hymn list.",
        string confirmLabel = "Create")
    {
        this.isDateTakenAsync = isDateTakenAsync;
        InitializeComponent();
        lblTitle.Text = title;
        lblMessage.Text = message;
        btnConfirm.Text = confirmLabel;
        datePicker.Date = initialDate.Date;
        ApplyDatePickerTheme();
        Opened += CreateHymnListPopup_Opened;
    }

    void ApplyDatePickerTheme()
    {
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        datePicker.TextColor = isDark
            ? Colors.White
            : Application.Current?.Resources.TryGetValue("PrimaryText", out var color) == true && color is Color c
                ? c
                : Colors.Black;
        datePicker.BackgroundColor = Colors.Transparent;
    }

    void CreateHymnListPopup_Opened(object sender, EventArgs e)
    {
        ApplyDatePickerTheme();
        var display = DeviceDisplay.MainDisplayInfo;
        Size = new Size(display.Width / display.Density, display.Height / display.Density);
    }

    void DatePicker_DateSelected(object sender, DateChangedEventArgs e)
    {
        lblError.IsVisible = false;
    }

    void Overlay_Tapped(object sender, TappedEventArgs e) => Close(null);

    void Cancel_Clicked(object sender, EventArgs e) => Close(null);

    async void Create_Clicked(object sender, EventArgs e)
    {
        var date = datePicker.Date.Date;
        if (await isDateTakenAsync(date))
        {
            lblError.Text = $"A hymn list already exists for {GroupHymnListDates.FormatName(date)}.";
            lblError.IsVisible = true;
            return;
        }

        Close(date);
    }

    public static async Task<DateTime?> PickDateAsync(
        Page host,
        DateTime initialDate,
        Func<DateTime, Task<bool>> isDateTakenAsync,
        string title = "New hymn list",
        string message = "Choose a date for this hymn list.",
        string confirmLabel = "Create")
    {
        if (host == null)
            return null;

        var popup = new CreateHymnListPopup(initialDate, isDateTakenAsync, title, message, confirmLabel);
        var result = await host.ShowPopupAsync(popup);
        return result is DateTime date ? date.Date : null;
    }
}
