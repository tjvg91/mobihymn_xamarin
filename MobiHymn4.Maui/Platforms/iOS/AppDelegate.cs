using Foundation;
using Plugin.Firebase.Core.Platforms.iOS;
using UIKit;

namespace MobiHymn4;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        CrossFirebase.Initialize();
        return base.FinishedLaunching(application, launchOptions);
    }
}
