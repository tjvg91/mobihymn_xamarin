using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AVFoundation;
using Foundation;
using Microsoft.AppCenter;
using Microsoft.AppCenter.Analytics;
using Microsoft.AppCenter.Crashes;
using Microsoft.AppCenter.Distribute;
using MobiHymn2.Models;
using PanCardView.iOS;
using UIKit;

namespace MobiHymn2.iOS
{
    // The UIApplicationDelegate for the application. This class is responsible for launching the 
    // User Interface of the application, as well as listening (and optionally responding) to 
    // application events from iOS.
    [Register("AppDelegate")]
    public partial class AppDelegate : global::Xamarin.Forms.Platform.iOS.FormsApplicationDelegate
    {
        //
        // This method is invoked when the application has loaded and is ready to run. In this 
        // method you should instantiate the window, load the UI into it and then make the window
        // visible.
        //
        // You have 17 seconds to return from this method, or iOS will terminate your application.
        //
        public override bool FinishedLaunching(UIApplication app, NSDictionary options)
        {
            Xamarin.Forms.Forms.SetFlags("SwipeView_Experimental");
            global::Xamarin.Forms.Forms.Init();
            FFImageLoading.Forms.Platform.CachedImageRenderer.Init();
            CardsViewRenderer.Preserve();


            Distribute.ReleaseAvailable = OnReleaseAvailable;

            AppCenter.LogLevel = LogLevel.Error;
            Distribute.DontCheckForUpdatesInDebug();
            AppCenter.Start("a23cd518-34cd-4ff4-b669-0f786dee8d87", typeof(Analytics), typeof(Crashes), typeof(Distribute));

            LoadApplication(new App());

            // The following are all optional settings to change the behavior on iOS

            // this controls whether the library will attempt to set the shared AVAudioSession category, and then reset it after recording completes
            // AudioRecorderService.RequestAVAudioSessionCategory(AVAudioSessionCategory.PlayAndRecord);
            // same thing as above, forces the shared AVAudioSession into recording mode, and then reset it after recording completes
            // AudioPlayer.RequestAVAudioSessionCategory(AVAudioSessionCategory.PlayAndRecord);

            // allows you to add additional code to configure/change the shared AVAudioSession before each playback instance
            //	this can be used to alter the cateogry, audio port, check if the system will allow your app to access the session, etc.
            //	See https://github.com/NateRickard/Plugin.AudioRecorder/issues/27 for additional info
            //AudioPlayer.OnPrepareAudioSession = audioSession =>
            //{
                // maybe force audio to route to the speaker?
                //var success = audioSession.OverrideOutputAudioPort(AVAudioSessionPortOverride.Speaker, out NSError error);

                // do something else like test if the audio session can go active?

                //if (success)
                //{
                //	audioSession.SetActive (true, out error);
                //}
            //};

            return base.FinishedLaunching(app, options);
        }

        private bool OnReleaseAvailable(ReleaseDetails releaseDetails)
        {
            Analytics.TrackEvent("Release available callback invoked.");

            string versionName = releaseDetails.ShortVersion;
            string versionCodeOrBuildNumber = releaseDetails.Version;
            //string releaseNotes = releaseDetails.ReleaseNotes;
            string releaseNotes = "New update available. Let's go!";
            Uri releaseNotesUrl = releaseDetails.ReleaseNotesUrl;

            // custom dialog
            var title = "Version " + versionName + " available!";
            Task answer;

            // On mandatory update, user can't postpone
            if (releaseDetails.MandatoryUpdate)
            {
                answer = App.Current.MainPage.DisplayAlert(title, releaseNotes, "Download and Install");
            }
            else
            {
                answer = App.Current.MainPage.DisplayAlert(title, releaseNotes, "Download and Install", "Maybe tomorrow...");
            }

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

