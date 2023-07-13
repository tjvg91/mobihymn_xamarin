using System;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.Content.PM;
using MobiHymn4.Models;

namespace MobiHymn4.Droid
{
	public class AppVersionBuild : IAppVersionBuild
    {
        PackageInfo _appInfo;
        public AppVersionBuild() 
		{
            var context = Android.App.Application.Context;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
                _appInfo = context.PackageManager.GetPackageInfo(context.PackageName, 0);
            else
            {
                #pragma warning disable CS0618 // Type or member is obsolete
                _appInfo = context.PackageManager.GetPackageInfo(context.PackageName, 0);
                #pragma warning restore CS0618 // Type or member is obsolete
            }
        }

        public string GetVersionNumber()
        {
            return _appInfo.VersionName;
        }

        public string GetBuildNumber()
        {
            if (Build.VERSION.SdkInt <= BuildVersionCodes.P)
                return _appInfo.VersionCode.ToString();
            else
                return PackageInfoCompat.GetLongVersionCode(_appInfo).ToString();
        }
    }
}

