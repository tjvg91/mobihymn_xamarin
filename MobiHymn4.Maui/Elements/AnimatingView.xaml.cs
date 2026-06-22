using SkiaSharp.Extended.UI.Controls;

namespace MobiHymn4.Elements;

public partial class AnimatingView : ContentView
{
    private static readonly Dictionary<string, byte[]> GifCache = new();
    private string currentSource;
    private double currentSize;
    private int loadVersion;
    private bool usesLottie;

    public AnimatingView()
    {
        InitializeComponent();
    }

    public static readonly BindableProperty SizeProperty = BindableProperty.Create(
        nameof(Size), typeof(double), typeof(AnimatingView), 0.0, propertyChanged: SizePropertyChanged);

    public static readonly BindableProperty SourceProperty = BindableProperty.Create(
        nameof(Source), typeof(string), typeof(AnimatingView), string.Empty, propertyChanged: SourcePropertyChanged);

    public static readonly BindableProperty SpeedProperty = BindableProperty.Create(
        nameof(Speed), typeof(float), typeof(AnimatingView), 1f);

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public string Source
    {
        get => (string)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public float Speed
    {
        get => (float)GetValue(SpeedProperty);
        set => SetValue(SpeedProperty, value);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler == null || string.IsNullOrWhiteSpace(Source))
            return;

        currentSource = null;
        ApplySourceAsync(Source);
    }

    protected override void OnPropertyChanged(string propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (propertyName == IsVisibleProperty.PropertyName &&
            IsVisible &&
            !string.IsNullOrWhiteSpace(Source))
        {
            currentSource = null;
            ApplySourceAsync(Source);
        }
    }

    static void SizePropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var control = (AnimatingView)bindable;
        control.currentSize = (double)newValue;
        control.ApplySize();
    }

    static void SourcePropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var control = (AnimatingView)bindable;
        var val = (string)newValue;
        if (string.IsNullOrWhiteSpace(val))
            return;

        control.currentSource = null;
        control.ApplySourceAsync(val);
    }

    void ApplySize()
    {
        if (currentSize <= 0)
            return;

        HeightRequest = currentSize;
        MinimumHeightRequest = currentSize;
        WidthRequest = currentSize;
        root.HeightRequest = currentSize;
        root.WidthRequest = currentSize;

        gif.WidthRequest = currentSize;
        gif.HeightRequest = currentSize;
        lottie.WidthRequest = currentSize;
        lottie.HeightRequest = currentSize;

        if (currentSize > 1)
        {
            gif.Scale = 1;
            lottie.Scale = 1;
        }
        else
        {
            gif.Scale = currentSize;
            lottie.Scale = currentSize;
        }
    }

    void ApplySourceAsync(string name)
    {
        currentSource = name;
        var version = ++loadVersion;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            gif.IsAnimationPlaying = false;
            gif.Source = null;
            lottie.IsVisible = false;
            lottie.IsAnimationEnabled = false;
            gif.IsVisible = true;
            usesLottie = false;
        });

        _ = LoadAnimationAsync(name, version);
    }

    public void RestartAnimation()
    {
        if (string.IsNullOrWhiteSpace(currentSource))
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (usesLottie)
            {
                lottie.IsAnimationEnabled = false;
                lottie.Progress = TimeSpan.Zero;
                lottie.IsAnimationEnabled = true;
            }
            else
            {
                gif.IsAnimationPlaying = false;
                gif.IsAnimationPlaying = true;
            }
        });
    }

    async Task LoadAnimationAsync(string name, int version)
    {
        if (await TryLoadGifAsync(name, version))
            return;

        await TryLoadLottieAsync(name, version);
    }

    async Task<bool> TryLoadGifAsync(string name, int version)
    {
        try
        {
            if (!GifCache.TryGetValue(name, out var bytes))
            {
                await using var stream = await FileSystem.OpenAppPackageFileAsync($"{name}.gif");
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer);
                bytes = buffer.ToArray();
                GifCache[name] = bytes;
            }

            var payload = bytes;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (currentSource != name || version != loadVersion || Handler == null)
                    return;

                usesLottie = false;
                lottie.IsVisible = false;
                lottie.IsAnimationEnabled = false;
                gif.IsVisible = true;

                ApplySize();
                gif.Source = ImageSource.FromStream(() => new MemoryStream(payload));
                gif.IsAnimationPlaying = true;
            });

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load gif {name}: {ex.Message}");
            return false;
        }
    }

    async Task TryLoadLottieAsync(string name, int version)
    {
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync($"{name}.json");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load lottie {name}: {ex.Message}");
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (currentSource != name || version != loadVersion || Handler == null)
                return;

            usesLottie = true;
            gif.IsVisible = false;
            gif.IsAnimationPlaying = false;
            gif.Source = null;
            lottie.IsVisible = true;

            ApplySize();
            lottie.Source = (SKLottieImageSource)SKLottieImageSource.FromFile($"{name}.json");
            lottie.IsAnimationEnabled = true;
        });
    }
}
