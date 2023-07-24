using System;

using Android.App;
using Android.Content.PM;
using Android.Runtime;
using Android.OS;
using AndroidX.AppCompat.App;
using PanCardView.Droid;
using System.Drawing;
using System.Threading.Tasks;
using Microsoft.AppCenter.Distribute;
using Microsoft.AppCenter;
using Microsoft.AppCenter.Analytics;
using Microsoft.AppCenter.Crashes;
using Plugin.FirebasePushNotification;
using System.Collections.Generic;

namespace MobiHymn4.Droid
{
    [Activity(Label = "MobiHymn", Icon = "@mipmap/ic_launcher", Theme = "@style/MainTheme", ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
    public class MainActivity : global::Xamarin.Forms.Platform.Android.FormsAppCompatActivity
    {
        private bool IsNotification = false;
        private IDictionary<string, object> NotificationData;
        protected override void OnCreate(Bundle savedInstanceState)
        {
            AppCompatDelegate.DefaultNightMode = AppCompatDelegate.ModeNightNo;
            base.OnCreate(savedInstanceState);

            Xamarin.Forms.Forms.SetFlags(new string[] { "SwipeView_Experimental", "Expander_Experimental" });

            Xamarin.Essentials.Platform.Init(this, savedInstanceState);
            global::Xamarin.Forms.Forms.Init(this, savedInstanceState);
            FFImageLoading.Forms.Platform.CachedImageRenderer.Init(true);
            CardsViewRenderer.Preserve();

            Distribute.ReleaseAvailable = OnReleaseAvailable;
            AppCenter.LogLevel = LogLevel.Verbose;
            AppCenter.Start("0b73296a-5659-4024-8c87-52893d062e8a", typeof(Analytics), typeof(Crashes), typeof(Distribute));

            if (!IsNotification)
                LoadApplication(new App());

            FirebasePushNotificationManager.ProcessIntent(this, Intent);

            CrossFirebasePushNotification.Current.OnNotificationOpened += (s, p) =>
            {
                System.Diagnostics.Debug.WriteLine("NOTIFICATION Opened", p.Data);
                Analytics.TrackEvent("Notification opened");
                NotificationData = p.Data;
                IsNotification = true;
                LoadApplication(new App(IsNotification, NotificationData));
            };
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
    }
}
