using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using MobiHymn4.Models;

namespace MobiHymn4.Views.Popups
{
    public partial class HymnInfoPopup : Popup
    {
        public HymnInfoPopup()
        {
            InitializeComponent();
            Opened += (_, _) =>
            {
                var display = DeviceDisplay.MainDisplayInfo;
                Size = new Size(display.Width / display.Density, display.Height / display.Density);
            };
        }

        public void Bind(Hymn hymn)
        {
            if (hymn == null)
            {
                lblNumber.Text = "Hymn";
                lblName.IsVisible = false;
                lblEmpty.IsVisible = true;
                return;
            }

            lblNumber.Text = string.IsNullOrWhiteSpace(hymn.Title)
                ? $"Hymn #{hymn.Number}"
                : $"Hymn #{hymn.Title}";

            if (!string.IsNullOrWhiteSpace(hymn.Name))
            {
                lblName.Text = hymn.Name;
                lblName.IsVisible = true;
            }
            else
            {
                lblName.IsVisible = false;
            }

            detailsHost.Children.Clear();

            var rows = new List<(string Label, IEnumerable<string> Values)>();
            AddRow(rows, "First line", hymn.FirstLine);
            AddRow(rows, "Author", hymn.Author);
            AddRow(rows, "Metre", hymn.Metre);
            AddRow(rows, "Tune", hymn.Tune);
            AddRow(rows, "Composer", hymn.TuneComposer);
            AddRow(rows, "Key", hymn.TuneKey);
            AddRow(rows, "Verses", hymn.GetVerseReferences());
            AddRow(rows, "Tags", hymn.Tags);
            AddRow(rows, "Year", hymn.Year);
            AddRow(rows, "Remark", hymn.Remark);

            var added = 0;
            foreach (var (label, values) in rows)
            {
                var pills = values
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v.Trim())
                    .ToList();
                if (pills.Count == 0)
                    continue;

                detailsHost.Children.Add(BuildRow(label, pills));
                added++;
            }

            lblEmpty.IsVisible = added == 0 && string.IsNullOrWhiteSpace(hymn.Name);
        }

        static void AddRow(List<(string Label, IEnumerable<string> Values)> rows, string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            rows.Add((label, new[] { value.Trim() }));
        }

        static void AddRow(List<(string Label, IEnumerable<string> Values)> rows, string label, IEnumerable<string> values)
        {
            if (values == null)
                return;

            var pills = values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .ToList();
            if (pills.Count == 0)
                return;

            rows.Add((label, pills));
        }

        static View BuildRow(string label, IReadOnlyList<string> values)
        {
            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            var pillBg = isDark ? Color.FromArgb("#333333") : Color.FromArgb("#F0F0F0");
            var pillText = isDark
                ? Colors.White
                : (Color)Application.Current.Resources["PrimaryText"];
            var labelColor = isDark
                ? (Color)Application.Current.Resources["GrayLight"]
                : (Color)Application.Current.Resources["Gray"];

            var pills = new FlexLayout
            {
                Wrap = FlexWrap.Wrap,
                Direction = FlexDirection.Row,
                AlignItems = FlexAlignItems.Start,
                JustifyContent = FlexJustify.Start,
            };

            foreach (var value in values)
            {
                pills.Children.Add(new Border
                {
                    Padding = new Thickness(10, 5),
                    Margin = new Thickness(0, 0, 6, 6),
                    StrokeThickness = 0,
                    BackgroundColor = pillBg,
                    StrokeShape = new RoundRectangle { CornerRadius = 12 },
                    Content = new Label
                    {
                        Text = value,
                        FontSize = 13,
                        LineBreakMode = LineBreakMode.WordWrap,
                        TextColor = pillText
                    }
                });
            }

            return new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    new Label
                    {
                        Text = label,
                        FontSize = 12,
                        TextColor = labelColor
                    },
                    pills
                }
            };
        }

        void Overlay_Tapped(object sender, EventArgs e) => Close(null);

        void btnClose_Clicked(object sender, EventArgs e) => Close(null);
    }
}
