using System;

using Xamarin.Forms;
using Xamarin.Essentials;

using MobiHymn2.Utils;
using Microsoft.AppCenter;

namespace MobiHymn2
{
    public partial class App : Application
    {
        private Globals globalInstance = Globals.Instance;
        public App ()
        {
            InitializeComponent();

            Acr.UserDialogs.ToastConfig.DefaultPosition = Acr.UserDialogs.ToastPosition.Top;

            MainPage = new AppShell();
            bool darkMode = Preferences.Get("darkMode", false);
            Application.Current.UserAppTheme = darkMode ? OSAppTheme.Dark : OSAppTheme.Light;
        }

        protected override void OnStart ()
        {
            DeviceDisplay.KeepScreenOn = true;
            AppCenter.Start(
                "ios=a23cd518-34cd-4ff4-b669-0f786dee8d87;" +
                "android=0b73296a-5659-4024-8c87-52893d062e8a;");
        }

        protected override void OnSleep ()
        {
            DeviceDisplay.KeepScreenOn = false;
            globalInstance.SaveSettings();
        }

        protected override void OnResume ()
        {
            DeviceDisplay.KeepScreenOn = globalInstance.KeepAwake;
        }
    }
}

