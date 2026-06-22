using MobiHymn4.Models;

namespace MobiHymn4.Services;

public class AppVersionBuildPlatform : IAppVersionBuild
{
    public string GetVersionNumber() =>
        AppInfo.Current.VersionString;

    public string GetBuildNumber() =>
        AppInfo.Current.BuildString;
}
