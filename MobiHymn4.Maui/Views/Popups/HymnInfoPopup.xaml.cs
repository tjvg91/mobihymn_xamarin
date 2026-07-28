using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Maui.Views;
using FontAwesome;
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

            var rows = new List<(string Label, string Icon, IEnumerable<string> Values)>();
            AddRow(rows, "First line", FontAwesomeIcons.QuoteLeft, hymn.FirstLine);
            AddRow(rows, "Author", FontAwesomeIcons.PenNib, hymn.Author);
            AddRow(rows, "Metre", FontAwesomeIcons.BookOpen, hymn.Metre);
            AddRow(rows, "Tune", FontAwesomeIcons.Music, hymn.Tune);
            AddRow(rows, "Composer", FontAwesomeIcons.User, hymn.TuneComposer);
            AddRow(rows, "Key", FontAwesomeIcons.Key, hymn.TuneKey);
            AddRow(rows, "Verses", FontAwesomeIcons.BookBible, hymn.GetVerseReferences());
            AddRow(rows, "Topic", FontAwesomeIcons.Tags, hymn.Tags);
            AddRow(rows, "Year", FontAwesomeIcons.Calendar, hymn.Year);
            AddRow(rows, "Remark", FontAwesomeIcons.Comment, hymn.Remark);

            var added = 0;
            foreach (var (label, icon, values) in rows)
            {
                var pills = values
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v.Trim())
                    .ToList();
                if (pills.Count == 0)
                    continue;

                detailsHost.Children.Add(BuildRow(label, icon, pills));
                added++;
            }

            lblEmpty.IsVisible = added == 0 && string.IsNullOrWhiteSpace(hymn.Name);
        }

        static void AddRow(
            List<(string Label, string Icon, IEnumerable<string> Values)> rows,
            string label,
            string icon,
            string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            rows.Add((label, icon, new[] { value.Trim() }));
        }

        static void AddRow(
            List<(string Label, string Icon, IEnumerable<string> Values)> rows,
            string label,
            string icon,
            IEnumerable<string> values)
        {
            if (values == null)
                return;

            var pills = values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .ToList();
            if (pills.Count == 0)
                return;

            rows.Add((label, icon, pills));
        }

        static View BuildRow(string label, string icon, IReadOnlyList<string> values)
        {
            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            var resources = Application.Current.Resources;
            var primary = (Color)resources["Primary"];
            var primaryText = (Color)resources["PrimaryText"];
            var gray = (Color)resources["Gray"];
            var grayLight = (Color)resources["GrayLight"];

            var cardBg = isDark ? Color.FromArgb("#333333") : Color.FromArgb("#F7F7F7");
            var iconBg = isDark ? Color.FromArgb("#3D3D3D") : Color.FromArgb("#FFF5BC");
            var valueColor = isDark ? Colors.White : primaryText;
            var labelColor = isDark ? grayLight : gray;
            var iconColor = isDark ? primary : primaryText;

            var iconBadge = new Border
            {
                WidthRequest = 36,
                HeightRequest = 36,
                StrokeThickness = 0,
                BackgroundColor = iconBg,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                VerticalOptions = LayoutOptions.Start,
                Content = new Label
                {
                    Text = icon,
                    FontFamily = "FAS",
                    FontSize = 14,
                    TextColor = iconColor,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center
                }
            };

            var valueStack = new VerticalStackLayout { Spacing = 4 };
            foreach (var value in values)
            {
                valueStack.Children.Add(new Label
                {
                    Text = value,
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    LineBreakMode = LineBreakMode.WordWrap,
                    TextColor = valueColor
                });
            }

            var textColumn = new VerticalStackLayout
            {
                Spacing = 2,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = label.ToUpperInvariant(),
                        FontSize = 11,
                        FontAttributes = FontAttributes.Bold,
                        CharacterSpacing = 0.6,
                        TextColor = labelColor,
                        Opacity = 0.85
                    },
                    valueStack
                }
            };

            var grid = new Grid
            {
                ColumnSpacing = 12,
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star)
                }
            };
            grid.Add(iconBadge, 0, 0);
            grid.Add(textColumn, 1, 0);

            return new Border
            {
                Padding = new Thickness(12, 10),
                StrokeThickness = 0,
                BackgroundColor = cardBg,
                StrokeShape = new RoundRectangle { CornerRadius = 14 },
                Content = grid
            };
        }

        void Overlay_Tapped(object sender, EventArgs e) => Close(null);

        void btnClose_Clicked(object sender, EventArgs e) => Close(null);
    }
}
