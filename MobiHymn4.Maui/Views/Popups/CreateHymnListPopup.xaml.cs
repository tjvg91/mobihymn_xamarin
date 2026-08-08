using System;
using System.Globalization;
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
    DateTime selectedDate;
    DateTime visibleMonth;

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
        selectedDate = initialDate.Date;
        visibleMonth = new DateTime(initialDate.Year, initialDate.Month, 1);
        RebuildCalendar();
        Opened += CreateHymnListPopup_Opened;
    }

    void CreateHymnListPopup_Opened(object sender, EventArgs e)
    {
        var display = DeviceDisplay.MainDisplayInfo;
        Size = new Size(display.Width / display.Density, display.Height / display.Density);
        RebuildCalendar();
    }

    void RebuildCalendar()
    {
        lblMonthTitle.Text = visibleMonth.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        dayGrid.Children.Clear();
        dayGrid.RowDefinitions.Clear();

        var first = visibleMonth;
        var startPad = (int)first.DayOfWeek;
        var daysInMonth = DateTime.DaysInMonth(first.Year, first.Month);
        var totalCells = startPad + daysInMonth;
        var rows = (int)Math.Ceiling(totalCells / 7.0);
        for (var r = 0; r < rows; r++)
            dayGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });

        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var primaryText = ResolveColor("PrimaryText", Colors.Black);
        var primary = ResolveColor("Primary", Color.FromArgb("#F5D200"));
        var normalText = isDark ? Colors.White : primaryText;
        var selectedBg = isDark ? primary : Color.FromArgb("#2D2D2D");
        var selectedFg = isDark ? Colors.Black : Colors.White;

        for (var i = 0; i < rows * 7; i++)
        {
            var row = i / 7;
            var col = i % 7;
            var dayIndex = i - startPad;

            if (dayIndex < 0 || dayIndex >= daysInMonth)
            {
                dayGrid.Add(new BoxView { Color = Colors.Transparent }, col, row);
                continue;
            }

            var date = new DateTime(first.Year, first.Month, dayIndex + 1);
            var selected = date.Date == selectedDate.Date;
            var today = date.Date == DateTime.Today;

            var cell = new Border
            {
                StrokeThickness = today && !selected ? 1.5 : 0,
                Stroke = primary,
                BackgroundColor = selected ? selectedBg : Colors.Transparent,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                Padding = 0,
            };
            var label = new Label
            {
                Text = date.Day.ToString(CultureInfo.InvariantCulture),
                FontSize = 14,
                FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None,
                TextColor = selected ? selectedFg : normalText,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
            };
            cell.Content = label;

            var tap = new TapGestureRecognizer();
            var captured = date;
            tap.Tapped += (_, _) =>
            {
                selectedDate = captured.Date;
                visibleMonth = new DateTime(captured.Year, captured.Month, 1);
                lblError.IsVisible = false;
                RebuildCalendar();
            };
            cell.GestureRecognizers.Add(tap);
            dayGrid.Add(cell, col, row);
        }
    }

    static Color ResolveColor(string key, Color fallback)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color c)
            return c;
        return fallback;
    }

    void PrevMonth_Clicked(object sender, EventArgs e)
    {
        visibleMonth = visibleMonth.AddMonths(-1);
        RebuildCalendar();
    }

    void NextMonth_Clicked(object sender, EventArgs e)
    {
        visibleMonth = visibleMonth.AddMonths(1);
        RebuildCalendar();
    }

    void Today_Clicked(object sender, EventArgs e)
    {
        selectedDate = DateTime.Today;
        visibleMonth = new DateTime(selectedDate.Year, selectedDate.Month, 1);
        lblError.IsVisible = false;
        RebuildCalendar();
    }

    void Overlay_Tapped(object sender, TappedEventArgs e) => Close(null);

    void Cancel_Clicked(object sender, EventArgs e) => Close(null);

    async void Create_Clicked(object sender, EventArgs e)
    {
        var date = selectedDate.Date;
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
