using System;
using Xamarin.Forms;
using Xamarin.Essentials;
using MobiHymn4.Services;
using MobiHymn4.Utils;
using System.Threading.Tasks;
using MobiHymn4.Models;

namespace MobiHymn4
{
    public partial class App : Application
    {
        private Globals globalInstance = Globals.Instance;
        IFirebaseHelper helper;
        FirebaseHelper fbInstance;

        public App()
        {
            InitializeComponent();

            MainPage = new AppShell();

            try
            {
                bool darkMode = Preferences.Get("darkMode", false);
                Current.UserAppTheme = darkMode ? OSAppTheme.Dark : OSAppTheme.Light;
                helper = DependencyService.Get<IFirebaseHelper>();
                fbInstance = new FirebaseHelper();
            }
            catch (Exception ex)
            {
                Console.Write(ex.Message);
            }
        }

        protected override async void OnStart()
        {
            try
            {
                DeviceDisplay.KeepScreenOn = globalInstance.KeepAwake;
                if (DeviceInfo.Platform == DevicePlatform.iOS)
                {
                    await Task.Delay(2000);
                    await Shell.Current.GoToAsync($"//{Routes.HOME}");
                }
                else
                    await DependencyService.Get<IAppCenterService>().InitiateAsync();

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
            await helper.LoginWithEmailPassword("tim.gandionco@gmail.com", "TLmSIsnw231");

            globalInstance.ResyncDetails.AddRange(await fbInstance.RetrieveSyncChangesFrom(int.Parse(Preferences.Get("ResyncVersion", "0"))));
            globalInstance.IsFetchingSyncDetails = false;
        }
    }
}

