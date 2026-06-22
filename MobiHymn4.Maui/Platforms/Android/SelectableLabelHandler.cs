#if ANDROID
using Android.Graphics.Drawables;
using Android.Widget;
using AndroidX.Core.Text;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using MobiHymn4.Elements;

namespace MobiHymn4.Handlers;

public class SelectableLabelHandler : LabelHandler
{
    public static IPropertyMapper<SelectableLabel, SelectableLabelHandler> SelectableLabelMapper =
        new PropertyMapper<SelectableLabel, SelectableLabelHandler>(LabelHandler.Mapper)
        {
            [nameof(SelectableLabel.Text)] = MapSelectable,
            [nameof(SelectableLabel.TextColor)] = MapSelectable,
            [nameof(SelectableLabel.FontFamily)] = MapSelectable,
            [nameof(SelectableLabel.FontSize)] = MapSelectable,
            [nameof(SelectableLabel.FontAttributes)] = MapSelectable,
            [nameof(SelectableLabel.HorizontalTextAlignment)] = MapSelectable,
            [nameof(SelectableLabel.LineHeight)] = MapSelectable,
            [nameof(SelectableLabel.CharacterSpacing)] = MapSelectable,
            [nameof(SelectableLabel.BackgroundColor)] = MapSelectableBackground,
        };

    public SelectableLabelHandler() : base(SelectableLabelMapper) { }

    public static void MapSelectable(SelectableLabelHandler handler, SelectableLabel view) =>
        UpdatePlatformView(handler, view);

    public static void MapSelectableBackground(SelectableLabelHandler handler, SelectableLabel view)
    {
        if (handler.PlatformView == null)
            return;

        handler.PlatformView.Background = new ColorDrawable(view.BackgroundColor.ToPlatform());
    }

    static void ConfigureSelection(TextView textView)
    {
        textView.SetTextIsSelectable(true);
        textView.SetHighlightColor(global::Android.Graphics.Color.ParseColor("#F5D200"));
        textView.Focusable = true;
        textView.LongClickable = true;
    }

    static void UpdatePlatformView(SelectableLabelHandler handler, SelectableLabel view)
    {
        var textView = handler.PlatformView;
        if (textView == null)
            return;

        ConfigureSelection(textView);
        LabelHandler.MapFont(handler, view);
        LabelHandler.MapHorizontalTextAlignment(handler, view);

        if (view.LineHeight > 0)
            textView.SetLineSpacing(0, (float)view.LineHeight);

        if (view.CharacterSpacing != 0)
            textView.LetterSpacing = (float)view.CharacterSpacing;

        var html = view.Text ?? string.Empty;
        if (string.IsNullOrEmpty(html))
        {
            textView.Text = string.Empty;
        }
        else
        {
            var spanned = HtmlCompat.FromHtml(html, HtmlCompat.FromHtmlModeLegacy);
            textView.SetText(spanned, TextView.BufferType.Spannable);
        }

        textView.SetTextColor(view.TextColor.ToPlatform());
    }
}
#endif
