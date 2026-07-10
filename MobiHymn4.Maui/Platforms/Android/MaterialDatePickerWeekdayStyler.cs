using Android.Graphics;
using Android.Widget;
using AndroidX.Lifecycle;
using AView = Android.Views.View;
using AViewGroup = Android.Views.ViewGroup;

namespace MobiHymn4.Platforms.Android;

static class MaterialDatePickerWeekdayStyler
{
    public static void ApplyDarkWeekdayStyles(AView? root)
    {
        if (root == null)
            return;

        if (TryStyleKnownWeekdayRow(root))
            return;

        if (root is AViewGroup group)
            WalkViewTree(group);
    }

    static bool TryStyleKnownWeekdayRow(AView root)
    {
        try
        {
            var rowId = root.Context.Resources?.GetIdentifier(
                "mtrl_calendar_day_of_week",
                "id",
                "com.google.android.material");
            if (rowId is > 0)
            {
                var row = root.FindViewById(rowId.Value);
                if (row != null)
                {
                    StyleWeekdayRow(row);
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    static void WalkViewTree(AViewGroup group)
    {
        for (var i = 0; i < group.ChildCount; i++)
        {
            var child = group.GetChildAt(i);
            if (child is AViewGroup childGroup && LooksLikeWeekdayRow(childGroup))
            {
                StyleWeekdayRow(childGroup);
                return;
            }

            if (child is AViewGroup nested)
                WalkViewTree(nested);
        }
    }

    static bool LooksLikeWeekdayRow(AViewGroup group)
    {
        if (group.ChildCount != 7)
            return false;

        for (var i = 0; i < 7; i++)
        {
            if (group.GetChildAt(i) is not TextView textView)
                return false;

            if (string.IsNullOrEmpty(textView.Text) || textView.Text.Length > 2)
                return false;
        }

        return true;
    }

    static void StyleWeekdayRow(AView row)
    {
        if (row is not AViewGroup group)
            return;

        for (var i = 0; i < group.ChildCount; i++)
        {
            if (group.GetChildAt(i) is not TextView textView)
                continue;

            textView.SetTextColor(global::Android.Graphics.Color.White);
            textView.SetTypeface(textView.Typeface, TypefaceStyle.Bold);
        }
    }

    public sealed class DarkModeObserver : Java.Lang.Object, ILifecycleEventObserver
    {
        public void OnStateChanged(ILifecycleOwner source, Lifecycle.Event e)
        {
            if (e != Lifecycle.Event.OnResume)
                return;

            var decorView = (source as Google.Android.Material.DatePicker.MaterialDatePicker)?.Dialog?.Window?.DecorView;
            decorView?.Post(() => ApplyDarkWeekdayStyles(decorView));
        }
    }
}
