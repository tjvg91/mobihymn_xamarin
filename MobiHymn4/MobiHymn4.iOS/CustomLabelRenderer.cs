using System;
using System.ComponentModel;
using System.Text.RegularExpressions;
using Foundation;
using MobiHymn4.Elements;
using MobiHymn4.iOS;
using UIKit;
using Xamarin.Forms;
using Xamarin.Forms.Platform.iOS;

[assembly: ExportRenderer(typeof(SelectableLabel), typeof(SelectableLabelRenderer))]
namespace MobiHymn4.iOS
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

            var mutable = new NSMutableAttributedString(new NSAttributedString(label.Text, attr, ref nsError));

            UIStringAttributes uiString = new UIStringAttributes()
            {
                Font = uiTextView.Font,
                ForegroundColor = uiTextView.TextColor,
                KerningAdjustment = Convert.ToSingle(label.CharacterSpacing),
            };

            NSMutableParagraphStyle paragrahStyle = new NSMutableParagraphStyle()
            {
                Alignment = uiTextView.TextAlignment,
                LineSpacing = new nfloat(label.LineHeight),
            };
            uiString.ParagraphStyle = paragrahStyle;

            mutable.SetAttributes(uiString, new NSRange(0, mutable.Length));
            uiTextView.AttributedText = mutable;

            SetNativeControl(uiTextView);
        }
    }
}

