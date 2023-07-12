using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AppCenter;
using Microsoft.AppCenter.Analytics;
using Microsoft.AppCenter.Crashes;
using Microsoft.AppCenter.Distribute;
using MobiHymn4.Utils;
using Xamarin.Essentials;
using Xamarin.Forms;

[assembly: Dependency(typeof(MobiHymn4.Droid.AppCenterService))]
namespace MobiHymn4.Droid
{
	public class AppCenterService : MobiHymn4.Services.IAppCenterService
	{
		public AppCenterService()
		{
		}

        public async Task InitiateAsync()
        {
            Distribute.ReleaseAvailable = (releaseDetails) =>
            {
                if (DeviceInfo.Platform == DevicePlatform.Android)
                {
                    string versionName = releaseDetails.ShortVersion;
                    string versionCodeOrBuildNumber = releaseDetails.Version;
                    string releaseNotes = releaseDetails.ReleaseNotes;
                    Uri releaseNotesUrl = releaseDetails.ReleaseNotesUrl;

                    var title = "Version " + versionName + " available!";
                    Task answer = releaseDetails.MandatoryUpdate ? App.Current.MainPage.DisplayAlert(title, releaseNotes, "Download and Install") :
                            App.Current.MainPage.DisplayAlert(title, releaseNotes, "Download and Install", "Ask Later");

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
                }

                return true;
            };
            AppCenter.LogLevel = LogLevel.Verbose;
            AppCenter.Start(
                "android=0b73296a-5659-4024-8c87-52893d062e8a;",
                typeof(Analytics), typeof(Crashes), typeof(Distribute)
            );

            await Analytics.SetEnabledAsync(true);
            await Distribute.SetEnabledAsync(true);
        }

        public void LogError(Exception exception)
        {
            Crashes.TrackError(exception);
        }

        public void LogInfo(string title)
        {
            Analytics.TrackEvent(title, new Dictionary<string, string>
            {
                { "Device Name", DeviceInfo.Name },
                { "Device Manufacturer", DeviceInfo.Manufacturer },
                { "Device Model", DeviceInfo.Model },
                { "Device OS Version", DeviceInfo.VersionString }
            });
        }

        public void LogInfo(string title, string valueName, string value)
        {
            Analytics.TrackEvent(title, new Dictionary<string, string>
            {
                { "Device Name", DeviceInfo.Name },
                { "Device Manufacturer", DeviceInfo.Manufacturer },
                { "Device Model", DeviceInfo.Model },
                { "Device OS Version", DeviceInfo.VersionString },
                { valueName, value }
            });
        }

        public void LogInfo(string title, string valueName, string oldValue, string newValue)
        {
            Analytics.TrackEvent(title, new Dictionary<string, string>
            {
                { "Device Name", DeviceInfo.Name },
                { "Device Manufacturer", DeviceInfo.Manufacturer },
                { "Device Model", DeviceInfo.Model },
                { "Device OS Version", DeviceInfo.VersionString },
                { $"Old {valueName}", oldValue },
                { $"New {valueName}", newValue }
            });
        }
    }
}

