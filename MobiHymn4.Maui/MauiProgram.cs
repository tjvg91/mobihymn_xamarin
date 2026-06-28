using CommunityToolkit.Maui;
using MobiHymn4.Handlers;
using MobiHymn4.Models;
using MobiHymn4.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace MobiHymn4;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseSkiaSharp()
            .ConfigureMauiHandlers(handlers =>
            {
#if ANDROID || IOS
                handlers.AddHandler<Elements.SelectableLabel, Handlers.SelectableLabelHandler>();
#endif
            })
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("SFPro.ttf", "SFPro");
                fonts.AddFont("faSolid.ttf", "FAS");
                fonts.AddFont("faRegular.ttf", "FAR");
                fonts.AddFont("faBrands.ttf", "FAB");
                fonts.AddFont("ionicons.ttf", "Ion");
                fonts.AddFont("logo.ttf", "LOGO");
                fonts.AddFont("NotoSerif-Regular.ttf", "NotoSerif");
                fonts.AddFont("NotoSerif-Bold.ttf", "NotoSerif-Bold");
                fonts.AddFont("NotoSerif-Italic.ttf", "NotoSerif-Italic");
                fonts.AddFont("NotoSerif-BoldItalic.ttf", "NotoSerif-BoldItalic");
                fonts.AddFont("ChelseaMarket-Regular.ttf", "ChelseaMarket");
                fonts.AddFont("Cookie-Regular.ttf", "Cookie");
                fonts.AddFont("DancingScript.ttf", "DancingScript");
                fonts.AddFont("Frosty.ttf", "Frosty");
                fonts.AddFont("KGKissMeSlowly.ttf", "KGKissMeSlowly");
                fonts.AddFont("KGMelonheadz.ttf", "KGMelonheadz");
                fonts.AddFont("KGWhattheTeacherWants.ttf", "KGWhattheTeacherWants");
                fonts.AddFont("StyleScript-Regular.ttf", "StyleScript");
                fonts.AddFont("UnifrakturMaguntia-Regular.ttf", "UnifrakturMaguntia");
                fonts.AddFont("VaudDisplay-Ultra.ttf", "VaudDisplay");
            });

        builder.Services.AddSingleton<IDataStore<Item>, MockDataStore>();
        builder.Services.AddSingleton<IFirebaseHelper, FirebaseHelperPlatform>();
        builder.Services.AddSingleton<IAppVersionBuild, AppVersionBuildPlatform>();
        builder.Services.AddSingleton<IMidiHelper, MidiHelperPlatform>();
        builder.Services.AddSingleton<IPlayService, HymnAudioPlayer>();
        builder.Services.AddSingleton<IVoiceRecognitionService, UnavailableVoiceRecognitionService>();
#if ANDROID
        builder.Services.AddSingleton<IVoiceRecognitionService, AndroidVoiceRecognitionService>();
        builder.Services.AddSingleton<IDownloadNotificationService, DownloadNotificationService>();
#endif

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();
        ServiceHelper.Initialize(app.Services);

#if ANDROID
        // Eagerly construct so Globals event subscriptions are wired before any download starts.
        app.Services.GetService<IDownloadNotificationService>();
#endif

        return app;
    }
}
