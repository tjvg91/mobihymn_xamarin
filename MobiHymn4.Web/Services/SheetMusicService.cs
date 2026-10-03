using Microsoft.JSInterop;

namespace MobiHymn4.Web.Services;

/// <summary>
/// Sheet-music PDFs read from Firebase Storage (pdf/Hymn {n}.pdf via /api/pdf), with optional
/// per-hymn offline copies kept on the device (pdf-viewer.js → mhSheetMusic).
/// Availability is checked per hymn; "unknown" answers (offline, server error) are re-asked later.
/// </summary>
public sealed class SheetMusicService
{
    public sealed record SavedSummary(int Count, long Bytes);

    readonly IJSRuntime js;
    readonly Dictionary<string, bool> known = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Task> pending = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, DateTime> retryAfter = new(StringComparer.OrdinalIgnoreCase);
    static readonly TimeSpan UnknownRetry = TimeSpan.FromSeconds(30);

    public SheetMusicService(IJSRuntime js) => this.js = js;

    public event Action? Changed;

    public bool HasPdf(string? number) =>
        !string.IsNullOrWhiteSpace(number) && known.TryGetValue(number.Trim(), out var has) && has;

    public Task CheckAsync(string? number)
    {
        if (string.IsNullOrWhiteSpace(number)) return Task.CompletedTask;
        var key = number.Trim();
        if (known.ContainsKey(key)) return Task.CompletedTask;
        if (pending.TryGetValue(key, out var running)) return running;
        if (retryAfter.TryGetValue(key, out var when) && DateTime.UtcNow < when) return Task.CompletedTask;
        var task = CheckCoreAsync(key);
        pending[key] = task;
        return task;
    }

    async Task CheckCoreAsync(string number)
    {
        try
        {
            var answer = await js.InvokeAsync<string>("mhSheetMusic.availability", number);
            if (answer is "yes" or "no")
            {
                known[number] = answer == "yes";
                retryAfter.Remove(number);
                Changed?.Invoke();
            }
            else
            {
                retryAfter[number] = DateTime.UtcNow + UnknownRetry;
            }
        }
        catch
        {
            retryAfter[number] = DateTime.UtcNow + UnknownRetry;
        }
        finally
        {
            pending.Remove(number);
        }
    }

    public async Task<bool> IsSavedAsync(string number)
    {
        try { return await js.InvokeAsync<bool>("mhSheetMusic.isSaved", number); }
        catch { return false; }
    }

    /// <summary>Saves or removes the offline copy; returns the new saved state.</summary>
    public async Task<bool> SetSavedAsync(string number, bool save)
    {
        var saved = await js.InvokeAsync<bool>(save ? "mhSheetMusic.save" : "mhSheetMusic.remove", number);
        Changed?.Invoke();
        return saved;
    }

    public async Task<SavedSummary> GetSavedSummaryAsync()
    {
        try { return await js.InvokeAsync<SavedSummary>("mhSheetMusic.savedSummary"); }
        catch { return new SavedSummary(0, 0); }
    }

    public async Task ClearSavedAsync()
    {
        try { await js.InvokeVoidAsync("mhSheetMusic.clearSaved"); }
        catch { /* ignore */ }
        Changed?.Invoke();
    }
}
