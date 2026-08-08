using CommunityToolkit.Maui;
using MobiHymn4.Handlers;
using MobiHymn4.Models;
using MobiHymn4.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Plugin.Firebase.Auth;
using Plugin.Firebase.Core;
using SkiaSharp.Views.Maui.Controls.Hosting;
#if ANDROID
using Plugin.Firebase.Core.Platforms.Android;
using Microsoft.Maui.Platform;
#elif IOS
using Plugin.Firebase.Core.Platforms.iOS;
#endif

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
            .RegisterFirebaseServices()
            .ConfigureMauiHandlers(handlers =>
            {
#if ANDROID || IOS
                handlers.AddHandler<Elements.SelectableLabel, Handlers.SelectableLabelHandler>();
#endif
#if ANDROID
                handlers.AddHandler<DatePicker, Platforms.Android.MobiHymnDatePickerHandler>();

                Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping(
                    "TransparentUnderline",
                    (handler, _) =>
                        handler.PlatformView.BackgroundTintList =
                            Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent));

                Microsoft.Maui.Handlers.DatePickerHandler.Mapper.AppendToMapping(
                    "MobiHymnDatePickerField",
                    (handler, view) =>
                    {
                        if (handler.PlatformView is not MauiDatePicker platformView)
                            return;

                        var color = view.TextColor
                            ?? (Application.Current?.RequestedTheme == AppTheme.Dark
                                ? Colors.White
                                : Colors.Black);
                        platformView.SetTextColor(color.ToPlatform());
                        platformView.BackgroundTintList =
                            Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
                        // Display-only: value comes from the Material date picker, not typing.
                        platformView.InputType = Android.Text.InputTypes.Null;
                        platformView.KeyListener = null;
                        platformView.SetCursorVisible(false);
                        platformView.LongClickable = false;
                        platformView.SetTextIsSelectable(false);
                    });
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
        builder.Services.AddSingleton<IFirebaseFirestoreAccessor, FirebaseFirestoreAccessor>();
        builder.Services.AddSingleton<IAuthService, AuthService>();
        builder.Services.AddSingleton<IProfileService, ProfileService>();
        builder.Services.AddSingleton<IGroupService, GroupService>();
        builder.Services.AddSingleton<IBoardService, BoardService>();
        builder.Services.AddSingleton<BoardContext>();
        builder.Services.AddSingleton<BoardNavigationContext>();
        builder.Services.AddSingleton<IAddToBoardService, AddToBoardService>();
        builder.Services.AddSingleton<IGroupDashboardService, GroupDashboardService>();
        builder.Services.AddSingleton<IBoardNotificationService, BoardNotificationService>();
        builder.Services.AddSingleton<IUserSettingsSyncService, UserSettingsSyncService>();
        builder.Services.AddSingleton<IGoogleSignInService, UnavailableGoogleSignInService>();
#if ANDROID
        builder.Services.AddSingleton<IVoiceRecognitionService, AndroidVoiceRecognitionService>();
        builder.Services.AddSingleton<IDownloadNotificationService, DownloadNotificationService>();
#endif

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();
        ServiceHelper.Initialize(app.Services);
        // Resolve once so auth listener is attached for cloud settings sync.
        _ = app.Services.GetService<IUserSettingsSyncService>();

#if ANDROID
        app.Services.GetService<IDownloadNotificationService>();
#endif
        // Board FCM starts from App / MainActivity after first frame — avoid double StartAsync here.

        return app;
    }

    static MauiAppBuilder RegisterFirebaseServices(this MauiAppBuilder builder)
    {
        builder.Services.AddSingleton(_ => CrossFirebaseAuth.Current);
        return builder;
    }
}
