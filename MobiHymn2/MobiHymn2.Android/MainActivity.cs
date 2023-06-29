using System;

using Android.App;
using Android.Content.PM;
using Android.Runtime;
using Android.OS;
using Acr.UserDialogs;
using AndroidX.AppCompat.App;
using Microsoft.AppCenter;
using PanCardView.Droid;
using Microsoft.AppCenter.Analytics;
using Microsoft.AppCenter.Crashes;
using Microsoft.AppCenter.Distribute;
using System.Threading.Tasks;

namespace MobiHymn2.Droid
{
    [Activity(Label = "MobiHymn", Icon = "@mipmap/ic_launcher", Theme = "@style/MainTheme", ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
    public class MainActivity : global::Xamarin.Forms.Platform.Android.FormsAppCompatActivity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            AppCompatDelegate.DefaultNightMode = AppCompatDelegate.ModeNightNo;
            base.OnCreate(savedInstanceState);

            Xamarin.Forms.Forms.SetFlags("SwipeView_Experimental");

            Xamarin.Essentials.Platform.Init(this, savedInstanceState);
            global::Xamarin.Forms.Forms.Init(this, savedInstanceState);
            UserDialogs.Init(this);
            FFImageLoading.Forms.Platform.CachedImageRenderer.Init(true);
            CardsViewRenderer.Preserve();

            Distribute.ReleaseAvailable = OnReleaseAvailable;
            Distribute.NoReleaseAvailable = OnNoReleaseAvailable;
            AppCenter.LogLevel = LogLevel.Verbose;
            AppCenter.Start("0b73296a-5659-4024-8c87-52893d062e8a", typeof(Analytics), typeof(Crashes), typeof(Distribute));

            LoadApplication(new App());
            Window.SetStatusBarColor(Android.Graphics.Color.ParseColor("#F5D200"));

        }
        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Android.Content.PM.Permission[] grantResults)
        {
            Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }

        private bool OnReleaseAvailable(ReleaseDetails releaseDetails)
        {
            Analytics.TrackEvent("Release available callback invoked.");

            string versionName = releaseDetails.ShortVersion;
            string versionCodeOrBuildNumber = releaseDetails.Version;
            string releaseNotes = "New update available. Let's go!";
            Uri releaseNotesUrl = releaseDetails.ReleaseNotesUrl;

            // custom dialog
            var title = "Version " + versionName + " available!";
            Task answer;

            // On mandatory update, user can't postpone
            if (releaseDetails.MandatoryUpdate)
                answer = App.Current.MainPage.DisplayAlert(title, releaseNotes, "Download and Install");
            else
                answer = App.Current.MainPage.DisplayAlert(title, releaseNotes, "Download and Install", "Maybe later");

            answer.ContinueWith((task) =>
            {
                // If mandatory or if answer was positive
                if (releaseDetails.MandatoryUpdate || (task as Task<bool>).Result)
                {
                    // Notify SDK that user selected update
                    Distribute.NotifyUpdateAction(UpdateAction.Update);
                }
                else
                {
                    // Notify SDK that user selected postpone (for 1 day)
                    // This method call is ignored by the SDK if the update is mandatory
                    Distribute.NotifyUpdateAction(UpdateAction.Postpone);
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
