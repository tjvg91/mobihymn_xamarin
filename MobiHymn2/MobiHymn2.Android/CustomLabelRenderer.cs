using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Android.Content;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Views;
using Android.Widget;
using AndroidX.Core.Content;
using MobiHymn2.Droid;
using MobiHymn2.Elements;
using Xamarin.Forms;
using Xamarin.Forms.Platform.Android;
using static Android.Views.GestureDetector;

[assembly: ExportRenderer(typeof(SelectableLabel), typeof(SelectableLabelRenderer))]
namespace MobiHymn2.Droid
{
    public class SelectableLabelRenderer : ViewRenderer<SelectableLabel, TextView>
    {
        TextView textView;

        public SelectableLabelRenderer(Context context) : base(context)
        {

        }

        private GravityFlags UpdateTextAlignment(Xamarin.Forms.TextAlignment alignment)
        {
            switch (alignment)
            {
                case Xamarin.Forms.TextAlignment.Center:
                    return GravityFlags.CenterHorizontal;
                case Xamarin.Forms.TextAlignment.End:
                    return GravityFlags.Right;
                case Xamarin.Forms.TextAlignment.Start:
                    return GravityFlags.Left;
                default:
                    return GravityFlags.Left;
            }
        }

        private void UpdateFont(SelectableLabel label)
        {
            Android.Graphics.Typeface t = new Regex("Roboto").IsMatch(label.FontFamily) ? null :
                Android.Graphics.Typeface.CreateFromAsset(Context.Assets, $"Fonts/{label.FontFamily}.ttf");

            switch (label.FontAttributes)
            {
                case FontAttributes.None:
                    textView.SetTypeface(t, Android.Graphics.TypefaceStyle.Normal);
                    break;
                case FontAttributes.Bold:
                    textView.SetTypeface(t, Android.Graphics.TypefaceStyle.Bold);
                    break;
                case FontAttributes.Italic:
                    textView.SetTypeface(t, Android.Graphics.TypefaceStyle.Italic);
                    break;
                default:
                    textView.SetTypeface(t, Android.Graphics.TypefaceStyle.Normal);
                    break;
            }
        }

        protected override void OnElementPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            base.OnElementPropertyChanged(sender, e);

            if (textView != null)
            {
                var label = (SelectableLabel)sender;
                switch (e.PropertyName)
                {
                    case "Text":
                        textView.Text = label.Text;
                        break;
                    case "TextColor":
                        textView.SetTextColor(label.TextColor.ToAndroid());
                        break;
                    case "BackgroundColor":
                        textView.Background = new ColorDrawable(label.BackgroundColor.ToAndroid());
                        break;
                    case "HorizontalTextAlignment":
                        textView.Gravity = UpdateTextAlignment(label.HorizontalTextAlignment);
                        break;
                    case "FontAttribute":
                    case "FontFamily":
                        UpdateFont(label);
                        break;
                    case "FontSize":
                        textView.TextSize = (float)label.FontSize;
                        break;
                }
            }
        }

        protected override void OnElementChanged(ElementChangedEventArgs<SelectableLabel> e)
        {
            base.OnElementChanged(e);

            var label = Element;
            if (label == null)
                return;

            if (Control == null)
            {
                textView = new TextView(this.Context);
            }

            textView.Enabled = true;
            textView.Focusable = true;
            textView.LongClickable = true;
            textView.Clickable = true;
            textView.SetTextIsSelectable(true);
            textView.SetHighlightColor(Color.FromHex("F5D200").ToAndroid());

            textView.Background = new ColorDrawable(label.BackgroundColor.ToAndroid());28;

            // Initial properties Set
            textView.Text = label.Text;
            textView.SetTextColor(label.TextColor.ToAndroid());
            textView.Gravity = UpdateTextAlignment(label.HorizontalTextAlignment);
            textView.TextSize = (float)label.FontSize;

            UpdateFont(label);


            SetNativeControl(textView);
        }
    }
}

