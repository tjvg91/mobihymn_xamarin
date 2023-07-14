using System;
using Android.App;
using Android.OS;
using Android.Runtime;
using Plugin.FirebasePushNotification;

namespace MobiHymn4.Droid
{
	[Application(UsesCleartextTraffic = true)]
	public class MainApplication: Application
	{
		public MainApplication(System.IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        public override void OnCreate()
        {
            base.OnCreate();

            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                FirebasePushNotificationManager.DefaultNotificationChannelId = Application.Context.PackageName;
                FirebasePushNotificationManager.DefaultNotificationChannelName = "MobiHymn";
                FirebasePushNotificationManager.DefaultNotificationChannelImportance = NotificationImportance.Max;
            }

#if DEBUG
            FirebasePushNotificationManager.Initialize(this, true);
#else
            FirebasePushNotificationManager.Initialize(this,false);
#endif
            FirebasePushNotificationManager.IconResource = Resource.Mipmap.ic_stat_logo;
            CrossFirebasePushNotification.Current.OnNotificationReceived += (s, p) => { };
        }
    }
}

