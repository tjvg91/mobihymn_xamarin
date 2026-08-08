using System.Text;
using System.Text.RegularExpressions;
using MobiHymn4.Shared.Models;
using MobiHymn4.Shared.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MobiHymn4.Shared;

public sealed class ApiHymnLyricsSource : IHymnLyricsSource
{
    readonly HttpClient http;
    readonly IHymnCatalogStore? catalogStore;
    readonly IHymnAccessPolicy? access;
    readonly Dictionary<string, Hymn> cache = new(StringComparer.OrdinalIgnoreCase);
    readonly object gate = new();
    HymnCatalogMeta? catalogCache;
    List<SearchDoc>? searchIndex;
    Task? indexLoadTask;
    Task? hydrateTask;

    public ApiHymnLyricsSource(
        HttpClient http,
        IHymnCatalogStore? catalogStore = null,
        IHymnAccessPolicy? access = null)
    {
        this.http = http;
        this.catalogStore = catalogStore;
        this.access = access;
    }

    public async Task<Hymn> GetHymnAsync(string number, CancellationToken cancellationToken = default)
    {
        number = (number ?? "").Trim().ToLowerInvariant();
        if (!Regex.IsMatch(number, @"^\d+[stf]?$"))
            throw new ArgumentException("Invalid hymn number.", nameof(number));

        lock (gate)
        {
            if (cache.TryGetValue(number, out var cached))
                return cached;
        }

        if (await UseLocalCatalogOnlyAsync().ConfigureAwait(false))
        {
            await EnsureLocalCatalogReadyAsync(cancellationToken).ConfigureAwait(false);
            lock (gate)
            {
                if (cache.TryGetValue(number, out var fromMem))
                    return fromMem;
            }

            if (catalogStore != null)
            {
                var json = await catalogStore.GetHymnJsonAsync(number, cancellationToken).ConfigureAwait(false);
                var hymn = HymnLyricsParser.TryParseJsonRecord(json ?? "", number);
                if (hymn != null)
                {
                    lock (gate)
                        cache[number] = hymn;
                    return hymn;
                }
            }

            throw new InvalidOperationException(
                $"Hymn #{number} is not in your downloaded catalog. Refresh the library in Settings.");
        }

        Hymn fetched;
        try
        {
            var json = await http.GetStringAsync(HymnApiPaths.ClientHymnJsonUrl(number), cancellationToken);
            fetched = HymnLyricsParser.TryParseJsonRecord(json, number)
                ?? throw new InvalidOperationException($"No JSON hymn record for #{number}");
        }
        catch
        {
            var bytes = await http.GetByteArrayAsync(HymnApiPaths.ClientLyricsUrl(number), cancellationToken);
            var raw = HymnLyricsParser.DecodeLyricsBytes(bytes);
            fetched = HymnLyricsParser.ParseHtmlLyrics(raw, number);
        }

        lock (gate)
            cache[number] = fetched;

        return fetched;
    }

    public async Task<HymnCatalogMeta> GetCatalogMetaAsync(CancellationToken cancellationToken = default)
    {
        if (catalogCache != null)
            return catalogCache;

        // Installed PWA: always use the downloaded catalog (works offline).
        if (await UseLocalCatalogOnlyAsync().ConfigureAwait(false))
            return await GetCatalogMetaFromLocalAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await GetCatalogMetaFreshAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Browser / flaky network: fall back to a downloaded library if one exists.
            var local = await TryBuildCatalogMetaFromLocalAsync(cancellationToken).ConfigureAwait(false);
            if (local != null)
                return local;
            throw;
        }
    }

    public async Task<HymnCatalogMeta> GetCatalogMetaFreshAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, HymnApiPaths.ClientMetaUrl);
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var meta = JsonConvert.DeserializeObject<HymnCatalogMeta>(text) ?? new HymnCatalogMeta();
        catalogCache = meta;
        return meta;
    }

    async Task<HymnCatalogMeta?> TryBuildCatalogMetaFromLocalAsync(CancellationToken cancellationToken)
    {
        if (catalogStore == null)
            return null;

        try
        {
            var count = await catalogStore.CountAsync(cancellationToken).ConfigureAwait(false);
            if (count <= 0)
                return null;
            return await GetCatalogMetaFromLocalAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    async Task<HymnCatalogMeta> GetCatalogMetaFromLocalAsync(CancellationToken cancellationToken)
    {
        await HydrateLocalCatalogAsync(cancellationToken).ConfigureAwait(false);

        List<HymnCatalogEntry> entries;
        lock (gate)
        {
            entries = (searchIndex ?? new List<SearchDoc>())
                .Select(d => new HymnCatalogEntry
                {
                    Number = d.Number,
                    FirstLine = d.FirstLine,
                    Title = d.FirstLine
                })
                .OrderBy(e => HymnSortNumber(e.Number))
                .ThenBy(e => e.Number, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (entries.Count == 0)
        {
            throw new InvalidOperationException(
                "No hymns in your downloaded catalog. Download or refresh the library in Settings.");
        }

        var meta = new HymnCatalogMeta
        {
            Total = entries.Count,
            Hymns = entries
        };
        catalogCache = meta;
        return meta;
    }

    public void InvalidateLocalCaches()
    {
        lock (gate)
        {
            cache.Clear();
            catalogCache = null;
            searchIndex = null;
            indexLoadTask = null;
            hydrateTask = null;
        }
    }

    public Task WarmSearchIndexAsync(CancellationToken cancellationToken = default) =>
        EnsureSearchIndexAsync(cancellationToken);

    public async Task SyncCatalogFromNetworkAsync(
        IProgress<(int Completed, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (catalogStore == null)
            throw new InvalidOperationException("No local catalog store is configured.");

        var hymns = await FetchAllHymnObjectsAsync(progress, cancellationToken).ConfigureAwait(false);
        progress?.Report((hymns.Count, Math.Max(hymns.Count, 1)));
        var payload = new JArray(hymns);
        await catalogStore.ReplaceAllJsonAsync(payload.ToString(Formatting.None), cancellationToken)
            .ConfigureAwait(false);
        ApplyCatalogObjects(hymns);
    }

    public async Task HydrateLocalCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (catalogStore == null)
            return;

        hydrateTask ??= LoadFromStoreAsync(CancellationToken.None);
        await hydrateTask.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task<IReadOnlyList<HymnSearchHit>> SearchLyricsAsync(
        string query, CancellationToken cancellationToken = default)
    {
        var needle = (query ?? "").Trim();
        if (needle.Length < 2)
            return Array.Empty<HymnSearchHit>();

        var index = await EnsureSearchIndexAsync(cancellationToken);
        var hits = new List<HymnSearchHit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var doc in index)
        {
            foreach (var line in doc.Lines)
            {
                if (line.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var key = $"{doc.Number}\0{SortKey(line)}";
                if (!seen.Add(key))
                    continue;

                hits.Add(new HymnSearchHit
                {
                    Number = doc.Number,
                    Line = line
                });
            }
        }

        return hits
            .OrderBy(h => SortKey(h.Line), StringComparer.OrdinalIgnoreCase)
            .ThenBy(h => h.Number, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<HymnSearchHit>> SearchMetadataAsync(
        string query, string field, CancellationToken cancellationToken = default)
    {
        var needle = StripPunctuation((query ?? "").Trim());
        if (needle.Length < 1)
            return Array.Empty<HymnSearchHit>();

        var index = await EnsureSearchIndexAsync(cancellationToken);
        var hits = new List<HymnSearchHit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var doc in index)
        {
            foreach (var (label, value) in MetadataValues(doc, field))
            {
                if (!MetadataMatches(field, value, query ?? "", needle))
                    continue;
                var key = $"{doc.Number}\0{StripPunctuation(value)}";
                if (!seen.Add(key))
                    continue;
                hits.Add(new HymnSearchHit
                {
                    Number = doc.Number,
                    Line = value,
                    Reason = label
                });
            }
        }

        return hits
            .OrderBy(h => StripPunctuation(h.Line), StringComparer.OrdinalIgnoreCase)
            .ThenBy(h => h.Number, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<HymnSearchGroup>> SearchMetadataGroupsAsync(
        string query, string field, CancellationToken cancellationToken = default)
    {
        var needle = StripPunctuation((query ?? "").Trim());
        if (needle.Length < 1 && !string.Equals(field, "key", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<HymnSearchGroup>();
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<HymnSearchGroup>();

        var index = await EnsureSearchIndexAsync(cancellationToken);
        var raw = new List<(string Category, string GroupValue, SearchDoc Doc)>();

        foreach (var doc in index)
        {
            foreach (var (label, value) in MetadataValues(doc, field))
            {
                if (!MetadataMatches(field, value, query ?? "", needle))
                    continue;
                raw.Add((label, value, doc));
            }
        }

        return raw
            .GroupBy(
                hit => $"{hit.Category}\0{hit.GroupValue}",
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(g =>
            {
                if (string.Equals(field, "verse", StringComparison.OrdinalIgnoreCase))
                {
                    var sk = VerseReferenceNormalizer.SortKey(g.First().GroupValue);
                    return (sk.Book, sk.Chapter, sk.Verse, StripPunctuation(g.First().GroupValue));
                }
                return (0, 0, 0, StripPunctuation(g.First().GroupValue));
            })
            .ThenBy(g => g.First().Category, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                var hymns = g
                    .GroupBy(h => h.Doc.Number, StringComparer.OrdinalIgnoreCase)
                    .Select(hg => hg.First().Doc)
                    .OrderBy(d => HymnSortNumber(d.Number))
                    .ThenBy(d => d.Number, StringComparer.OrdinalIgnoreCase)
                    .Select(d => new HymnSearchHit
                    {
                        Number = d.Number,
                        Line = string.IsNullOrWhiteSpace(d.FirstLine)
                            ? "No first line available"
                            : d.FirstLine
                    })
                    .ToList();

                return new HymnSearchGroup
                {
                    Title = first.GroupValue,
                    Category = first.Category,
                    Hymns = hymns
                };
            })
            .ToList();
    }

    /// <summary>
    /// MAUI strips punctuation before metadata match so "8787" hits "8.7.8.7.".
    /// Verse expands book aliases (Gen → Genesis). Key uses music-key canonicalization.
    /// </summary>
    static bool MetadataMatches(string field, string value, string rawQuery, string strippedNeedle)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (string.Equals(field, "key", StringComparison.OrdinalIgnoreCase))
            return KeyMatches(rawQuery, value);

        if (string.Equals(field, "verse", StringComparison.OrdinalIgnoreCase))
        {
            var needle = StripPunctuation(VerseReferenceNormalizer.Normalize(rawQuery));
            if (needle.Length < 1)
                return false;
            var hay = StripPunctuation(VerseReferenceNormalizer.Normalize(value));
            return hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        if (strippedNeedle.Length < 1)
            return false;

        return StripPunctuation(value)
            .IndexOf(strippedNeedle, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool KeyMatches(string query, string tuneKey)
    {
        if (TryCanonicalizeMusicKey(query, out var canonicalQuery))
        {
            return TryCanonicalizeMusicKey(tuneKey, out var canonicalKey)
                && canonicalKey == canonicalQuery;
        }

        var normalizedQuery = NormalizeMusicKeyText(query);
        var normalizedKey = NormalizeMusicKeyText(tuneKey);
        return !string.IsNullOrWhiteSpace(normalizedQuery)
            && normalizedKey.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool TryCanonicalizeMusicKey(string value, out string? canonical)
    {
        canonical = null;
        var normalized = NormalizeMusicKeyText(value);
        if (normalized.Length == 0)
            return false;

        var keyMatch = Regex.Match(
            normalized,
            @"^(?<root>[a-g])(?<accidental>flat|sharp)?(?<mode>m|minor|maj|major)?$",
            RegexOptions.IgnoreCase);
        if (!keyMatch.Success)
            return false;

        var rootPitch = keyMatch.Groups["root"].Value.ToLowerInvariant() switch
        {
            "c" => 0,
            "d" => 2,
            "e" => 4,
            "f" => 5,
            "g" => 7,
            "a" => 9,
            "b" => 11,
            _ => 0
        };

        var accidental = keyMatch.Groups["accidental"].Value;
        if (accidental.Equals("sharp", StringComparison.OrdinalIgnoreCase))
            rootPitch++;
        else if (accidental.Equals("flat", StringComparison.OrdinalIgnoreCase))
            rootPitch--;

        rootPitch = (rootPitch + 12) % 12;

        var mode = keyMatch.Groups["mode"].Value;
        var canonicalMode = mode.Equals("m", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("minor", StringComparison.OrdinalIgnoreCase)
                ? "minor"
                : "major";

        canonical = $"{rootPitch}:{canonicalMode}";
        return true;
    }

    static string NormalizeMusicKeyText(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0)
            return normalized;

        normalized = normalized
            .Replace("♭", " flat ", StringComparison.Ordinal)
            .Replace("♯", " sharp ", StringComparison.Ordinal)
            .Replace("#", " sharp ", StringComparison.Ordinal);

        normalized = Regex.Replace(
            normalized,
            @"^([A-Ga-g])\s*[bB](?=\s*(?:m|minor|major|maj)?\s*$)",
            "$1 flat ",
            RegexOptions.IgnoreCase);

        normalized = Regex.Replace(normalized, @"\s+", string.Empty);
        return new string(normalized
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    static IEnumerable<(string Label, string Value)> MetadataValues(SearchDoc doc, string field) =>
        field.ToLowerInvariant() switch
        {
            "author" => Flatten("Author", doc.Author)
                .Concat(Flatten("Composer", doc.TuneComposer)),
            "metre" => Flatten("Metre", doc.Metre),
            "key" => Flatten("Key", doc.TuneKey),
            "verse" => doc.Verses.Select(v => ("Verse", v)),
            _ => Enumerable.Empty<(string, string)>()
        };

    static int HymnSortNumber(string? number)
    {
        if (string.IsNullOrWhiteSpace(number)) return int.MaxValue;
        var digits = new string(number.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n : int.MaxValue;
    }

    static string StripPunctuation(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return new string(s.Where(c => !char.IsPunctuation(c)).ToArray());
    }

    static string SortKey(string? line)
    {
        if (string.IsNullOrEmpty(line)) return "";
        var chars = line.Where(c => !char.IsPunctuation(c) && !char.IsSymbol(c)).ToArray();
        return new string(chars).TrimStart();
    }

    static IEnumerable<(string Label, string Value)> Flatten(string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;
        yield return (label, value.Trim());
    }

    async Task<List<SearchDoc>> EnsureSearchIndexAsync(CancellationToken cancellationToken)
    {
        if (searchIndex != null)
            return searchIndex;

        if (await UseLocalCatalogOnlyAsync().ConfigureAwait(false))
        {
            await EnsureLocalCatalogReadyAsync(cancellationToken).ConfigureAwait(false);
            return searchIndex ?? new List<SearchDoc>();
        }

        indexLoadTask ??= LoadSearchIndexFromNetworkAsync(CancellationToken.None);
        await indexLoadTask.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return searchIndex ?? new List<SearchDoc>();
    }

    async Task EnsureLocalCatalogReadyAsync(CancellationToken cancellationToken)
    {
        if (access != null)
            await access.InitializeAsync().ConfigureAwait(false);

        if (access is { IsLibraryDownloaded: false })
        {
            throw new InvalidOperationException(
                "Download the hymn library in Settings before reading or searching hymns.");
        }

        await HydrateLocalCatalogAsync(cancellationToken).ConfigureAwait(false);

        if (searchIndex == null || searchIndex.Count == 0)
        {
            throw new InvalidOperationException(
                "No hymns in your downloaded catalog. Download or refresh the library in Settings.");
        }
    }

    async Task<bool> UseLocalCatalogOnlyAsync()
    {
        if (access == null)
            return false;
        await access.InitializeAsync().ConfigureAwait(false);
        return access.UseLocalCatalogOnly;
    }

    async Task LoadFromStoreAsync(CancellationToken cancellationToken)
    {
        if (catalogStore == null)
            return;

        var json = await catalogStore.GetAllJsonAsync(cancellationToken).ConfigureAwait(false);
        JArray arr;
        try { arr = JArray.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json); }
        catch { arr = new JArray(); }

        var objects = arr.OfType<JObject>().ToList();
        ApplyCatalogObjects(objects);
    }

    async Task LoadSearchIndexFromNetworkAsync(CancellationToken cancellationToken)
    {
        var hymns = await FetchAllHymnObjectsAsync(null, cancellationToken).ConfigureAwait(false);
        ApplyCatalogObjects(hymns);
    }

    async Task<List<JObject>> FetchAllHymnObjectsAsync(
        IProgress<(int Completed, int Total)>? progress,
        CancellationToken cancellationToken)
    {
        const int pageSize = 200;

        var firstJson = await http.GetStringAsync(
            HymnApiPaths.ClientHymnsPageUrl(0, pageSize),
            cancellationToken).ConfigureAwait(false);
        var firstRoot = JObject.Parse(firstJson);
        var firstArr = firstRoot["hymns"] as JArray ?? new JArray();
        var total = firstRoot.Value<int?>("total") ?? firstArr.Count;
        var hymns = new List<JObject>(Math.Max(total, firstArr.Count));
        AppendObjects(hymns, firstArr);
        progress?.Report((hymns.Count, Math.Max(total, 1)));

        // Sequential pages so progress updates stay meaningful.
        for (var offset = pageSize; offset < total; offset += pageSize)
        {
            var json = await http.GetStringAsync(
                HymnApiPaths.ClientHymnsPageUrl(offset, pageSize),
                cancellationToken).ConfigureAwait(false);
            AppendObjects(hymns, JObject.Parse(json)["hymns"] as JArray ?? new JArray());
            progress?.Report((hymns.Count, Math.Max(total, 1)));
        }

        return hymns;
    }

    void ApplyCatalogObjects(IReadOnlyList<JObject> hymns)
    {
        var list = new List<SearchDoc>(hymns.Count);
        lock (gate)
        {
            cache.Clear();
            foreach (var obj in hymns)
            {
                var doc = SearchDoc.FromJson(obj);
                if (doc == null) continue;
                list.Add(doc);

                var hymn = HymnLyricsParser.HymnFromJsonObject(obj, doc.Number);
                if (hymn != null)
                    cache[hymn.Number] = hymn;
            }

            searchIndex = list;
            indexLoadTask = null;
            hydrateTask = Task.CompletedTask;
        }
    }

    static void AppendObjects(List<JObject> list, JArray arr)
    {
        foreach (var token in arr)
        {
            if (token is JObject obj)
                list.Add(obj);
        }
    }

    public void Remember(Hymn hymn)
    {
        if (hymn == null || string.IsNullOrWhiteSpace(hymn.Number))
            return;
        lock (gate)
            cache[hymn.Number] = hymn;
    }

    /// <summary>Lean in-memory row for local search (plain lyric lines, no HTML).</summary>
    sealed class SearchDoc
    {
        public string Number { get; init; } = "";
        public string[] Lines { get; init; } = Array.Empty<string>();
        public string? FirstLine { get; init; }
        public string? Author { get; init; }
        public string? Metre { get; init; }
        public string? TuneComposer { get; init; }
        public string? TuneKey { get; init; }
        public string[] Verses { get; init; } = Array.Empty<string>();

        public static SearchDoc? FromJson(JObject obj)
        {
            var number = (obj.Value<string>("number") ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(number)) return null;

            var raw = (obj.Value<string>("lyrics") ?? "")
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Replace('\uFFFD', '\'');

            var lines = raw.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToArray();

            static string? N(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

            string[] VersesFrom(JToken? token)
            {
                if (token == null || token.Type == JTokenType.Null)
                    return Array.Empty<string>();
                IEnumerable<string?> vals = token.Type == JTokenType.Array
                    ? token.Values<string>()
                    : new[] { token.Value<string>() };
                return vals
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            var firstLine = N(obj.Value<string>("firstLine")) ?? (lines.Length > 0 ? lines[0] : null);

            return new SearchDoc
            {
                Number = number,
                Lines = lines,
                FirstLine = firstLine,
                Author = N(obj.Value<string>("author")),
                Metre = N(obj.Value<string>("metre")),
                TuneComposer = N(obj.Value<string>("tuneComposer")),
                TuneKey = N(obj.Value<string>("tuneKey")),
                Verses = VersesFrom(obj["verses"] ?? obj["verseRef"])
            };
        }
    }
}

public sealed class AgentApiClient : IAgentApi
{
    readonly HttpClient http;

    public AgentApiClient(HttpClient http) => this.http = http;

    public async Task<AgentSearchResponse> SearchAsync(
        string query, int limit, string? mode = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["query"] = query,
            ["limit"] = limit
        };
        if (!string.IsNullOrWhiteSpace(mode) && !string.Equals(mode, "auto", StringComparison.OrdinalIgnoreCase))
            body["mode"] = mode;

        var json = JsonConvert.SerializeObject(body);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(HymnApiPaths.ClientAgentSearchUrl, content, ct);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(ct);
        return JsonConvert.DeserializeObject<AgentSearchResponse>(text) ?? new AgentSearchResponse();
    }

    public async Task<AgentChatResponse> ChatAsync(
        IReadOnlyList<AgentChatMessageDto> messages,
        int limit,
        string? sessionId = null,
        string? mode = null,
        CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["messages"] = messages,
            ["limit"] = limit
        };
        if (!string.IsNullOrWhiteSpace(sessionId))
            body["sessionId"] = sessionId;
        if (!string.IsNullOrWhiteSpace(mode) && !string.Equals(mode, "auto", StringComparison.OrdinalIgnoreCase))
            body["mode"] = mode;

        var json = JsonConvert.SerializeObject(body);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(HymnApiPaths.ClientAgentChatUrl, content, ct);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(ct);
        return JsonConvert.DeserializeObject<AgentChatResponse>(text) ?? new AgentChatResponse();
    }
}
