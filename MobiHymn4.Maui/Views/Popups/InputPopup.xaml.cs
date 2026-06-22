using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using MobiHymn4.Utils;
using MobiHymn4.ViewModels;
using CommunityToolkit.Maui.Views;

using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;

namespace MobiHymn4.Views.Popups
{
    public partial class InputPopup : Popup, INotifyPropertyChanged
    {
        private InputPopupModel model;

        public string Title
        {
            get => model.Title;
            set => model.Title = value;
        }

        public string ActionString
        {
            get => model.ActionString;
            set => model.ActionString = value;
        }

        private Func<string, string> validation;
        public Func<string, string> Validation
        {
            get => validation;
            set => validation = value;
        }

        public event EventHandler OK;
        public event EventHandler Cancel;

        public InputPopup()
        {
            InitializeComponent();

            model = (InputPopupModel)BindingContext;
            model.ErrorString = "";

            Opened += InputPopup_Opened;
            entInput.HandlerChanged += Input_HandlerChanged;
            groupPicker.HandlerChanged += Input_HandlerChanged;
        }

        public void SetGroups(IEnumerable<string> groupNames, string defaultGroup = null)
        {
            var names = groupNames?
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            if (names.Count == 0)
                names.Add("General");

            model.SetGroupOptions(names);

            var selected = !string.IsNullOrWhiteSpace(defaultGroup) && names.Any(name => name.Equals(defaultGroup, StringComparison.OrdinalIgnoreCase))
                ? names.First(name => name.Equals(defaultGroup, StringComparison.OrdinalIgnoreCase))
                : names[0];
            model.SelectedGroup = selected;

            if (groupPicker != null)
            {
                groupPicker.SelectedItem = selected;
                groupPicker.ItemsSource = model.GroupOptions;
            }
        }

        void Input_HandlerChanged(object sender, EventArgs e) => ApplyInputAccents();

        void InputPopup_Opened(object sender, EventArgs e)
        {
            var display = DeviceDisplay.MainDisplayInfo;
            Size = new Size(display.Width / display.Density, display.Height / display.Density);

            ApplyInputAccents();

            if (groupPicker != null && model.GroupOptions.Count > 0)
            {
                groupPicker.ItemsSource = model.GroupOptions;
                groupPicker.SelectedItem = model.SelectedGroup ?? model.GroupOptions[0];
            }

            entInput?.Focus();
        }

        void ApplyInputAccents()
        {
            var accent = (Color)Application.Current.Resources["Primary"];

#if ANDROID
            var androidColor = accent.ToPlatform();
            var tint = Android.Content.Res.ColorStateList.ValueOf(androidColor);

            if (entInput?.Handler?.PlatformView is Android.Widget.EditText editText)
            {
                editText.Background = null;
                editText.SetBackgroundColor(Android.Graphics.Color.Transparent);
                editText.SetHighlightColor(androidColor);
                editText.BackgroundTintList = tint;
            }

            if (groupPicker?.Handler?.PlatformView is Android.Views.View pickerView)
            {
                pickerView.BackgroundTintList = tint;
                if (pickerView is Android.Widget.EditText pickerEditText)
                {
                    pickerEditText.SetHighlightColor(androidColor);
                }
            }
#endif
        }

        void Overlay_Tapped(object sender, EventArgs e) => Dismiss();

        void btnOK_Clicked(object sender, EventArgs e)
        {
            var isNewGroup = !string.IsNullOrWhiteSpace(entInput.Text);
            string val;
            if (isNewGroup)
            {
                val = entInput.Text.Trim();
            }
            else if (groupPicker?.SelectedItem is string selectedGroup)
            {
                val = selectedGroup;
            }
            else
            {
                val = string.IsNullOrWhiteSpace(model.SelectedGroup) ? "General" : model.SelectedGroup;
            }

            if (isNewGroup)
                model.ErrorString = Validation?.Invoke(val) ?? "";
            else
                model.ErrorString = "";

            if (!isNewGroup || Validation == null || model.ErrorString == "")
            {
                OK?.Invoke(val, EventArgs.Empty);
                Close(val);
            }
        }

        void btnCancel_Clicked(object sender, EventArgs e)
        {
            Dismiss();
        }

        void Dismiss()
        {
            string val = entInput.Text;
            Cancel?.Invoke(val, EventArgs.Empty);
            Close(val);
        }
    }
}
