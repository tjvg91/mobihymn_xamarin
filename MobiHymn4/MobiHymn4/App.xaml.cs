using System;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using MobiHymn4.Services;
using MobiHymn4.Utils;
using MobiHymn4.Models;
using Plugin.FirebasePushNotification;
using Xamarin.Forms;
using Xamarin.Essentials;

namespace MobiHymn4
{
    public partial class App : Application
    {
        private Globals globalInstance = Globals.Instance;
        IFirebaseHelper fbHelper;
        IAppVersionBuild appVBHelper;
        FirebaseHelper fbInstance;

        bool fromFirebaseNotif = false;

        public App(bool hasNotification = false, IDictionary<string, object> notificationData = null)
        {
            InitializeComponent();

            MainPage = new AppShell();

            bool darkMode = Preferences.Get(PreferencesVar.DARK_MODE, false);
            Current.UserAppTheme = darkMode ? OSAppTheme.Dark : OSAppTheme.Light;

            fbHelper = DependencyService.Get<IFirebaseHelper>();
            appVBHelper = DependencyService.Get<IAppVersionBuild>();
            fbInstance = FirebaseHelper.Instance;

            fromFirebaseNotif = hasNotification;

            CrossFirebasePushNotification.Current.OnTokenRefresh += Current_OnTokenRefresh;
            CrossFirebasePushNotification.Current.OnNotificationOpened += Current_OnNotificationOpened;
            CrossFirebasePushNotification.Current.OnNotificationReceived += Current_OnNotificationReceived;
        }

        private void Current_OnNotificationReceived(object source, FirebasePushNotificationDataEventArgs e)
        {
            Debug.WriteLine("Notification Received");
            if ((string)e.Data["type"] == "sync")
            {
                InitFirebase();
            }
            else if ((string)e.Data["type"] == "release" && DeviceInfo.Platform == DevicePlatform.iOS)
            {
                App.Current.MainPage.DisplayAlert("Release available!", "New release is available. Check your email to install!", "OK");
            }
        }

        private void Current_OnNotificationOpened(object source, FirebasePushNotificationResponseEventArgs e)
        {
            Debug.WriteLine("Notification Opened");
            if ((string)e.Data["type"] == "sync")
            {
                InitFirebase();
            }
        }

        private void Current_OnTokenRefresh(object source, FirebasePushNotificationTokenEventArgs e)
        {
            Debug.WriteLine($"Refresh token {e.Token}");
        }

        protected override async void OnStart()
        {
            try
            {
                var isNew = Preferences.Get(PreferencesVar.IS_NEW, true);
                if (!isNew) globalInstance.Init();

                DeviceDisplay.KeepScreenOn = globalInstance.KeepAwake;
                //Preferences.Set(PreferencesVar.RESYNC_VERSION, "0");
                if (DeviceInfo.Platform == DevicePlatform.iOS)
                {
                    await Task.Delay(2000);
                    await Shell.Current.GoToAsync($"//{Routes.HOME}");
                }
                else
                    await DependencyService.Get<IAppCenterService>().InitiateAsync();

                if(!fromFirebaseNotif)
                    InitFirebase();

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
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

        public async void InitFirebase()
        {
            globalInstance.IsFetchingSyncDetails = true;
            await fbHelper.LoginWithEmailPassword("tim.gandionco@gmail.com", "TLmSIsnw231");

            var deviceVersion = int.Parse(Preferences.Get(PreferencesVar.RESYNC_VERSION, "0"));
            globalInstance.ResyncDetails.AddRange(
                await fbInstance.RetrieveSyncChangesFrom(deviceVersion)
            );
            
            globalInstance.IsFetchingSyncDetails = false;
        }
    }
}

