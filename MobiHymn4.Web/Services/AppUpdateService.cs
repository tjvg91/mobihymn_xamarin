using MobiHymn4.Shared;
using MobiHymn4.Shared.Models;

namespace MobiHymn4.Web.Services;

/// <summary>
/// App-version prompts from Firebase LatestRelease are disabled for the web PWA.
/// Hosting / service-worker updates deliver new shells; hymn catalog updates stay separate.
/// </summary>
public sealed class AppUpdateService
{
    public const string PlatformKey = "Web";

    public event Action? Changed;

    public bool IsPromptVisible => false;
    public LatestRelease? PendingRelease => null;
    public string PromptTitle => "";
    public string PromptMessage => "";
    public string DownloadLabel => "Download";
    public bool IsMandatory => false;

    public string LocalVersion => ReleaseHistory.CurrentVersion;

    public Task CheckAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task AcceptDownloadAsync() => Task.CompletedTask;

    public void Dismiss() { }
}
