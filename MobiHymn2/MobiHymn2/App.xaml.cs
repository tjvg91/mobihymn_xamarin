using System;

using Xamarin.Forms;
using Xamarin.Essentials;

using MobiHymn2.Utils;
using Microsoft.AppCenter;
using Microsoft.AppCenter.Analytics;
using Microsoft.AppCenter.Crashes;
using Microsoft.AppCenter.Distribute;
using System.Threading.Tasks;

namespace MobiHymn2
{
    public partial class App : Application
    {
        private Globals globalInstance = Globals.Instance;
        public App()
        {
            InitializeComponent();

            MainPage = new AppShell();
            bool darkMode = Preferences.Get("darkMode", false);
            Application.Current.UserAppTheme = darkMode ? OSAppTheme.Dark : OSAppTheme.Light;
        }

        protected override void OnStart()
        {
            DeviceDisplay.KeepScreenOn = true;
            Distribute.ReleaseAvailable = OnReleaseAvailable;
            Distribute.NoReleaseAvailable = OnNoReleaseAvailable;
            AppCenter.LogLevel = LogLevel.Verbose;
            AppCenter.Start(
                "ios=a23cd518-34cd-4ff4-b669-0f786dee8d87;" +
                "android=0b73296a-5659-4024-8c87-52893d062e8a;",
                typeof(Analytics), typeof(Crashes), typeof(Distribute)
            );

            Analytics.SetEnabledAsync(true);
            Distribute.SetEnabledAsync(true);
        }

        protected override void OnSleep()
        {
            DeviceDisplay.KeepScreenOn = false;
            globalInstance.SaveSettings();
        }

        protected override void OnResume()
        {
            DeviceDisplay.KeepScreenOn = globalInstance.KeepAwake;
        }

        public static bool OnReleaseAvailable(ReleaseDetails releaseDetails)
        {
            string versionName = releaseDetails.ShortVersion;
            string versionCodeOrBuildNumber = releaseDetails.Version;
            string releaseNotes = releaseDetails.ReleaseNotes;
            Uri releaseNotesUrl = releaseDetails.ReleaseNotesUrl;

            var title = "Version " + versionName + " available!";
            Task answer = releaseDetails.MandatoryUpdate ? Current.MainPage.DisplayAlert(title, releaseNotes, "Download and Install") :
                    Current.MainPage.DisplayAlert(title, releaseNotes, "Download and Install", "Ask Later");

            Globals.LogAppCenter(title);

            answer.ContinueWith((task) =>
            {
                if (releaseDetails.MandatoryUpdate || (task as Task<bool>).Result)
                {
                    Distribute.NotifyUpdateAction(UpdateAction.Update);
                    Globals.LogAppCenter(title, "Update action", "Update");
                }
                else
                {
                    Distribute.NotifyUpdateAction(UpdateAction.Postpone);
                    Globals.LogAppCenter(title, "Update action", "Postpone");
                }
            });

            return true;
        }

        private void OnNoReleaseAvailable()
        {
            Analytics.TrackEvent("No release available callback invoked.");
        }
    }
}