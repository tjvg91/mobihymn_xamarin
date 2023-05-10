using System;
using System.ComponentModel;
using System.Text.RegularExpressions;
using Foundation;
using MobiHymn2.Elements;
using MobiHymn2.iOS;
using UIKit;
using Xamarin.Forms;
using Xamarin.Forms.Platform.iOS;

[assembly: ExportRenderer(typeof(SelectableLabel), typeof(SelectableLabelRenderer))]
namespace MobiHymn2.iOS
{
    public class SelectableLabelRenderer : ViewRenderer<SelectableLabel, UITextView>
    {
        UITextView uiTextView;

        private UITextAlignment UpdateTextAlignment(TextAlignment alignment)
        {
            switch (alignment)
            {
                case TextAlignment.Center:
                    return UITextAlignment.Center;
                case TextAlignment.End:
                    return UITextAlignment.Right;
                case TextAlignment.Start:
                    return UITextAlignment.Left;
                default:
                    return UITextAlignment.Left;
            }
        }

        private UIFont UpdateFontAttribute(SelectableLabel label)
        {
            string fontFamily = new Regex("ChelseaMarket|StyleScript").IsMatch(label.FontFamily)
                                ? $"{label.FontFamily}-Regular" : label.FontFamily;
            UIFont font = UIFont.FromName(fontFamily, new nfloat(label.FontSize));

            switch (label.FontAttributes)
            {
                case FontAttributes.None:
                    return font ?? UIFont.SystemFontOfSize(new nfloat(label.FontSize));
                case FontAttributes.Bold:
                    return UIFont.FromName($"{fontFamily}-Bold", new nfloat(label.FontSize));
                case FontAttributes.Italic:
                    return UIFont.FromName($"{fontFamily}-Italic", new nfloat(label.FontSize));
                default:
                    return UIFont.FromName(fontFamily, new nfloat(label.FontSize));
            }
        }

        protected override void OnElementPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            base.OnElementPropertyChanged(sender, e);

            if(uiTextView != null)
            {
                SelectableLabel label = (SelectableLabel)sender;
                switch (e.PropertyName)
                {
                    case "Text":
                        uiTextView.Text = label.Text;
                        break;
                    case "TextColor":
                        uiTextView.TextColor = label.TextColor.ToUIColor();
                        break;
                    case "HorizontalTextAlignment":
                        uiTextView.TextAlignment = UpdateTextAlignment(label.HorizontalTextAlignment);
                        break;
                    case "FontAttribute":
                    case "FontSize":
                    case "FontFamily":
                        uiTextView.Font = UpdateFontAttribute(label);
                        break;
                }
            }
        }

        protected override void OnElementChanged(ElementChangedEventArgs<SelectableLabel> e)
        {
            base.OnElementChanged(e);

            var label = (SelectableLabel)Element;
            if (label == null)
                return;

            if (Control == null)
            {
                uiTextView = new UITextView();
            }
            uiTextView.Selectable = true;
            uiTextView.Editable = false;
            uiTextView.ScrollEnabled = false;
            uiTextView.TextContainerInset = UIEdgeInsets.Zero;
            uiTextView.TextContainer.LineFragmentPadding = 0;
            uiTextView.BackgroundColor = UIColor.Clear;

            // Initial properties Set
            uiTextView.Text = label.Text;
            uiTextView.TextColor = label.TextColor.ToUIColor();
            uiTextView.TextAlignment = UpdateTextAlignment(label.HorizontalTextAlignment);
            uiTextView.Font = UpdateFontAttribute(label);

            var attr = new NSAttributedStringDocumentAttributes();
            var nsError = new NSError();
            attr.DocumentType = NSDocumentType.HTML;

            uiTextView.AttributedText = new NSAttributedString(label.Text, attr, ref nsError);

            SetNativeControl(uiTextView);
        }

        /*private void FixFontAtLocation(int location, NSMutableAttributedString text, string fontFamily, FontAttributes fontAttributes)
        {
            if (fontFamily == null)
                return;

            NSRange range;
            var font = (UIFont)text.GetAttribute(UIStringAttributeKey.Font, location, out range);
            var baseFontName = GetBaseFontName(fontFamily);

            if (font.Name.Contains("-") && font.Name.StartsWith(baseFontName))
                return;

            var newName = GetFontName(fontFamily, fontAttributes);
            font = UIFont.FromName(newName, font.PointSize);
            text.RemoveAttribute(UIStringAttributeKey.Font, range);
            text.AddAttribute(UIStringAttributeKey.Font, font, range);
        }

        private void UpdateFormattedText()
        {
            var text = Control?.AttributedText as NSMutableAttributedString;
            if (text == null)
                return;

            var fontFamily = Element.FontFamily;
            text.BeginEditing();
            if (Element.FormattedText == null)
            {
                FixFontAtLocation(0, text, fontFamily, Element.FontAttributes);
            }
            else
            {
                var location = 0;
                foreach (var span in Element.FormattedText.Spans)
                {
                    var spanFamily = span.FontFamily ?? fontFamily;
                    FixFontAtLocation(location, text, spanFamily, span.FontAttributes);
                    location += span.Text.Length;
                }
            }
            text.EndEditing();
        }*/
    }
}

