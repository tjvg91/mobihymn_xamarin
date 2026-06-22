using CommunityToolkit.Maui.Views;
using MobiHymn4.Elements;
using MobiHymn4.Utils;
using MobiHymn4.ViewModels;

namespace MobiHymn4.Views.Popups;

public partial class IntroPopup : Popup
{
    public string IntroResult { get; private set; }

    private readonly Globals globalInstance = Globals.Instance;
    private readonly AboutViewModel model;

    public IntroPopup()
    {
        InitializeComponent();

        var info = DeviceDisplay.MainDisplayInfo;
        var width = info.Width / info.Density;
        var height = info.Height / info.Density;
        Size = new Size(width, height);
        Color = globalInstance.PrimaryText;

        model = (AboutViewModel)BindingContext;
        model.IsLastIndexVisited += Model_IsLastIndexVisited;

        Opened += (_, _) => ShowSlide(0);
    }

    private void ShowSlide(int index)
    {
        var slides = model.IntroSlides;
        if (slides == null || index < 0 || index >= slides.Count)
            return;

        var slide = slides[index];
        introAnimation.Size = slide.Size;
        introAnimation.Source = slide.Image;
        introAnimation.RestartAnimation();
    }

    private void Model_IsLastIndexVisited(object sender, EventArgs e) =>
        btnSkipDone.Text = "Done";

    private void CarouselIntro_PositionChanged(object sender, PositionChangedEventArgs e)
    {
        model.CurrentSlideIndex = e.CurrentPosition;
        ShowSlide(e.CurrentPosition);
    }

    private void IntroAnimation_Swiped(object sender, SwipedEventArgs e)
    {
        var delta = e.Direction == SwipeDirection.Left ? 1 : -1;
        var nextPosition = carouselIntro.Position + delta;
        var slides = model.IntroSlides;

        if (slides == null || nextPosition < 0 || nextPosition >= slides.Count)
            return;

        carouselIntro.Position = nextPosition;
        model.CurrentSlideIndex = nextPosition;
        ShowSlide(nextPosition);
    }

    private void Button_Clicked(object sender, EventArgs e)
    {
        IntroResult = btnSkipDone.Text;
        Close(IntroResult);
    }
}
