using MobiHymn4.Shared.Models;

namespace MobiHymn4.Web.Services;

/// <summary>Cloud backup for <c>users/{uid}/appData/settings</c>.</summary>
public interface IUserSettingsCloudStore
{
    Task EnsureReadyAsync();
    Task<UserSettingsCloudDoc?> GetSettingsAsync(string uid);
    Task SetSettingsAsync(string uid, UserSettingsCloudDoc doc);
}
