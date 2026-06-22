using System;
using System.Collections.Generic;
using CommunityToolkit.Maui.Views;

using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

using MobiHymn4.Utils;

namespace MobiHymn4.Views.Popups
{
    public partial class SettingsPopup : Popup
    {
        private Globals globalInstance = Globals.Instance;
        ViewModels.ReadViewModel model;
        List<(Border Box, Label Label, TextAlignment Value)> alignmentOptions;
        List<(Border Box, Label Label, Color Value)> themeOptions;
        List<(Border Box, Label Label, string Value)> fontOptions;

        public SettingsPopup()
        {
            InitializeComponent();

            alignmentOptions = new()
            {
                (alignStartBox, alignStartLabel, TextAlignment.Start),
                (alignCenterBox, alignCenterLabel, TextAlignment.Center),
                (alignEndBox, alignEndLabel, TextAlignment.End)
            };

            themeOptions = new()
            {
                (themeWhiteBox, themeWhiteLabel, globalInstance.White),
                (themeSepiaBox, themeSepiaLabel, globalInstance.Sepia),
                (themeBrownBox, themeBrownLabel, globalInstance.Brown),
                (themeGrayBox, themeGrayLabel, globalInstance.Gray),
                (themeDarkBox, themeDarkLabel, globalInstance.PrimaryText),
                (themeGreenBox, themeGreenLabel, globalInstance.Green),
                (themeOrangeBox, themeOrangeLabel, globalInstance.Orange),
                (themeBlueBox, themeBlueLabel, globalInstance.Blue),
                (themePurpleBox, themePurpleLabel, globalInstance.Purple),
                (themePinkBox, themePinkLabel, globalInstance.Pink)
            };

            fontOptions = new()
            {
                (fontRobotoBox, fontRobotoLabel, DeviceInfo.Platform == DevicePlatform.Android ? "Roboto" : "SFPro"),
                (fontNotoBox, fontNotoLabel, "NotoSerif"),
                (fontChelseaBox, fontChelseaLabel, "ChelseaMarket"),
                (fontUnifrakturBox, fontUnifrakturLabel, "UnifrakturMaguntia"),
                (fontStyleScriptBox, fontStyleScriptLabel, "StyleScript"),
                (fontCookieBox, fontCookieLabel, "Cookie"),
                (fontFrostyBox, fontFrostyLabel, "Frosty"),
                (fontKissBox, fontKissLabel, "KGKissMeSlowly"),
                (fontMelonBox, fontMelonLabel, "KGMelonheadz"),
                (fontTeacherBox, fontTeacherLabel, "KGWhattheTeacherWants")
            };

            Opened += SettingsPopup_Opened;
            Closed += SettingsPopup_Closed;
        }

        void SettingsPopup_Opened(object sender, EventArgs e)
        {
            globalInstance.ActiveReadThemeChanged += Globals_ActiveReadThemeChanged;
            UpdateSelectedStates();
        }

        void SettingsPopup_Closed(object sender, CommunityToolkit.Maui.Core.PopupClosedEventArgs e)
        {
            globalInstance.ActiveReadThemeChanged -= Globals_ActiveReadThemeChanged;
        }

        protected override void OnBindingContextChanged()
        {
            base.OnBindingContextChanged();
            model = BindingContext as ViewModels.ReadViewModel;
        }

        private void Globals_ActiveReadThemeChanged(object sender, EventArgs e)
        {
            if (model != null)
                model.ActiveColor = (Color)sender;

            UpdateThemeSelection();
        }

        void Alignment_Tapped(object sender, TappedEventArgs e)
        {
            globalInstance.ActiveAlignment = (e.Parameter as string) switch
            {
                "Center" => TextAlignment.Center,
                "End" => TextAlignment.End,
                _ => TextAlignment.Start
            };
            UpdateAlignmentSelection();
        }

        void Theme_Tapped(object sender, TappedEventArgs e)
        {
            globalInstance.ActiveReadTheme = (e.Parameter as string) switch
            {
                "Sepia" => globalInstance.Sepia,
                "Brown" => globalInstance.Brown,
                "Gray" => globalInstance.Gray,
                "PrimaryText" => globalInstance.PrimaryText,
                "Green" => globalInstance.Green,
                "Orange" => globalInstance.Orange,
                "Blue" => globalInstance.Blue,
                "Purple" => globalInstance.Purple,
                "Pink" => globalInstance.Pink,
                _ => globalInstance.White
            };
            UpdateThemeSelection();
        }

        void Font_Tapped(object sender, TappedEventArgs e)
        {
            if (e.Parameter is not string fontName)
                return;

            globalInstance.ActiveFont = fontName;
            UpdateFontSelection();
        }

        void UpdateSelectedStates()
        {
            UpdateAlignmentSelection();
            UpdateThemeSelection();
            UpdateFontSelection();
        }

        void UpdateAlignmentSelection()
        {
            foreach (var option in alignmentOptions)
                SetOptionSelected(option.Box, option.Label, option.Value == globalInstance.ActiveAlignment);
        }

        void UpdateThemeSelection()
        {
            foreach (var option in themeOptions)
                option.Label.Opacity = option.Value.Equals(globalInstance.ActiveReadTheme) ? 1 : 0;

            foreach (var option in themeOptions)
                option.Box.Stroke = option.Value.Equals(globalInstance.ActiveReadTheme) ? globalInstance.PrimaryText : globalInstance.Gray;
        }

        void UpdateFontSelection()
        {
            foreach (var option in fontOptions)
                SetOptionSelected(option.Box, option.Label, option.Value == globalInstance.ActiveFont);
        }

        void SetOptionSelected(Border box, Label label, bool isSelected)
        {
            box.BackgroundColor = isSelected ? globalInstance.Primary : Colors.Transparent;
            box.Stroke = isSelected ? globalInstance.Primary : globalInstance.Gray;
            label.TextColor = globalInstance.PrimaryText;
        }
    }
}
