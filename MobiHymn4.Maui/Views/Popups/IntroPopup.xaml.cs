using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Maui.Views;
using MobiHymn4.Elements;
using MobiHymn4.Utils;
using MobiHymn4.ViewModels;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
#if ANDROID
using AndroidX.Core.View;
#endif

namespace MobiHymn4.Views.Popups;

public partial class IntroPopup : Popup
{
    public string IntroResult { get; private set; }

    private readonly Globals globalInstance = Globals.Instance;
    private readonly AboutViewModel model;

    public IntroPopup()
    {
        InitializeComponent();

        Color = globalInstance.PrimaryText;
        model = (AboutViewModel)BindingContext;
        model.IsLastIndexVisited += Model_IsLastIndexVisited;

        Opened += (_, _) =>
        {
            ApplyPopupSizeAndInsets();
            UpdateBottomButtons();
            RestartCurrentSlideAnimation();
        };
    }

    void ApplyPopupSizeAndInsets()
    {
        var info = DeviceDisplay.MainDisplayInfo;
        var width = info.Width / info.Density;
        var height = info.Height / info.Density;

        var window = Application.Current?.Windows?.FirstOrDefault();
        if (window is { Width: > 0, Height: > 0 })
        {
            width = window.Width;
            height = window.Height;
        }

        Size = new Size(width, height);

        // Lift Skip/Next/Done above the Android tablet taskbar / system nav.
        var bottomInset = GetBottomSafeInset();
        bottomBar.Padding = new Thickness(0, 12, 0, 16 + bottomInset);
    }

    static double GetBottomSafeInset()
    {
#if ANDROID
        try
        {
            var activity = Platform.CurrentActivity;
            var root = activity?.Window?.DecorView;
            if (root != null)
            {
                var insets = ViewCompat.GetRootWindowInsets(root);
                if (insets != null)
                {
                    var bars = insets.GetInsets(WindowInsetsCompat.Type.SystemBars());
                    var density = DeviceDisplay.MainDisplayInfo.Density;
                    var dp = bars.Bottom / density;
                    if (dp > 0)
                        return dp;
                }
            }
        }
        catch
        {
        }

        // Pixel Tablet / large-screen taskbar is often ~48–72dp when insets aren't ready yet.
        return DeviceInfo.Idiom == DeviceIdiom.Tablet ? 72 : 24;
#else
        return DeviceInfo.Idiom == DeviceIdiom.Tablet ? 24 : 8;
#endif
    }

    private void Model_IsLastIndexVisited(object sender, EventArgs e) =>
        UpdateBottomButtons();

    void UpdateBottomButtons()
    {
        var last = model.IsLastIndex;
        btnSkipDone.Text = last ? "Done" : "Next";
        btnSkip.IsVisible = !last;
    }

    private void CarouselIntro_PositionChanged(object sender, PositionChangedEventArgs e)
    {
        model.CurrentSlideIndex = e.CurrentPosition;
        UpdateBottomButtons();
        RestartCurrentSlideAnimation();
    }

    private void CarouselIntro_CurrentItemChanged(object sender, CurrentItemChangedEventArgs e) =>
        RestartCurrentSlideAnimation();

    void RestartCurrentSlideAnimation()
    {
        var slide = model.IntroSlides?.ElementAtOrDefault(model.CurrentSlideIndex);
        if (slide == null)
            return;

        // Carousel keeps neighbors alive — restart only the current slide's animation.
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(50);
            FindAnimatingViewForSlide(carouselIntro, slide)?.RestartAnimation();
        });
    }

    static AnimatingView FindAnimatingViewForSlide(Element root, IntroSlide slide)
    {
        if (root is AnimatingView av
            && (ReferenceEquals(av.BindingContext, slide)
                || (av.BindingContext is IntroSlide bound
                    && string.Equals(bound.Image, slide.Image, StringComparison.Ordinal))))
            return av;

        if (root is not IVisualTreeElement tree)
            return null;

        foreach (var child in tree.GetVisualChildren())
        {
            if (child is not Element element)
                continue;
            var found = FindAnimatingViewForSlide(element, slide);
            if (found != null)
                return found;
        }

        return null;
    }

    private void Skip_Clicked(object sender, EventArgs e)
    {
        IntroResult = "Skip";
        Close(IntroResult);
    }

    private void Button_Clicked(object sender, EventArgs e)
    {
        if (!model.IsLastIndex)
        {
            var next = carouselIntro.Position + 1;
            var slides = model.IntroSlides;
            if (slides != null && next < slides.Count)
            {
                carouselIntro.Position = next;
                model.CurrentSlideIndex = next;
                UpdateBottomButtons();
            }
            return;
        }

        IntroResult = "Done";
        Close(IntroResult);
    }
}
