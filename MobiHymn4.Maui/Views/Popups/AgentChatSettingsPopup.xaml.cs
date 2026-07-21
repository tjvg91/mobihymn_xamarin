using System;
using System.Collections.Generic;
using CommunityToolkit.Maui.Views;
using FontAwesome;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using MobiHymn4.Utils;

namespace MobiHymn4.Views.Popups
{
    public partial class AgentChatSettingsPopup : Popup
    {
        static readonly AgentMode[] ModeOptions = { AgentMode.Auto, AgentMode.Local, AgentMode.Cloud };

        readonly Globals globalInstance = Globals.Instance;
        enum ChoiceKind { None, Agent, Limit }
        ChoiceKind activeChoice = ChoiceKind.None;

        public AgentChatSettingsPopup()
        {
            InitializeComponent();
            Opened += AgentChatSettingsPopup_Opened;
            RefreshValues();
        }

        void AgentChatSettingsPopup_Opened(object sender, EventArgs e)
        {
            var display = DeviceDisplay.MainDisplayInfo;
            Size = new Size(display.Width / display.Density, display.Height / display.Density);
        }

        void RefreshValues()
        {
            modeValueLabel.Text = globalInstance.AgentMode.ToString();
            limitValueLabel.Text = Globals.SnapAgentChatLimit(globalInstance.AgentChatLimit).ToString();
        }

        void AgentValue_Tapped(object sender, TappedEventArgs e) => ShowChoices(ChoiceKind.Agent);

        void LimitValue_Tapped(object sender, TappedEventArgs e) => ShowChoices(ChoiceKind.Limit);

        void ShowChoices(ChoiceKind kind)
        {
            activeChoice = kind;
            settingsPanel.IsVisible = false;
            choicePanel.IsVisible = true;
            choicePanel.Children.Clear();
            backButton.IsVisible = true;
            doneButton.Text = "Cancel";

            titleLabel.Text = kind == ChoiceKind.Agent ? "Agent" : "Results limit";

            if (kind == ChoiceKind.Agent)
            {
                foreach (var mode in ModeOptions)
                {
                    var selected = mode == globalInstance.AgentMode;
                    choicePanel.Children.Add(CreateChoiceRow(mode.ToString(), selected, () =>
                    {
                        globalInstance.AgentMode = mode;
                        globalInstance.SaveSettings();
                        ShowSettings();
                    }));
                }
            }
            else
            {
                var current = Globals.SnapAgentChatLimit(globalInstance.AgentChatLimit);
                foreach (var limit in Globals.AgentChatLimitOptions)
                {
                    var selected = limit == current;
                    var captured = limit;
                    choicePanel.Children.Add(CreateChoiceRow(captured.ToString(), selected, () =>
                    {
                        globalInstance.AgentChatLimit = captured;
                        globalInstance.SaveSettings();
                        ShowSettings();
                    }));
                }
            }
        }

        void ShowSettings()
        {
            activeChoice = ChoiceKind.None;
            choicePanel.IsVisible = false;
            choicePanel.Children.Clear();
            settingsPanel.IsVisible = true;
            backButton.IsVisible = false;
            doneButton.Text = "Done";
            titleLabel.Text = "AI settings";
            RefreshValues();
        }

        View CreateChoiceRow(string label, bool selected, Action onSelect)
        {
            var primary = ResolveColor("Primary", Color.FromArgb("#F5D200"));
            var primaryText = ResolveColor("PrimaryText", Color.FromArgb("#2D2D2D"));
            var dark = Application.Current?.RequestedTheme == AppTheme.Dark
                || Application.Current?.UserAppTheme == AppTheme.Dark;
            var normalText = dark ? Colors.White : primaryText;
            var rowBg = selected
                ? (dark ? Color.FromArgb("#2A2A2A") : Color.FromArgb("#FFF8E1"))
                : Colors.Transparent;

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                Padding = new Thickness(12, 10),
                BackgroundColor = rowBg
            };

            var text = new Label
            {
                Text = label,
                FontSize = 15,
                FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None,
                VerticalOptions = LayoutOptions.Center,
                TextColor = selected ? (dark ? primary : primaryText) : normalText
            };

            var check = new Label
            {
                Text = FontAwesomeIcons.Check,
                FontFamily = "FAS",
                FontSize = 14,
                VerticalOptions = LayoutOptions.Center,
                IsVisible = selected,
                TextColor = primary
            };

            grid.Add(text);
            grid.Add(check, 1, 0);

            var border = new Border
            {
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                BackgroundColor = rowBg,
                Content = grid,
                Margin = new Thickness(0, 1)
            };

            border.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(onSelect)
            });

            return border;
        }

        static Color ResolveColor(string key, Color fallback) =>
            Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
                ? color
                : fallback;

        void Back_Clicked(object sender, EventArgs e) => ShowSettings();

        void Overlay_Tapped(object sender, EventArgs e)
        {
            if (activeChoice != ChoiceKind.None)
                ShowSettings();
            else
                Close(null);
        }

        void Done_Clicked(object sender, EventArgs e)
        {
            if (activeChoice != ChoiceKind.None)
                ShowSettings();
            else
                Close(null);
        }
    }
}
