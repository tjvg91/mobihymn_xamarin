namespace MobiHymn4.Shared.Models;

/// <summary>Firebase Realtime DB <c>LatestRelease/{Platform}</c> (MAUI Android/iOS; Web PWA uses <c>Web</c>).</summary>
public sealed class LatestRelease
{
    public string Version { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public bool Mandatory { get; set; }
}
