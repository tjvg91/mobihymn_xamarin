using Microsoft.JSInterop;
using MobiHymn4.Shared.Services;

namespace MobiHymn4.Web.Services;

/// <summary>IndexedDB-backed hymn catalog for the installed PWA.</summary>
public sealed class BrowserHymnCatalogStore : IHymnCatalogStore
{
    readonly IJSRuntime js;

    public BrowserHymnCatalogStore(IJSRuntime js) => this.js = js;

    public async Task ReplaceAllJsonAsync(string hymnsJsonArray, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await js.InvokeVoidAsync("mobihymnCatalog.replaceAllJson", cancellationToken, hymnsJsonArray)
            .ConfigureAwait(false);
    }

    public async Task<string?> GetHymnJsonAsync(string number, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await js.InvokeAsync<string?>("mobihymnCatalog.getJson", cancellationToken, number)
            .ConfigureAwait(false);
    }

    public async Task<string> GetAllJsonAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await js.InvokeAsync<string>("mobihymnCatalog.getAllJson", cancellationToken)
            .ConfigureAwait(false) ?? "[]";
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await js.InvokeAsync<int>("mobihymnCatalog.count", cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await js.InvokeVoidAsync("mobihymnCatalog.clear", cancellationToken)
            .ConfigureAwait(false);
    }
}
