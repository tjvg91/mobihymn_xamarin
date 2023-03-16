using System;
using System.Collections.Generic;
using Lottie.Forms;
using Xamarin.Forms;

namespace MobiHymn2.Elements
{
    public partial class AnimatingView : ContentView
    {
        public AnimatingView()
        {
            InitializeComponent();
        }

        public static readonly BindableProperty SizeProperty = BindableProperty.Create
            (nameof(Size), typeof(double), typeof(AnimatingView), 0.0, BindingMode.OneWay, propertyChanged: SizePropertyChanged);
        public static readonly BindableProperty SourceProperty = BindableProperty.Create
            (nameof(Source), typeof(string), typeof(AnimatingView), "", BindingMode.OneWay, propertyChanged: SourcePropertyChanged);
        public static readonly BindableProperty SpeedProperty = BindableProperty.Create
            (nameof(Speed), typeof(float), typeof(AnimatingView), 1f, BindingMode.OneWay, propertyChanged: SpeedPropertyChanged);

        public double Size
        {
            get => (double)GetValue(SizeProperty);
            set => SetValue(SizeProperty, value);
        }

        public string Source
        {
            get => (string)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        public float Speed
        {
            get => (float)GetValue(SpeedProperty);
            set => SetValue(SpeedProperty, value);
        }

        private static void SizePropertyChanged(BindableObject bindable, object oldValue, object newValue)
        {
            var control = (AnimatingView)bindable;
            var val = (double)newValue;
            switch (Device.RuntimePlatform)
            {
                case Device.iOS:
                case Device.macOS:
                    control.gif.Scale = val;
                    break;
                default:
                    control.animation.WidthRequest = control.animation.HeightRequest = val;
                    break;
            }
        }

        private static void SourcePropertyChanged(BindableObject bindable, object oldValue, object newValue)
        {
            var control = (AnimatingView)bindable;
            var val = (string)newValue;
            switch (Device.RuntimePlatform)
            {
                case Device.iOS:
                case Device.macOS:
                    control.gif.Source = $"resource://MobiHymn2.Resources.GIF.{val}.gif";
                    break;
                default:
                    control.animation.Animation = $"{val}.json";
                    break;
            }
        }

        private static void SpeedPropertyChanged(BindableObject bindable, object oldValue, object newValue)
        {
            var control = (AnimatingView)bindable;
            var val = (float)newValue;
            switch (Device.RuntimePlatform)
            {
                case Device.Android:
                    control.animation.Speed = val;
                    break;
            }
        }
    }
}

