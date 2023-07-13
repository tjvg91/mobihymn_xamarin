using System;
using Foundation;
using MobiHymn4.Models;

namespace MobiHymn4.iOS
{
	public class AppVersionBuild : IAppVersionBuild
    {
		public AppVersionBuild()
		{
		}

        string IAppVersionBuild.GetBuildNumber()
        {
            return NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleShortVersionString").ToString();
        }

        string IAppVersionBuild.GetVersionNumber()
        {
            return NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleVersion").ToString();
        }
    }
}

