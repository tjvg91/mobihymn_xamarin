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
using AndroidX.Core.Text;
using MobiHymn4.Droid;
using MobiHymn4.Elements;
using Xamarin.Forms;
using Xamarin.Forms.Platform.Android;

[assembly: ExportRenderer(typeof(SelectableLabel), typeof(SelectableLabelRenderer))]
namespace MobiHymn4.Droid
{
    public class SelectableLabelRenderer : LabelRenderer, IGestureRecognizer
    {
        public SelectableLabelRenderer(Context context) : base(context)
        {
            
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private Android.Views.TextAlignment UpdateTextAlignment(Xamarin.Forms.TextAlignment alignment)
        {
            switch (alignment)
            {
                case Xamarin.Forms.TextAlignment.Center:
                    return Android.Views.TextAlignment.Center;
                case Xamarin.Forms.TextAlignment.End:
                    return Android.Views.TextAlignment.TextEnd;
                case Xamarin.Forms.TextAlignment.Start:
                    return Android.Views.TextAlignment.TextStart;
                default:
                    return Android.Views.TextAlignment.TextStart;
            }
        }

        private void UpdateFont(Label label)
        {
            Android.Graphics.Typeface t = new Regex("Roboto").IsMatch(label.FontFamily) ? null :
                Android.Graphics.Typeface.CreateFromAsset(Context.Assets, $"Fonts/{label.FontFamily}.ttf");

            switch (label.FontAttributes)
            {
                case FontAttributes.None:
                    Control.SetTypeface(t, Android.Graphics.TypefaceStyle.Normal);
                    break;
                case FontAttributes.Bold:
                    Control.SetTypeface(t, Android.Graphics.TypefaceStyle.Bold);
                    break;
                case FontAttributes.Italic:
                    Control.SetTypeface(t, Android.Graphics.TypefaceStyle.Italic);
                    break;
                default:
                    Control.SetTypeface(t, Android.Graphics.TypefaceStyle.Normal);
                    break;
            }
        }

        protected override void OnElementPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            base.OnElementPropertyChanged(sender, e);

            if (Control != null)
            {
                var label = (SelectableLabel)sender;
                switch (e.PropertyName)
                {
                    case "Text":
                        Control.Text = label.Text;
                        break;
                    case "TextColor":
                        Control.SetTextColor(label.TextColor.ToAndroid());
                        break;
                    case "BackgroundColor":
                        Control.Background = new ColorDrawable(label.BackgroundColor.ToAndroid());
                        break;
                    case "HorizontalTextAlignment":
                        Control.TextAlignment = UpdateTextAlignment(label.HorizontalTextAlignment);
                        break;
                    case "FontAttribute":
                    case "FontFamily":
                        UpdateFont(label);
                        break;
                    case "FontSize":
                        Control.TextSize = (float)label.FontSize;
                        break;
                    case "CharacterSpacing":
                        Control.LetterSpacing = (float)label.CharacterSpacing;
                        break;
                    case "LineHeight":
                        Control.SetLineSpacing((float)label.LineHeight, 1f);
                        break;
                }

                if (new Regex("TextColor|Font").IsMatch(e.PropertyName))
                {
                    Control.SetText(
                        HtmlCompat.FromHtml(
                            Control.Text.ToString(), HtmlCompat.FromHtmlModeLegacy),
                        TextView.BufferType.Spannable);
                }
            }
        }

        protected override void OnElementChanged(ElementChangedEventArgs<Label> e)
        {
            base.OnElementChanged(e);

            var label = e.NewElement;
            if (label == null)
                return;

            //Control.Enabled = true;
            //Control.Focusable = true;
            //Control.LongClickable = true;
            //Control.Clickable = true;

            Control.SetTextIsSelectable(true);
            Control.SetHighlightColor(Color.FromHex("F5D200").ToAndroid());

            Control.Background = new ColorDrawable(label.BackgroundColor.ToAndroid());

            // Initial properties Set
            Control.Text = label.Text;
            Control.SetTextColor(label.TextColor.ToAndroid());
            Control.TextAlignment = UpdateTextAlignment(label.HorizontalTextAlignment);
            Control.TextSize = (float)label.FontSize;
            Control.LetterSpacing = (float)label.CharacterSpacing;

            Control.SetText(
                HtmlCompat.FromHtml(
                    Control.Text.ToString(), HtmlCompat.FromHtmlModeLegacy),
                TextView.BufferType.Spannable);

            UpdateFont(label);

            SetNativeControl(Control);
        }
    }
}

