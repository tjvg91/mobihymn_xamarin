using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace MobiHymn4.Web.Services;

/// <summary>
/// Web app-shell updates. A new deploy installs a waiting service worker; index.html
/// (window.mhUpdate) calls <see cref="OnUpdateReady"/> and this service applies the
/// policy published by tools/deploy-web-hosting.ps1 -Update auto|optional|mandatory:
/// auto reloads silently, optional prompts (Later applies on next launch), mandatory blocks.
/// Hymn catalog updates are separate (<see cref="CatalogUpdateService"/>).
/// </summary>
public sealed class AppUpdateService : IAsyncDisposable
{
    public const string BuildIdMetadataKey = "MobiHymnBuildId";

    enum UpdateMode { Auto, Optional, Mandatory }

    sealed class UpdatePolicy
    {
        [JsonPropertyName("build")] public string? Build { get; set; }
        [JsonPropertyName("version")] public string? Version { get; set; }
        [JsonPropertyName("mode")] public string? Mode { get; set; }
        [JsonPropertyName("mandatorySince")] public string? MandatorySince { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
    }

    readonly IJSRuntime js;
    DotNetObjectReference<AppUpdateService>? selfRef;
    bool started;
    string? dismissedBuild;
    string? promptBuild;

    public AppUpdateService(IJSRuntime js) => this.js = js;

    public event Action? Changed;

    /// <summary>Build stamp of the running app; empty for local/dev builds.</summary>
    public static string LocalBuildId { get; } =
        typeof(AppUpdateService).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == BuildIdMetadataKey)?.Value ?? "";

    public bool IsPromptVisible { get; private set; }
    public bool IsMandatory { get; private set; }
    public string PromptTitle { get; private set; } = "";
    public string PromptMessage { get; private set; } = "";
    public string DownloadLabel { get; private set; } = "Update now";

    public async Task StartAsync()
    {
        if (started) return;
        started = true;
        selfRef = DotNetObjectReference.Create(this);
        try
        {
            await js.InvokeVoidAsync("mhUpdate.register", selfRef);
        }
        catch { /* dev server / older cached index.html: silent updates still apply */ }
    }

    [JSInvokable]
    public async Task OnUpdateReady()
    {
        var policy = await ReadPolicyAsync();
        var mode = ResolveMode(policy);
        if (mode == UpdateMode.Auto)
        {
            await ApplyAsync();
            return;
        }

        var build = policy?.Build ?? "";
        if (mode == UpdateMode.Optional && build.Length > 0 && build == dismissedBuild)
            return;
        if (IsPromptVisible && promptBuild == build && IsMandatory == (mode == UpdateMode.Mandatory))
            return;

        var versionText = string.IsNullOrWhiteSpace(policy?.Version)
            ? "A new version of MobiHymn"
            : $"MobiHymn {policy!.Version.Trim()}";
        var custom = policy?.Message?.Trim();

        promptBuild = build;
        IsMandatory = mode == UpdateMode.Mandatory;
        PromptTitle = IsMandatory ? "Update required" : "Update available";
        PromptMessage = !string.IsNullOrEmpty(custom)
            ? custom
            : IsMandatory
                ? $"{versionText} is required to keep using the app. It only takes a moment."
                : $"{versionText} is ready. Update now, or it will install the next time you open the app.";
        DownloadLabel = "Update now";
        IsPromptVisible = true;
        Changed?.Invoke();
    }

    public async Task AcceptDownloadAsync()
    {
        DownloadLabel = "Updating…";
        Changed?.Invoke();
        await ApplyAsync();
    }

    public void Dismiss()
    {
        if (IsMandatory || !IsPromptVisible) return;
        dismissedBuild = promptBuild;
        IsPromptVisible = false;
        Changed?.Invoke();
    }

    async Task ApplyAsync()
    {
        try
        {
            await js.InvokeVoidAsync("mhUpdate.apply");
        }
        catch { /* page is reloading */ }
    }

    async Task<UpdatePolicy?> ReadPolicyAsync()
    {
        try
        {
            var json = await js.InvokeAsync<string?>("mhUpdate.fetchPolicy");
            return string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<UpdatePolicy>(json);
        }
        catch
        {
            return null;
        }
    }

    static UpdateMode ResolveMode(UpdatePolicy? policy)
    {
        if (policy == null) return UpdateMode.Auto;

        var mandatorySince = policy.MandatorySince ?? "";
        if (LocalBuildId.Length > 0 && mandatorySince.Length > 0
            && string.CompareOrdinal(LocalBuildId, mandatorySince) < 0)
            return UpdateMode.Mandatory;

        return (policy.Mode ?? "").Trim().ToLowerInvariant() switch
        {
            "mandatory" => UpdateMode.Mandatory,
            "optional" => UpdateMode.Optional,
            _ => UpdateMode.Auto
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (selfRef == null) return;
        try { await js.InvokeVoidAsync("mhUpdate.unregister"); } catch { /* ignore */ }
        selfRef.Dispose();
        selfRef = null;
    }
}
