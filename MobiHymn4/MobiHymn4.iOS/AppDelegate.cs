using System;
using System.Collections.Generic;
using System.Linq;

using Foundation;
using ObjCRuntime;
using PanCardView.iOS;
using Plugin.FirebasePushNotification;
using UIKit;

namespace MobiHymn4.iOS
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

            Firebase.Core.App.Configure();
            FirebasePushNotificationManager.Initialize(options, true);

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

        public override void RegisteredForRemoteNotifications(UIApplication application, NSData deviceToken)
        {
            //base.RegisteredForRemoteNotifications(application, deviceToken);
            FirebasePushNotificationManager.DidRegisterRemoteNotifications(deviceToken);
        }

        public override void FailedToRegisterForRemoteNotifications(UIApplication application, NSError error)
        {
            //base.FailedToRegisterForRemoteNotifications(application, error);
            FirebasePushNotificationManager.RemoteNotificationRegistrationFailed(error);
        }

        public override void DidReceiveRemoteNotification(UIApplication application, NSDictionary userInfo, Action<UIBackgroundFetchResult> completionHandler)
        {
            //base.DidReceiveRemoteNotification(application, userInfo, completionHandler);

            FirebasePushNotificationManager.DidReceiveMessage(userInfo);
            // Do your magic to handle the notification data
            System.Console.WriteLine(userInfo);

            completionHandler(UIBackgroundFetchResult.NewData);
        }
    }
}

