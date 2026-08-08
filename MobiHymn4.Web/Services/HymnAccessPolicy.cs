using Microsoft.JSInterop;
using MobiHymn4.Shared;
using MobiHymn4.Shared.Services;

namespace MobiHymn4.Web.Services;

/// <summary>
/// Installed PWA → local catalog only. Browser tab → API on demand.
/// </summary>
public sealed class HymnAccessPolicy : IHymnAccessPolicy
{
    readonly IAppPreferences prefs;
    readonly IJSRuntime js;
    bool initialized;
    bool standalone;

    public HymnAccessPolicy(IAppPreferences prefs, IJSRuntime js)
    {
        this.prefs = prefs;
        this.js = js;
    }

    public bool IsStandalonePwa => standalone;

    public bool IsLibraryDownloaded =>
        prefs.GetBool(PrefKeys.HymnLibraryDownloaded, false);

    /// <summary>Installed app must use the downloaded catalog for reads/search.</summary>
    public bool UseLocalCatalogOnly => standalone;

    public async Task InitializeAsync()
    {
        if (initialized) return;
        try
        {
            standalone = await js.InvokeAsync<bool>("mobihymnPwa.isStandalone");
        }
        catch
        {
            standalone = false;
        }

        initialized = true;
    }
}
