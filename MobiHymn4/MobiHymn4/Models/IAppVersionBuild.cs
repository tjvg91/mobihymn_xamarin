using System;
namespace MobiHymn4.Models
{
	public interface IAppVersionBuild
	{
        string GetVersionNumber();
        string GetBuildNumber();
    }
}

