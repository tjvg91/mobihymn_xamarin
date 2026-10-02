using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using MobiHymn4.Shared;
using MobiHymn4.Shared.Services;

namespace MobiHymn4.Web.Services;

/// <summary>
/// Catalog update badges (MAUI-style) — only for an installed PWA after the hymn library
/// has been downloaded. Browser tabs keep on-demand API loading with no update badges.
/// Installed PWAs read/search hymns only from the downloaded catalog.
/// An update is pending when the server catalogHash changes, or when
/// tools/push-catalog-update.ps1 publishes a newer /catalog-policy.json (optionally mandatory).
/// </summary>
public sealed class CatalogUpdateService
{
    const string PolicyPath = "catalog-policy.json";

    sealed class CatalogPolicy
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("mode")] public string? Mode { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
    }

    readonly IHymnLyricsSource lyrics;
    readonly IAppPreferences prefs;
    readonly IHymnAccessPolicy access;
    readonly IHymnCatalogStore catalogStore;
    readonly HttpClient http;
    int checkInFlight;
    CatalogPolicy? latestPolicy;
    DateTime lastRefreshUtc;

    public CatalogUpdateService(
        IHymnLyricsSource lyrics,
        IAppPreferences prefs,
        IHymnAccessPolicy access,
        IHymnCatalogStore catalogStore,
        HttpClient http)
    {
        this.lyrics = lyrics;
        this.prefs = prefs;
        this.access = access;
        this.catalogStore = catalogStore;
        this.http = http;
    }

    public event Action? Changed;

    public bool IsStandalonePwa => access.IsStandalonePwa;
    public bool IsLibraryDownloaded => access.IsLibraryDownloaded;

    /// <summary>True when installed as PWA and hymns have been downloaded at least once.</summary>
    public bool IsUpdateTrackingEnabled => IsStandalonePwa && IsLibraryDownloaded;

    public bool IsChecking { get; private set; }
    public bool IsDownloadingLibrary { get; private set; }
    public double DownloadProgress { get; private set; }
    public int DownloadCompleted { get; private set; }
    public int DownloadTotal { get; private set; }
    public bool HasPendingUpdates { get; private set; }
    /// <summary>Identifies the pending update (server hash, plus policy id when pushed).</summary>
    public string? PendingUpdateKey { get; private set; }
    public int PendingTotal { get; private set; }
    /// <summary>A pushed catalog policy marked mandatory — the prompt has no Later.</summary>
    public bool IsMandatoryUpdate { get; private set; }
    /// <summary>Custom text from the pushed catalog policy, if any.</summary>
    public string? PendingMessage { get; private set; }
    public string? CheckError { get; private set; }

    public bool ShowHamburgerBadge { get; private set; }
    public bool ShowSettingsBadge { get; private set; }
    public bool ShowResyncBadge { get; private set; }

    bool hamburgerAcknowledged;
    bool settingsAcknowledged;
    bool resyncAcknowledged;
    string? acknowledgedForHash;
    bool initialized;

    /// <summary>Installed PWA with no local library yet — show the first-run download popup.</summary>
    public bool NeedsLibraryDownload => IsStandalonePwa && !IsLibraryDownloaded;

    public string DownloadProgressText
    {
        get
        {
            if (DownloadTotal > 0)
                return $"{DownloadCompleted} / {DownloadTotal}";
            if (IsDownloadingLibrary)
                return "Starting…";
            return "";
        }
    }

    public string SyncSummary
    {
        get
        {
            if (!IsStandalonePwa)
                return "Hymns load from the server as you read. Install MobiHymn as an app to download the library and get update alerts.";
            if (!IsLibraryDownloaded)
                return "Download the hymn library once. The installed app reads and searches only from that download.";
            if (IsChecking)
                return "Checking the server for hymn changes…";
            if (!string.IsNullOrEmpty(CheckError))
                return CheckError;
            if (HasPendingUpdates)
            {
                var total = PendingTotal > 0 ? $" ({PendingTotal} hymns on server)" : "";
                return $"Hymn catalog updates are available{total}. Tap Refresh library to load them.";
            }
            var storedTotal = prefs.Get(PrefKeys.HymnTotal, "");
            if (int.TryParse(storedTotal, out var n) && n > 0)
                return $"Your hymn catalog is up to date ({n} hymns). Reading and search use the downloaded library.";
            return "Your hymn catalog is up to date. Reading and search use the downloaded library.";
        }
    }

    public async Task InitializeAsync()
    {
        if (initialized) return;
        await access.InitializeAsync().ConfigureAwait(false);

        // Reconcile the download flag with IndexedDB. Never clear the flag on a
        // transient IDB/JS error — that caused users to be asked to download again.
        try
        {
            var count = await CountCatalogWithRetryAsync().ConfigureAwait(false);
            if (count > 0)
            {
                if (!IsLibraryDownloaded)
                    prefs.SetBool(PrefKeys.HymnLibraryDownloaded, true);
            }
            else if (IsLibraryDownloaded)
            {
                // Confirmed empty store — older builds set the flag without persisting hymns.
                prefs.SetBool(PrefKeys.HymnLibraryDownloaded, false);
            }
        }
        catch
        {
            // Leave HymnLibraryDownloaded as-is if we can't read the store yet.
        }

        initialized = true;
        if (!IsUpdateTrackingEnabled)
            ClearPending();
        Notify();
    }

    async Task<int> CountCatalogWithRetryAsync()
    {
        try
        {
            return await catalogStore.CountAsync().ConfigureAwait(false);
        }
        catch
        {
            await Task.Delay(250).ConfigureAwait(false);
            return await catalogStore.CountAsync().ConfigureAwait(false);
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync().ConfigureAwait(false);

        if (!IsUpdateTrackingEnabled)
        {
            ClearPending();
            CheckError = null;
            Notify();
            return;
        }

        if (Interlocked.Exchange(ref checkInFlight, 1) != 0)
            return;

        IsChecking = true;
        CheckError = null;
        lastRefreshUtc = DateTime.UtcNow;
        Notify();

        try
        {
            var metaTask = lyrics.GetCatalogMetaFreshAsync(cancellationToken);
            var policy = await FetchPolicyAsync(cancellationToken).ConfigureAwait(false);
            var meta = await metaTask.ConfigureAwait(false);
            if (meta.Total > 0)
                prefs.Set(PrefKeys.HymnTotal, meta.Total.ToString());

            var serverHash = (meta.CatalogHash ?? "").Trim();
            var lastHash = prefs.Get(PrefKeys.HymnCatalogHash, "").Trim();
            var policyId = (policy?.Id ?? "").Trim();
            var policyPending = policyId.Length > 0
                && string.CompareOrdinal(prefs.Get(PrefKeys.CatalogPolicyAppliedId, "").Trim(), policyId) < 0;

            // No baseline yet after download flag without hash — treat as current after meta.
            if (string.IsNullOrEmpty(lastHash) && !policyPending)
            {
                if (!string.IsNullOrEmpty(serverHash))
                    prefs.Set(PrefKeys.HymnCatalogHash, serverHash);
                ClearPending();
                return;
            }

            if (!policyPending
                && !string.IsNullOrEmpty(serverHash)
                && string.Equals(serverHash, lastHash, StringComparison.Ordinal))
            {
                ClearPending();
                return;
            }

            HasPendingUpdates = true;
            var hashKey = string.IsNullOrEmpty(serverHash) ? lastHash : serverHash;
            PendingUpdateKey = policyPending ? $"{hashKey}+policy:{policyId}" : NullIfEmpty(serverHash);
            PendingTotal = meta.Total;
            IsMandatoryUpdate = policyPending
                && string.Equals(policy?.Mode?.Trim(), "mandatory", StringComparison.OrdinalIgnoreCase);
            PendingMessage = policyPending ? NullIfEmpty(policy?.Message?.Trim()) : null;
            ResetAckIfHashChanged(PendingUpdateKey ?? lastHash);
            UpdateBadgeFlags();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CheckError = "Could not check for hymn changes. Try again later."
                + DescribeFailure(ex);
            Notify();
        }
        finally
        {
            IsChecking = false;
            Interlocked.Exchange(ref checkInFlight, 0);
            Notify();
        }
    }

    /// <summary>Re-check when the app returns to the foreground, at most once per <paramref name="minInterval"/>.</summary>
    public Task RefreshIfStaleAsync(TimeSpan minInterval)
    {
        if (!IsUpdateTrackingEnabled || DateTime.UtcNow - lastRefreshUtc < minInterval)
            return Task.CompletedTask;
        return RefreshAsync();
    }

    async Task<CatalogPolicy?> FetchPolicyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{PolicyPath}?t={DateTime.UtcNow.Ticks}");
            request.SetBrowserRequestCache(BrowserRequestCache.NoStore);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return latestPolicy = null;
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return latestPolicy = JsonSerializer.Deserialize<CatalogPolicy>(json);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Offline or SPA fallback HTML — keep the last policy we saw.
            return latestPolicy;
        }
    }

    /// <summary>After a full sync this device has everything up to the newest pushed policy.</summary>
    async Task MarkPolicyAppliedAsync(CancellationToken cancellationToken)
    {
        var policy = await FetchPolicyAsync(cancellationToken).ConfigureAwait(false);
        var id = (policy?.Id ?? "").Trim();
        if (id.Length > 0)
            prefs.Set(PrefKeys.CatalogPolicyAppliedId, id);
    }

    static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>First-time PWA download: persist catalog and mark library as downloaded.</summary>
    public async Task DownloadLibraryAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync().ConfigureAwait(false);
        if (!IsStandalonePwa)
        {
            CheckError = "Install MobiHymn as an app first, then download the hymn library.";
            Notify();
            return;
        }

        if (IsDownloadingLibrary)
            return;

        IsChecking = true;
        IsDownloadingLibrary = true;
        CheckError = null;
        DownloadProgress = 0;
        DownloadCompleted = 0;
        DownloadTotal = 0;
        Notify();

        var progress = new Progress<(int Completed, int Total)>(p =>
        {
            DownloadCompleted = p.Completed;
            DownloadTotal = p.Total;
            DownloadProgress = p.Total > 0 ? Math.Clamp((double)p.Completed / p.Total, 0, 1) : 0;
            Notify();
        });

        try
        {
            lyrics.InvalidateLocalCaches();
            await lyrics.SyncCatalogFromNetworkAsync(progress, cancellationToken).ConfigureAwait(false);

            // Confirm hymns were actually persisted before flipping the one-time download flag.
            var stored = await CountCatalogWithRetryAsync().ConfigureAwait(false);
            if (stored <= 0)
                throw new InvalidOperationException("The hymn library could not be saved on this device.");

            var meta = await lyrics.GetCatalogMetaFreshAsync(cancellationToken).ConfigureAwait(false);
            if (meta.Total > 0)
                prefs.Set(PrefKeys.HymnTotal, meta.Total.ToString());
            if (!string.IsNullOrWhiteSpace(meta.CatalogHash))
                prefs.Set(PrefKeys.HymnCatalogHash, meta.CatalogHash.Trim());
            prefs.Set(PrefKeys.HymnTotal, Math.Max(meta.Total, stored).ToString());
            await MarkPolicyAppliedAsync(cancellationToken).ConfigureAwait(false);
            prefs.SetBool(PrefKeys.HymnLibraryDownloaded, true);
            DownloadProgress = 1;
            DownloadCompleted = stored;
            DownloadTotal = Math.Max(DownloadTotal, stored);
            ClearPending();
            AcknowledgeAll();
        }
        catch (Exception ex)
        {
            CheckError = "Could not download the hymn library. " + ex.Message;
        }
        finally
        {
            IsChecking = false;
            IsDownloadingLibrary = false;
            Notify();
        }
    }

    public async Task ApplyUpdatesAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync().ConfigureAwait(false);
        if (!IsUpdateTrackingEnabled)
        {
            await DownloadLibraryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (IsDownloadingLibrary)
            return;

        IsChecking = true;
        IsDownloadingLibrary = true;
        CheckError = null;
        DownloadProgress = 0;
        DownloadCompleted = 0;
        DownloadTotal = 0;
        Notify();

        var progress = new Progress<(int Completed, int Total)>(p =>
        {
            DownloadCompleted = p.Completed;
            DownloadTotal = p.Total;
            DownloadProgress = p.Total > 0 ? Math.Clamp((double)p.Completed / p.Total, 0, 1) : 0;
            Notify();
        });

        try
        {
            lyrics.InvalidateLocalCaches();
            await lyrics.SyncCatalogFromNetworkAsync(progress, cancellationToken).ConfigureAwait(false);

            var stored = await CountCatalogWithRetryAsync().ConfigureAwait(false);
            if (stored <= 0)
                throw new InvalidOperationException("The hymn library could not be saved on this device.");

            var meta = await lyrics.GetCatalogMetaFreshAsync(cancellationToken).ConfigureAwait(false);
            if (meta.Total > 0)
                prefs.Set(PrefKeys.HymnTotal, meta.Total.ToString());
            if (!string.IsNullOrWhiteSpace(meta.CatalogHash))
                prefs.Set(PrefKeys.HymnCatalogHash, meta.CatalogHash.Trim());
            prefs.Set(PrefKeys.HymnTotal, Math.Max(meta.Total, stored).ToString());
            await MarkPolicyAppliedAsync(cancellationToken).ConfigureAwait(false);
            prefs.SetBool(PrefKeys.HymnLibraryDownloaded, true);
            DownloadProgress = 1;
            DownloadCompleted = stored;
            DownloadTotal = Math.Max(DownloadTotal, stored);
            ClearPending();
            AcknowledgeAll();
        }
        catch (Exception ex)
        {
            CheckError = "Could not refresh the hymn library. " + ex.Message;
        }
        finally
        {
            IsChecking = false;
            IsDownloadingLibrary = false;
            Notify();
        }
    }

    /// <summary>A newer catalog is on the server and this device hasn't been asked about it yet.</summary>
    public bool ShouldPromptForUpdate =>
        IsUpdateTrackingEnabled
        && HasPendingUpdates
        && !IsDownloadingLibrary
        && !string.IsNullOrEmpty(PendingUpdateKey)
        && (IsMandatoryUpdate
            || !string.Equals(
                prefs.Get(PrefKeys.CatalogUpdatePromptedHash, ""),
                PendingUpdateKey,
                StringComparison.Ordinal));

    public void MarkUpdatePrompted()
    {
        if (!string.IsNullOrEmpty(PendingUpdateKey))
            prefs.Set(PrefKeys.CatalogUpdatePromptedHash, PendingUpdateKey);
    }

    public void AcknowledgeHamburgerBadge()
    {
        if (!ShowHamburgerBadge) return;
        hamburgerAcknowledged = true;
        UpdateBadgeFlags();
        Notify();
    }

    public void AcknowledgeSettingsBadge()
    {
        if (!ShowSettingsBadge && !ShowResyncBadge) return;
        settingsAcknowledged = true;
        UpdateBadgeFlags();
        Notify();
    }

    public void AcknowledgeResyncBadge()
    {
        if (!ShowResyncBadge) return;
        resyncAcknowledged = true;
        UpdateBadgeFlags();
        Notify();
    }

    void AcknowledgeAll()
    {
        hamburgerAcknowledged = true;
        settingsAcknowledged = true;
        resyncAcknowledged = true;
        UpdateBadgeFlags();
    }

    void ClearPending()
    {
        HasPendingUpdates = false;
        PendingUpdateKey = null;
        PendingTotal = 0;
        IsMandatoryUpdate = false;
        PendingMessage = null;
        UpdateBadgeFlags();
    }

    void ResetAckIfHashChanged(string hash)
    {
        if (string.Equals(acknowledgedForHash, hash, StringComparison.Ordinal))
            return;
        acknowledgedForHash = hash;
        hamburgerAcknowledged = false;
        settingsAcknowledged = false;
        resyncAcknowledged = false;
    }

    void UpdateBadgeFlags()
    {
        var show = IsUpdateTrackingEnabled && HasPendingUpdates;
        ShowHamburgerBadge = show && !hamburgerAcknowledged;
        ShowSettingsBadge = show && !settingsAcknowledged;
        ShowResyncBadge = show && !resyncAcknowledged;
    }

    static string DescribeFailure(Exception ex) => ex switch
    {
        TaskCanceledException => " (Request timed out.)",
        HttpRequestException httpEx when httpEx.StatusCode.HasValue =>
            $" (Server responded with {(int)httpEx.StatusCode.Value}.)",
        HttpRequestException => " (Could not reach the server.)",
        _ => string.Empty
    };

    void Notify() => Changed?.Invoke();
}
