using System;
using System.Collections.Generic;
using MobiHymn4.Models;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;


namespace MobiHymn4.Views.Popups
{	
	public partial class ToastPopup : Popup
	{
        public string PopupAnim
        {
            get => (string)GetValue(PopupAnimProperty);
            set => SetValue(PopupAnimProperty, value);
        }

        public string PopupLabel
        {
            get => (string)GetValue(PopupLabelProperty);
            set => SetValue(PopupLabelProperty, value);
        }

        public double PopupAnimSize
        {
            get => (double)GetValue(PopupAnimSizeProperty);
            set => SetValue(PopupAnimSizeProperty, value);
        }

        public Rect LayoutBounds
        {
            get => (Rect)GetValue(LayoutBoundsProperty);
            set => SetValue(LayoutBoundsProperty, value);
        }

        public ToastPopup ()
		{
			InitializeComponent();
            UpdatePopupSize(PopupAnimSize);
        }

        void UpdatePopupSize(double animSize)
        {
            var size = animSize > 1 ? animSize : 120;
            Size = new Size(Math.Max(180, size + 48), size + 84);
        }

        public static readonly BindableProperty PopupAnimProperty = BindableProperty.Create(
                                                         propertyName: nameof(PopupAnim),
                                                         returnType: typeof(string),
                                                         declaringType: typeof(ToastPopup),
                                                         defaultValue: null,
                                                         defaultBindingMode: BindingMode.TwoWay,
                                                         propertyChanged: PopupAnimChanged);

        public static readonly BindableProperty PopupLabelProperty = BindableProperty.Create(
                                                         propertyName: nameof(PopupLabel),
                                                         returnType: typeof(string),
                                                         declaringType: typeof(ToastPopup),
                                                         defaultValue: null,
                                                         defaultBindingMode: BindingMode.TwoWay,
                                                         propertyChanged: PopupLabelChanged);

        public static readonly BindableProperty PopupAnimSizeProperty = BindableProperty.Create(
                                                         propertyName: nameof(PopupAnimSize),
                                                         returnType: typeof(double),
                                                         declaringType: typeof(ToastPopup),
                                                         defaultValue: DeviceInfo.Platform == DevicePlatform.Android ?
                                                                        Double.Parse("150") : Double.Parse("1"),
                                                         defaultBindingMode: BindingMode.TwoWay,
                                                         propertyChanged: PopupAnimSizeChanged);

        public static readonly BindableProperty LayoutBoundsProperty = BindableProperty.Create(
                                                         propertyName: nameof(LayoutBounds),
                                                         returnType: typeof(Rect),
                                                         declaringType: typeof(ToastPopup),
                                                         defaultValue: DeviceInfo.Platform == DevicePlatform.Android ?
                                                                        new Rect(0.5, 0, 2, 2) : new Rect(0.8, 0.8, 1, 1),
                                                         defaultBindingMode: BindingMode.TwoWay,
                                                         propertyChanged: LayoutBoundsChanged);

        private static void PopupAnimChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (newValue != null && newValue.Equals(oldValue))
            {
                var control = (ToastPopup)bindable;
                control.PopupAnim = (string)newValue;
            }
        }

        private static void PopupLabelChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (newValue != null && newValue.Equals(oldValue))
            {
                var control = (ToastPopup)bindable;
                control.PopupLabel = (string)newValue;
            }
        }

        private static void PopupAnimSizeChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (newValue is double size && bindable is ToastPopup control)
                control.UpdatePopupSize(size);
        }

        private static void LayoutBoundsChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (newValue != null && newValue.Equals(oldValue))
            {
                var control = (ToastPopup)bindable;
                control.LayoutBounds = (Rect)newValue;
            }
        }
    }
}

