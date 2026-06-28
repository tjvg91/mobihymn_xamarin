#if ANDROID
using Android.Graphics;
using Android.Text;
using Android.Text.Style;
using Android.Widget;
using AndroidX.Core.Text;
using Java.Lang;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using MobiHymn4.Elements;
using ATypeface = Android.Graphics.Typeface;

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
            [nameof(SelectableLabel.HorizontalTextAlignment)] = MapSelectableAlignment,
            [nameof(SelectableLabel.LineHeight)] = MapSelectableMetrics,
            [nameof(SelectableLabel.CharacterSpacing)] = MapSelectableMetrics,
            [nameof(SelectableLabel.BackgroundColor)] = MapSelectableBackground,
        };

    public SelectableLabelHandler() : base(SelectableLabelMapper) { }

    public static void MapSelectable(SelectableLabelHandler handler, SelectableLabel view) =>
        UpdatePlatformView(handler, view);

    public static void MapSelectableAlignment(SelectableLabelHandler handler, SelectableLabel view)
    {
        if (handler.PlatformView == null) return;
        LabelHandler.MapHorizontalTextAlignment(handler, view);
    }

    // Called only when LineHeight or CharacterSpacing changes — no need to re-parse HTML.
    public static void MapSelectableMetrics(SelectableLabelHandler handler, SelectableLabel view)
    {
        var textView = handler.PlatformView;
        if (textView == null) return;
        ApplySpacing(textView, view);
    }

    public static void MapSelectableBackground(SelectableLabelHandler handler, SelectableLabel view)
    {
        if (handler.PlatformView == null) return;
        handler.PlatformView.SetBackgroundColor(view.BackgroundColor.ToPlatform());
    }

    static void ConfigureSelection(TextView textView)
    {
        textView.SetTextIsSelectable(true);
        textView.SetHighlightColor(global::Android.Graphics.Color.ParseColor("#F5D200"));
        textView.Focusable = true;
        textView.LongClickable = true;
        textView.SetIncludeFontPadding(false);
    }

    static void UpdatePlatformView(SelectableLabelHandler handler, SelectableLabel view)
    {
        var textView = handler.PlatformView;
        if (textView == null)
            return;

        ConfigureSelection(textView);

        var html = view.Text ?? string.Empty;
        if (string.IsNullOrEmpty(html))
        {
            textView.Text = string.Empty;
            LabelHandler.MapFont(handler, view);
            LabelHandler.MapHorizontalTextAlignment(handler, view);
            LabelHandler.MapTextColor(handler, view);
            ApplySpacing(textView, view);
            return;
        }

        var parsed = HtmlCompat.FromHtml(html, HtmlCompat.FromHtmlModeLegacy);
        if (parsed == null)
        {
            textView.Text = string.Empty;
            return;
        }

        textView.SetText(parsed, TextView.BufferType.Spannable);

        LabelHandler.MapFont(handler, view);
        LabelHandler.MapHorizontalTextAlignment(handler, view);
        LabelHandler.MapTextColor(handler, view);

        ApplyUniformStyle(handler, textView, view);

        // Apply spacing last — after ApplyUniformStyle so MapFont inside it cannot reset them.
        ApplySpacing(textView, view);
    }

    // Single source of truth for spacing.
    // CharacterSpacing options are 0 / 0.5 / 1 in logical units; divide by 10 to map to
    // Android's em-based LetterSpacing so values stay in a readable 0–0.1 em range.
    static void ApplySpacing(TextView textView, SelectableLabel view)
    {
        if (view.LineHeight > 0)
            textView.SetLineSpacing(0, (float)view.LineHeight);

        textView.LetterSpacing = (float)view.CharacterSpacing / 10f;
    }

    static void ApplyUniformStyle(SelectableLabelHandler handler, TextView textView, SelectableLabel view)
    {
        var formatted = textView.TextFormatted;
        if (formatted is not ISpannable spannable || spannable.Length() == 0)
            return;

        var builder = new SpannableStringBuilder(spannable);
        var length = builder.Length();
        if (length == 0)
            return;

        RemoveStandardSpans(builder, length);

        var textColor = view.TextColor.ToPlatform();
        builder.SetSpan(new ForegroundColorSpan(textColor), 0, length, SpanTypes.InclusiveInclusive);

        if (textView.Typeface != null)
            builder.SetSpan(new CustomTypefaceSpan(textView.Typeface), 0, length, SpanTypes.InclusiveInclusive);

        textView.SetText(builder, TextView.BufferType.Spannable);
        textView.SetTextColor(textColor);
        LabelHandler.MapFont(handler, view);
    }

    static void RemoveStandardSpans(SpannableStringBuilder builder, int length)
    {
        TryRemoveSpans(builder, length, Class.FromType(typeof(AbsoluteSizeSpan)));
        TryRemoveSpans(builder, length, Class.FromType(typeof(RelativeSizeSpan)));
        TryRemoveSpans(builder, length, Class.FromType(typeof(ForegroundColorSpan)));
        TryRemoveSpans(builder, length, Class.FromType(typeof(StyleSpan)));
        TryRemoveSpans(builder, length, Class.FromType(typeof(TypefaceSpan)));
    }

    static void TryRemoveSpans(SpannableStringBuilder builder, int length, Class spanClass)
    {
        try
        {
            var spans = builder.GetSpans(0, length, spanClass);
            if (spans == null) return;
            foreach (var span in spans)
                builder.RemoveSpan(span);
        }
        catch (System.Exception) { }
    }

    sealed class CustomTypefaceSpan : MetricAffectingSpan
    {
        readonly ATypeface typeface;

        public CustomTypefaceSpan(ATypeface typeface) => this.typeface = typeface;

        public override void UpdateDrawState(TextPaint paint) => paint.SetTypeface(typeface);

        public override void UpdateMeasureState(TextPaint paint) => paint.SetTypeface(typeface);
    }
}
#endif
