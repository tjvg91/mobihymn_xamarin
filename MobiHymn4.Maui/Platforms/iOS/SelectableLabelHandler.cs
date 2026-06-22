#if IOS
using System.Text.RegularExpressions;
using Foundation;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using MobiHymn4.Elements;
using UIKit;

namespace MobiHymn4.Handlers;

public class SelectableLabelHandler : ViewHandler<SelectableLabel, UITextView>
{
    public static IPropertyMapper<SelectableLabel, SelectableLabelHandler> SelectableLabelMapper =
        new PropertyMapper<SelectableLabel, SelectableLabelHandler>(ViewMapper)
        {
            [nameof(SelectableLabel.Text)] = MapContent,
            [nameof(SelectableLabel.TextColor)] = MapContent,
            [nameof(SelectableLabel.FontFamily)] = MapContent,
            [nameof(SelectableLabel.FontSize)] = MapContent,
            [nameof(SelectableLabel.FontAttributes)] = MapContent,
            [nameof(SelectableLabel.HorizontalTextAlignment)] = MapContent,
            [nameof(SelectableLabel.LineHeight)] = MapContent,
            [nameof(SelectableLabel.CharacterSpacing)] = MapContent,
        };

    public SelectableLabelHandler() : base(SelectableLabelMapper) { }

    protected override UITextView CreatePlatformView()
    {
        var textView = new UITextView
        {
            Selectable = true,
            Editable = false,
            ScrollEnabled = false,
            BackgroundColor = UIColor.Clear,
        };
        textView.TextContainerInset = UIEdgeInsets.Zero;
        textView.TextContainer.LineFragmentPadding = 0;
        return textView;
    }

    public static void MapContent(SelectableLabelHandler handler, SelectableLabel view) =>
        handler.UpdateContent(view);

    void UpdateContent(SelectableLabel label)
    {
        if (PlatformView == null)
            return;

        var textView = PlatformView;
        var font = ResolveFont(label);
        var textColor = label.TextColor.ToPlatform();
        var alignment = label.HorizontalTextAlignment.ToPlatformHorizontal();

        textView.Font = font;
        textView.TextColor = textColor;
        textView.TextAlignment = alignment;

        var html = label.Text ?? string.Empty;
        if (string.IsNullOrEmpty(html))
        {
            textView.Text = string.Empty;
            return;
        }

        var data = NSData.FromString(html, NSStringEncoding.UTF8);
        if (data == null)
        {
            textView.Text = html;
            return;
        }

        NSError error = null;
        var options = new NSAttributedStringDocumentAttributes
        {
            DocumentType = NSDocumentType.HTML,
            StringEncoding = NSStringEncoding.UTF8,
        };

        var parsed = new NSAttributedString(data, options, ref error);
        if (parsed == null || parsed.Length == 0)
        {
            textView.Text = html;
            return;
        }

        var mutable = new NSMutableAttributedString(parsed);
        var range = new NSRange(0, mutable.Length);
        var paragraph = new NSMutableParagraphStyle
        {
            Alignment = alignment,
            LineSpacing = (nfloat)Math.Max(0, label.LineHeight - 1),
        };

        mutable.AddAttribute(UIStringAttributeKey.Font, font, range);
        mutable.AddAttribute(UIStringAttributeKey.ForegroundColor, textColor, range);
        mutable.AddAttribute(UIStringAttributeKey.ParagraphStyle, paragraph, range);

        if (label.CharacterSpacing != 0)
            mutable.AddAttribute(UIStringAttributeKey.Kern, NSNumber.FromFloat((float)label.CharacterSpacing), range);

        textView.AttributedText = mutable;
    }

    static UIFont ResolveFont(SelectableLabel label)
    {
        var family = label.FontFamily ?? string.Empty;
        if (string.IsNullOrEmpty(family))
            return UIFont.SystemFontOfSize((nfloat)label.FontSize);

        if (Regex.IsMatch(family, "ChelseaMarket|StyleScript"))
            family = $"{family}-Regular";

        var font = UIFont.FromName(family, (nfloat)label.FontSize);
        if (font != null)
            return font;

        return label.FontAttributes switch
        {
            FontAttributes.Bold => UIFont.BoldSystemFontOfSize((nfloat)label.FontSize),
            FontAttributes.Italic => UIFont.ItalicSystemFontOfSize((nfloat)label.FontSize),
            _ => UIFont.SystemFontOfSize((nfloat)label.FontSize),
        };
    }
}
#endif
