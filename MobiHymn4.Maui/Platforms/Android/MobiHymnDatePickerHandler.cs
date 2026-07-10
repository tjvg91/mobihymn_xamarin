using Google.Android.Material.DatePicker;
using AndroidX.AppCompat.App;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace MobiHymn4.Platforms.Android;

public sealed class MobiHymnDatePickerHandler : DatePickerHandler
{
    MaterialDatePicker? materialPicker;

    protected override MauiDatePicker CreatePlatformView()
    {
        var platformView = base.CreatePlatformView();
        platformView.ShowPicker = ShowMaterialPicker;
        platformView.HidePicker = HideMaterialPicker;
        return platformView;
    }

    void ShowMaterialPicker()
    {
        if (VirtualView == null)
            return;

        var activity = Platform.CurrentActivity as AppCompatActivity;
        var fragmentManager = activity?.SupportFragmentManager;
        if (fragmentManager == null)
            return;

        const string tag = "MobiHymnDatePicker";
        if (fragmentManager.FindFragmentByTag(tag) != null)
            return;

        var date = VirtualView.Date;
        var theme = Microsoft.Maui.Controls.Application.Current?.RequestedTheme == AppTheme.Dark
            ? Resource.Style.ThemeOverlay_MobiHymn_DatePicker_Dark
            : Resource.Style.ThemeOverlay_MobiHymn_DatePicker_Light;

        var builder = MaterialDatePicker.Builder.DatePicker()
            .SetTheme(theme)
            .SetSelection(ToUtcMilliseconds(date));

        var constraints = BuildConstraints(VirtualView);
        if (constraints != null)
            builder.SetCalendarConstraints(constraints);

        materialPicker = builder.Build();
        materialPicker.AddOnPositiveButtonClickListener(new PositiveClickListener(VirtualView));
        if (theme == Resource.Style.ThemeOverlay_MobiHymn_DatePicker_Dark)
            materialPicker.Lifecycle.AddObserver(new MaterialDatePickerWeekdayStyler.DarkModeObserver());
        materialPicker.Show(fragmentManager, tag);
    }

    void HideMaterialPicker()
    {
        materialPicker?.DismissAllowingStateLoss();
        materialPicker = null;
    }

    static CalendarConstraints? BuildConstraints(IDatePicker virtualView)
    {
        var min = virtualView.MinimumDate;
        var max = virtualView.MaximumDate;
        var hasMin = min > DateTime.MinValue.AddYears(1);
        var hasMax = max < DateTime.MaxValue.AddYears(-1);
        if (!hasMin && !hasMax)
            return null;

        var builder = new CalendarConstraints.Builder();
        if (hasMin)
            builder.SetStart(ToUtcMilliseconds(min.Date));
        if (hasMax)
            builder.SetEnd(ToUtcMilliseconds(max.Date));

        return builder.Build();
    }

    static long ToUtcMilliseconds(DateTime date)
    {
        var utc = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        return new DateTimeOffset(utc).ToUnixTimeMilliseconds();
    }

    sealed class PositiveClickListener : Java.Lang.Object, IMaterialPickerOnPositiveButtonClickListener
    {
        readonly IDatePicker virtualView;

        public PositiveClickListener(IDatePicker virtualView) =>
            this.virtualView = virtualView;

        public void OnPositiveButtonClick(Java.Lang.Object? selection)
        {
            if (selection is not Java.Lang.Long value)
                return;

            virtualView.Date = DateTimeOffset.FromUnixTimeMilliseconds(value.LongValue()).UtcDateTime.Date;
        }
    }
}
