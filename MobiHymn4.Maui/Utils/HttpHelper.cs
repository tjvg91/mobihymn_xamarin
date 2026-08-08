using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;   
using System.Text.RegularExpressions;
using System.Threading;
using System.Text;

using MobiHymn4.Models;
using Newtonsoft.Json;
using MvvmHelpers;
using HtmlAgilityPack;
using Polly;
using System.Net;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;

namespace MobiHymn4.Utils
{
	public class HttpHelper
	{
		HttpClient httpClient;
        HttpClient httpClient2;

		string jsonFile = "lyrics.mb";
        string backupFile = "lyrics_backup.mb";
        string checkpointFile = "download_checkpoint.json";
        string folderName = "mobihymn";
        string folderMidiName = "midi";
        string message = "Could not complete download";
        const int SaveEveryStreamHymns = 100;

        public HttpHelper()
		{
			httpClient = new HttpClient();
            httpClient2 = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(30);
            httpClient2.Timeout = TimeSpan.FromMinutes(30);
        }

		public async Task<HymnList> DownloadHymns(
            IProgress<string> progress,
            CancellationToken cts,
            HymnList origList = null,
            bool forceSync = false,
            bool excludeMidi = true,
            bool trackCheckpoint = true,
            bool skipExisting = false,
            bool updateResyncVersion = true)
		{
            HymnList hymnList;
            HymnList syncables = new HymnList();
            var resumed = false;
            var nextSaveAt = SaveEveryStreamHymns;

            if (trackCheckpoint)
            {
                var checkpoint = await LoadCheckpoint();
                if (checkpoint != null && checkpoint.NextSyncDetailIndex == null
                    && checkpoint.ForceSync == forceSync && checkpoint.MissingOnly == skipExisting)
                {
                    hymnList = await ReadHymns();
                    resumed = hymnList.Count > 0;
                    if (resumed)
                        progress?.Report($"Resuming download ({hymnList.Count} already saved)…");
                    else
                        progress?.Report("Starting download…");
                }
                else
                {
                    hymnList = skipExisting && origList != null ? new HymnList(origList) : new HymnList();
                    await SaveCheckpoint(new DownloadCheckpoint
                    {
                        NextBaseIndex = 1,
                        ForceSync = forceSync,
                        MissingOnly = skipExisting,
                        SavedHymnCount = hymnList.Count
                    });
                }
            }
            else
            {
                hymnList = skipExisting && origList != null ? new HymnList(origList) : new HymnList();
            }

            // On resume / missing-only, skip hymns already present while re-streaming.
            var treatAsSkipExisting = skipExisting || resumed;
            var existingNumbers = treatAsSkipExisting
                ? new HashSet<string>(hymnList.Select(h => h.Number), StringComparer.OrdinalIgnoreCase)
                : null;

            progress?.Report("Connecting to hymn stream…");

            using var request = new HttpRequestMessage(HttpMethod.Get, Globals.HYMN_STREAM_URL);
            request.Headers.Accept.ParseAdd("text/event-stream");
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };

            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cts).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = Preferences.Default.Get(PreferencesVar.HYMN_TOTAL, 0);
            var processed = 0;
            var receivedDone = false;
            var verb = skipExisting ? "Downloaded" : forceSync ? "Syncing" : "Downloaded";

            // Separate CTS so we can abandon the SSE connection immediately after `done`
            // instead of waiting for the server/HttpClient timeout while disposing the stream.
            using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(cts);
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(streamCts.Token).ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024);

                while (!streamCts.IsCancellationRequested)
                {
                    string line;
                    try
                    {
                        line = await reader.ReadLineAsync(streamCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (receivedDone)
                    {
                        break;
                    }

                    if (line == null)
                        break;

                    if (line.Length == 0 || line.StartsWith(':') || line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var json = line.Substring(5).TrimStart();
                    if (string.IsNullOrWhiteSpace(json))
                        continue;

                    HymnStreamEvent evt;
                    try
                    {
                        evt = JsonConvert.DeserializeObject<HymnStreamEvent>(json);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"SSE parse: {ex.Message}");
                        continue;
                    }

                    if (evt == null || string.IsNullOrEmpty(evt.Type))
                        continue;

                    if (evt.Total > 0)
                    {
                        total = evt.Total;
                        Preferences.Default.Set(PreferencesVar.HYMN_TOTAL, total);
                    }

                    if (string.Equals(evt.Type, "start", StringComparison.OrdinalIgnoreCase))
                    {
                        progress?.Report(total > 0
                            ? $"Downloading 0/{total}…"
                            : "Downloading hymns…");
                        continue;
                    }

                    if (string.Equals(evt.Type, "done", StringComparison.OrdinalIgnoreCase))
                    {
                        processed = evt.Processed > 0 ? evt.Processed : processed;
                        if (evt.Total > 0)
                            total = evt.Total;
                        receivedDone = true;
                        progress?.Report(total > 0
                            ? $"Saving {processed}/{total}…"
                            : "Saving hymns…");
                        break;
                    }

                    if (!string.Equals(evt.Type, "hymn", StringComparison.OrdinalIgnoreCase) || evt.Hymn == null)
                        continue;

                    processed = evt.Processed > 0 ? evt.Processed : processed + 1;
                    if (evt.Total > 0)
                        total = evt.Total;

                    var number = (evt.Hymn.Number ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(number))
                        continue;

                    if (treatAsSkipExisting && existingNumbers != null && existingNumbers.Contains(number))
                    {
                        progress?.Report(total > 0
                            ? $"{verb} {processed}/{total} (#{number})…"
                            : $"{verb} hymn #{number}…");
                        continue;
                    }

                    Hymn newHymn;
                    try
                    {
                        newHymn = HymnFromStream(evt.Hymn);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"DownloadHymns #{number}: {ex.Message}");
                        progress?.Report(total > 0
                            ? $"{verb} {processed}/{total} (#{number})…"
                            : $"{verb} hymn #{number}…");
                        continue;
                    }

                    var origHymn = origList?[newHymn.Number];
                    if (forceSync && (origHymn == null
                        || origHymn.Lyrics != newHymn.Lyrics
                        || newHymn.FirstLine != origHymn.FirstLine))
                        syncables.Add(newHymn);

                    if (origHymn != null && !string.IsNullOrEmpty(origHymn.MidiFileName))
                        newHymn.MidiFileName = origHymn.MidiFileName;
                    else if (treatAsSkipExisting)
                    {
                        var existing = hymnList[newHymn.Number];
                        if (existing != null && !string.IsNullOrEmpty(existing.MidiFileName))
                            newHymn.MidiFileName = existing.MidiFileName;
                    }

                    var existingIndex = hymnList.FindIndex(h =>
                        h.Number != null && h.Number.Equals(newHymn.Number, StringComparison.OrdinalIgnoreCase));
                    if (existingIndex >= 0)
                        hymnList[existingIndex] = newHymn;
                    else
                        hymnList.Add(newHymn);

                    existingNumbers?.Add(newHymn.Number);

                    if (!excludeMidi)
                        await DownloadMIDI(number, cts).ConfigureAwait(false);

                    progress?.Report(total > 0
                        ? $"{verb} {processed}/{total} (#{number})…"
                        : $"{verb} hymn #{number}…");

                    if (trackCheckpoint && processed >= nextSaveAt)
                    {
                        await PersistDownloadProgress(hymnList, processed, forceSync, skipExisting).ConfigureAwait(false);
                        nextSaveAt = processed + SaveEveryStreamHymns;
                    }
                }

                if (cts.IsCancellationRequested && !receivedDone)
                        {
                            progress?.Report(message);
                    if (trackCheckpoint)
                        await PersistDownloadProgress(hymnList, Math.Max(1, processed), forceSync, skipExisting).ConfigureAwait(false);
                    return hymnList;
                }

                if (!receivedDone && hymnList.Count == 0)
                    throw new Exception("Hymn stream ended before any hymns were received.");

                var saveTotal = total > 0 ? total : hymnList.Count;
                var saveProcessed = processed > 0 ? processed : hymnList.Count;

                // Save before disposing the SSE stream — disposing an unfinished response can hang
                // for a long time if the server leaves the connection open after `done`.
                if (skipExisting || !forceSync || syncables.Count > 0 || hymnList.Count > 0)
                {
                    progress?.Report(saveTotal > 0
                        ? $"Saving {saveProcessed}/{saveTotal}…"
                        : "Saving hymns…");
                    await SaveHymns(hymnList).ConfigureAwait(false);
                }

                if (trackCheckpoint)
                    await ClearCheckpoint().ConfigureAwait(false);

                if (trackCheckpoint && updateResyncVersion)
                    _ = UpdateResyncVersionAsync();

                if (saveTotal > 0)
                    Preferences.Default.Set(PreferencesVar.HYMN_TOTAL, saveTotal);

                progress?.Report(saveTotal > 0
                    ? $"Saved {saveProcessed}/{saveTotal}"
                    : "Saved hymns");

                // Abort the open SSE connection before Dispose awaits unread bytes.
                try { streamCts.Cancel(); } catch { /* ignore */ }

                return hymnList;
            }
            catch (OperationCanceledException) when (receivedDone)
            {
                // Expected after cancelling the stream on `done`.
            }
            finally
            {
                try { streamCts.Cancel(); } catch { /* ignore */ }
            }

            if (cts.IsCancellationRequested && !receivedDone)
            {
                progress?.Report(message);
                if (trackCheckpoint)
                    await PersistDownloadProgress(hymnList, Math.Max(1, processed), forceSync, skipExisting).ConfigureAwait(false);
            }

            return hymnList;
		}

        public async Task<CatalogMeta> GetCatalogMetaAsync(CancellationToken cts)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Globals.HYMN_META_URL);
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            using var response = await httpClient.SendAsync(request, cts).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cts).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<CatalogMeta>(json)
                ?? throw new InvalidDataException("The server returned invalid catalog metadata.");
        }

        public async Task<AgentSearchResponse> SearchAgentAsync(
            string query,
            CancellationToken cts,
            int limit = 30)
        {
            var body = new JObject
            {
                ["query"] = query ?? string.Empty,
                ["limit"] = limit
            };
            AppendAgentMode(body);
            using var content = new StringContent(
                body.ToString(Formatting.None),
                Encoding.UTF8,
                "application/json");
            using var response = await httpClient.PostAsync(
                Globals.HYMN_AGENT_SEARCH_URL,
                content,
                cts).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cts).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<AgentSearchResponse>(json)
                ?? throw new InvalidDataException("The server returned an invalid agent search response.");
        }

        public async Task<AgentChatResponse> ChatAgentAsync(
            IEnumerable<object> messages,
            CancellationToken cts,
            string sessionId = null,
            int limit = 30,
            IEnumerable<string> excludeNumbers = null)
        {
            var body = new JObject
            {
                ["messages"] = JArray.FromObject(messages ?? Array.Empty<object>()),
                ["limit"] = limit
            };
            if (!string.IsNullOrWhiteSpace(sessionId))
                body["sessionId"] = sessionId;

            var excludes = (excludeNumbers ?? Enumerable.Empty<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (excludes.Count > 0)
                body["excludeNumbers"] = JArray.FromObject(excludes);

            AppendAgentMode(body);

            using var content = new StringContent(
                body.ToString(Formatting.None),
                Encoding.UTF8,
                "application/json");
            using var response = await httpClient.PostAsync(
                Globals.HYMN_AGENT_CHAT_URL,
                content,
                cts).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cts).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<AgentChatResponse>(json)
                ?? throw new InvalidDataException("The server returned an invalid agent chat response.");
        }

        // Auto means "let the server decide" — omit mode from the JSON body entirely.
        static void AppendAgentMode(JObject body)
        {
            switch (Globals.Instance.AgentMode)
            {
                case AgentMode.Local:
                    body["mode"] = "local";
                    break;
                case AgentMode.Cloud:
                    body["mode"] = "cloud";
                    break;
            }
        }

        public async Task<CatalogDiff> GetCatalogChangesAsync(
            HymnList localCatalog,
            CancellationToken cts)
        {
            var hymns = new JArray((localCatalog ?? new HymnList()).Select(ToApiCatalogHymn));
            var body = new JObject { ["hymns"] = hymns };
            using var content = new StringContent(
                body.ToString(Formatting.None),
                Encoding.UTF8,
                "application/json");
            using var response = await httpClient.PostAsync(
                Globals.HYMN_CHANGES_URL,
                content,
                cts).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cts).ConfigureAwait(false);
            var result = JsonConvert.DeserializeObject<CatalogDiff>(json)
                ?? throw new InvalidDataException("The server returned an invalid catalog diff.");
            result.Normalize();
            return result;
        }

        public async Task<HymnList> ApplyCatalogChangesAsync(
            HymnList localCatalog,
            CatalogDiff diff,
            IProgress<string> progress,
            CancellationToken cts)
        {
            if (diff == null)
                throw new ArgumentNullException(nameof(diff));

            if (diff.NumbersIncomplete)
                throw new InvalidDataException(
                    "The change list is incomplete. Use Resync All to update the full catalog.");

            var updated = new HymnList(localCatalog ?? new HymnList());
            var targetNumbers = new HashSet<string>(
                diff.AddedOrModifiedNumbers,
                StringComparer.OrdinalIgnoreCase);
            var received = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var removedNumbers = diff.GetRemovedNumbers();
            var completed = 0;
            var total = targetNumbers.Count + removedNumbers.Count;

            if (targetNumbers.Count > 0)
            {
                var numbersQuery = string.Join(",",
                    targetNumbers
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .Select(Uri.EscapeDataString));
                var streamUrl = $"{Globals.HYMN_STREAM_URL}&numbers={numbersQuery}";
                using var request = new HttpRequestMessage(HttpMethod.Get, streamUrl);
                request.Headers.Accept.ParseAdd("text/event-stream");
                request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
                using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cts).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(cts).ConfigureAwait(false);
                using var reader = new StreamReader(
                    stream,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 64 * 1024);

                while (!cts.IsCancellationRequested && received.Count < targetNumbers.Count)
                {
                    var line = await reader.ReadLineAsync(cts).ConfigureAwait(false);
                    if (line == null)
                        break;
                    if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var json = line.Substring(5).TrimStart();
                    if (string.IsNullOrWhiteSpace(json))
                        continue;

                    HymnStreamEvent evt;
                    try
                    {
                        evt = JsonConvert.DeserializeObject<HymnStreamEvent>(json);
                    }
                    catch (JsonException ex)
                    {
                        Debug.WriteLine($"Catalog partial sync SSE parse: {ex.Message}");
                        continue;
                    }

                    if (string.Equals(evt?.Type, "done", StringComparison.OrdinalIgnoreCase))
                        break;

                    if (!string.Equals(evt?.Type, "hymn", StringComparison.OrdinalIgnoreCase)
                        || evt.Hymn == null
                        || !targetNumbers.Contains(evt.Hymn.Number ?? string.Empty))
                        continue;

                    var hymn = HymnFromStream(evt.Hymn);
                    var existing = updated[hymn.Number];
                    if (existing != null && !string.IsNullOrWhiteSpace(existing.MidiFileName))
                        hymn.MidiFileName = existing.MidiFileName;

                    var index = updated.FindIndex(item =>
                        string.Equals(item?.Number, hymn.Number, StringComparison.OrdinalIgnoreCase));
                    if (index >= 0)
                        updated[index] = hymn;
                    else
                        updated.Add(hymn);

                    if (received.Add(hymn.Number))
                    {
                        completed++;
                        progress?.Report($"Syncing {completed}/{Math.Max(1, total)} (#{hymn.Number})…");
                    }
                }
            }

            var missing = targetNumbers.Except(received, StringComparer.OrdinalIgnoreCase).ToList();
            if (missing.Count > 0)
                throw new InvalidDataException(
                    $"The server did not return hymn{(missing.Count == 1 ? string.Empty : "s")} {string.Join(", ", missing.Select(number => $"#{number}"))}.");

            foreach (var number in removedNumbers)
            {
                cts.ThrowIfCancellationRequested();
                updated.RemoveAll(hymn =>
                    string.Equals(hymn?.Number, number, StringComparison.OrdinalIgnoreCase));
                completed++;
                progress?.Report($"Syncing {completed}/{Math.Max(1, total)} (removed #{number})…");
            }

            await SaveHymns(updated).ConfigureAwait(false);
            return updated;
        }

        static JObject ToApiCatalogHymn(Hymn hymn)
        {
            var verses = hymn?.GetVerseReferences() ?? Enumerable.Empty<string>();
            return new JObject
            {
                ["number"] = hymn?.Number ?? string.Empty,
                ["title"] = hymn?.Name ?? string.Empty,
                ["firstLine"] = hymn?.FirstLine ?? string.Empty,
                ["lyrics"] = StoredLyricsToApiText(hymn?.Lyrics),
                ["midiFileName"] = hymn?.MidiFileName ?? string.Empty,
                ["author"] = hymn?.Author ?? string.Empty,
                ["metre"] = hymn?.Metre ?? string.Empty,
                ["tune"] = hymn?.Tune ?? string.Empty,
                ["tuneComposer"] = hymn?.TuneComposer ?? string.Empty,
                ["tuneKey"] = hymn?.TuneKey ?? string.Empty,
                // changes.py currently compares against "verseRef" (not "verses"); sending
                // both keeps this working even if/when the endpoint is updated to prefer "verses".
                ["verseRef"] = new JArray(verses),
                ["verses"] = new JArray(verses),
                ["tags"] = new JArray(hymn?.Tags ?? Array.Empty<string>()),
                ["year"] = hymn?.Year ?? string.Empty,
                // The server stores "no value" as "" rather than null for every optional field
                // above; normalizing locally-null fields the same way avoids every hymn missing
                // one of these values from showing up as falsely "modified" on every check.
                ["remark"] = hymn?.Remark ?? string.Empty
            };
        }

        static string StoredLyricsToApiText(string lyrics)
        {
            if (string.IsNullOrWhiteSpace(lyrics))
                return string.Empty;

            var normalized = SanitizeStoredLyrics(lyrics);
            normalized = Regex.Replace(normalized, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            var document = new HtmlDocument();
            document.LoadHtml(normalized);
            return WebUtility.HtmlDecode(document.DocumentNode.InnerText)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
        }

        public async Task<List<string>> FindMissingHymnNumbersAsync(HymnList local, CancellationToken cts)
        {
            string[] tunes = { "", "s", "t", "f" };
            var localNumbers = new HashSet<string>(
                local.Select(h => h.Number),
                StringComparer.OrdinalIgnoreCase);
            var missing = new List<string>();
            var i = 1;
            var done = false;

            while (!done && !cts.IsCancellationRequested)
            {
                for (var j = 0; j < tunes.Length; j++)
                {
                    if (cts.IsCancellationRequested)
                        break;

                    var number = $"{i}{tunes[j]}";
                    if (localNumbers.Contains(number))
                        continue;

                    try
                    {
                        var lyrics = await GetLyricsAsync(number).ConfigureAwait(false);
                        if (string.IsNullOrEmpty(lyrics) || new Regex("Error:", RegexOptions.IgnoreCase).IsMatch(lyrics))
                        {
                            if (j == 0)
                                done = true;
                            continue;
                        }

                        missing.Add(number);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"FindMissingHymnNumbersAsync #{number}: {ex.Message}");
                        if (j == 0)
                            done = true;
                    }
                }

                i++;
            }

            return missing;
        }

        public async Task<HymnList> SyncChanges(
            IProgress<string> progress,
            CancellationToken cts,
            ObservableRangeCollection<ResyncDetail> resyncDetails,
            HymnList origList,
            int startDetailIndex = 0)
        {
            HymnList updatedList = new HymnList(origList);
            var completedAll = true;

            for (var detailIndex = startDetailIndex; detailIndex < resyncDetails.Count; detailIndex++)
            {
                var resyncDetail = resyncDetails[detailIndex];
                if (cts.IsCancellationRequested)
                {
                    completedAll = false;
                    break;
                }

                switch(resyncDetail.Mode)
                {
                    case CRUD.Delete:
                        updatedList.RemoveAt(updatedList.FindIndex(hymn => hymn.Number.Equals(resyncDetail.Number)));
                        break;
                    case CRUD.Update:
                        if (resyncDetail.Number == "*")
                        {
                            if (resyncDetail.Type == ResyncType.Lyrics)
                                updatedList = await DownloadHymns(progress, cts, updatedList, false, true, trackCheckpoint: false);
                            else
                                updatedList = await DownloadAllMIDIs(updatedList, resyncDetail.Mode, progress, cts);
                        }
                        goto default;
                    case CRUD.Create:
                        if (resyncDetail.Type == ResyncType.Audio)
                        {
                            try
                            {
                                if (resyncDetail.Number == "*")
                                    updatedList = await DownloadAllMIDIs(updatedList, resyncDetail.Mode, progress, cts);
                                else if (await DownloadMIDI(resyncDetail.Number, cts))
                                    updatedList.Find(hymn => hymn.Number == resyncDetail.Number).MidiFileName = $"h{resyncDetail.Number}.mid";
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Sync MIDI {resyncDetail.Number}: {ex.Message}");
                            }
                        }
                        goto default;
                    default:
                        if(resyncDetail.Type == ResyncType.Lyrics)
                        {
                            var number = resyncDetail.Number;
                            try
                            {
                                var lyrics = await GetLyricsAsync(resyncDetail.Number);
                                var newHymn = ProcessLyrics(ref lyrics, number);
                                var origHymn = origList[number];

                                if (resyncDetail.Mode == CRUD.Create && origList[number] == null)
                                {
                                    var intNum = int.Parse(new Regex("[0-9]+").Match(number).Value);
                                    var intLastNum = int.Parse(new Regex("[0-9]+").Match(origList.Last().Number).Value);

                                    if (intNum - intLastNum == 1) updatedList.Add(newHymn);
                                    else updatedList.Insert(origList.FindIndex(hymn => hymn.Number == intNum + ""), newHymn);
                                }
                                else if (origList[number] != null && !origHymn.Lyrics.Equals(newHymn.Lyrics))
                                    updatedList[number] = newHymn;
                                progress.Report($"{Enum.GetName(resyncDetail.Mode.GetType(), resyncDetail.Mode)}d #{newHymn.Number} ({resyncDetail.Type})");
                            }
                            catch (Exception ex)
                            {
                                progress.Report($"Error syncing.{ex.Message}");
                            }
                        }
                        break;
                }

                await SaveHymns(updatedList);
                await SaveCheckpoint(new DownloadCheckpoint
                {
                    NextSyncDetailIndex = detailIndex + 1,
                    ForceSync = false,
                    SavedHymnCount = updatedList.Count
                });
            }

            if (completedAll)
            {
                await ClearCheckpoint();
                _ = UpdateResyncVersionAsync();
            }

            return updatedList;
        }

        public async Task<bool> SyncSingleHymn(
            string number,
            HymnList hymnList,
            IProgress<string> progress,
            CancellationToken cts) =>
            await SyncSingleHymnCore(number, hymnList, progress, cts, saveChanges: true);

        public async Task<(int Succeeded, int Failed)> SyncHymns(
            IEnumerable<string> numbers,
            HymnList hymnList,
            IProgress<string> progress,
            CancellationToken cts,
            bool saveToDisk = true)
        {
            var succeeded = 0;
            var failed = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var raw in numbers)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var number = raw.Trim().ToLowerInvariant();
                if (!seen.Add(number))
                    continue;

                if (!Regex.IsMatch(number, @"^\d+[stf]?$"))
                {
                    failed++;
                    continue;
                }

                try
                {
                    cts.ThrowIfCancellationRequested();
                    progress?.Report($"Syncing hymn #{number}…");
                    if (await SyncSingleHymnCore(number, hymnList, progress, cts, saveChanges: false))
                        succeeded++;
                    else
                        failed++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SyncHymns #{number}: {ex.Message}");
                    failed++;
                }
            }

            if (succeeded > 0 && saveToDisk)
                await SaveHymns(hymnList);

            return (succeeded, failed);
        }

        async Task<bool> SyncSingleHymnCore(
            string number,
            HymnList hymnList,
            IProgress<string> progress,
            CancellationToken cts,
            bool saveChanges)
        {
            if (string.IsNullOrWhiteSpace(number))
                return false;

            number = number.Trim().ToLowerInvariant();
            if (!Regex.IsMatch(number, @"^\d+[stf]?$"))
                return false;

            var lyrics = await GetLyricsAsync(number);
            cts.ThrowIfCancellationRequested();

            var newHymn = ProcessLyrics(ref lyrics, number);
            var hadMidi = false;
            var updatedExisting = false;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var index = hymnList.FindIndex(h => h?.Number == number);
                hadMidi = index >= 0 && !string.IsNullOrEmpty(hymnList[index].MidiFileName);
                updatedExisting = index >= 0;
                if (index >= 0)
                    hymnList[index] = newHymn;
                else
                    hymnList.Add(newHymn);
            });

            if (hadMidi && await DownloadMIDI(number, cts))
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                    newHymn.MidiFileName = $"h{number}.mid");
            }

            if (saveChanges)
                await SaveHymns(hymnList);

            progress?.Report(updatedExisting ? $"Re-synced hymn #{number}" : $"Downloaded hymn #{number}");
            return true;
        }

        private Hymn ProcessLyrics(ref string lyrics, string number)
        {
            lyrics = lyrics.Replace(Environment.NewLine, "<br/>");
            lyrics = Regex.Replace(lyrics, "TAGS>.+<pre>", "TAGS><pre>");
            lyrics = Regex.Replace(lyrics, "<pre>[^A-Z]+", "<pre>");
            lyrics = Regex.Replace(lyrics, "\\r<br\\/>", "<br/>");
            lyrics = Regex.Replace(lyrics, "<br\\/><\\/pre>", "</pre>");
            lyrics = Regex.Replace(lyrics, "\\r", "<br/>");

            if (string.IsNullOrEmpty(lyrics) || new Regex("Error:", RegexOptions.IgnoreCase).IsMatch(lyrics))
                throw new Exception(lyrics);

            // Each call gets its own document — the shared instance was not thread-safe
            // and crashed when multiple tunes downloaded in parallel.
            var doc = new HtmlDocument();
            doc.LoadHtml(lyrics);
            var preText = doc.DocumentNode.Descendants("pre").SingleOrDefault();
            if (preText == null)
                throw new Exception($"No lyrics body for hymn #{number}");

            var newHymn = new Hymn
            {
                Lyrics = lyrics,
                FirstLine = new Regex("<br>").Split(preText.InnerHtml)[0],
                Title = number.ToTitle(),
                Number = number
            };
            return newHymn;
        }

        static Hymn HymnFromStream(HymnStreamPayload payload)
        {
            var number = (payload.Number ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(number))
                throw new Exception("Stream hymn missing number");

            var raw = NormalizeStreamNewlines(payload.Lyrics);

            var firstLine = string.IsNullOrWhiteSpace(payload.FirstLine)
                ? raw.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? number
                : payload.FirstLine.Trim();

            // Use <br> (not <pre>). HtmlCompat often ignores/messes up <br> inside <pre>,
            // which made the whole hymn soft-wrap as one block.
            var htmlBody = System.Net.WebUtility.HtmlEncode(raw).Replace("\n", "<br>");

            return new Hymn
            {
                Number = number,
                Title = number.ToTitle(),
                Name = NullIfWhiteSpace(payload.Title),
                FirstLine = firstLine,
                Lyrics = htmlBody,
                MidiFileName = NullIfWhiteSpace(payload.MidiFileName),
                Author = NullIfWhiteSpace(payload.Author),
                Metre = NullIfWhiteSpace(payload.Metre),
                Tune = NullIfWhiteSpace(payload.Tune),
                TuneComposer = NullIfWhiteSpace(payload.TuneComposer),
                TuneKey = NullIfWhiteSpace(payload.TuneKey),
                Verses = StringArrayFromToken(payload.Verses ?? payload.VerseRef),
                Tags = StringArrayFromToken(payload.Tags),
                Year = NullIfWhiteSpace(payload.Year),
                Remark = NullIfWhiteSpace(payload.Remark)
            };
        }

        static string NullIfWhiteSpace(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        static string[] StringArrayFromToken(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return Array.Empty<string>();

            var values = token.Type == JTokenType.Array
                ? token.Values<string>()
                : new[] { token.Value<string>() };

            return values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        static string NormalizeStreamNewlines(string lyrics)
        {
            if (string.IsNullOrEmpty(lyrics))
                return string.Empty;

            return lyrics
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Replace('\u2028', '\n')
                .Replace('\u2029', '\n')
                .Replace('\uFFFD', '\'');
        }

        /// <summary>
        /// Repairs streamed lyrics artifacts (TAGS> prefix, wrapping &lt;pre&gt; that breaks line breaks).
        /// </summary>
        public static string SanitizeStoredLyrics(string lyrics)
        {
            if (string.IsNullOrEmpty(lyrics))
                return lyrics;

            lyrics = lyrics.Replace('\uFFFD', '\'');

            while (lyrics.StartsWith("TAGS>", StringComparison.OrdinalIgnoreCase))
                lyrics = lyrics.Substring(5);

            // Unwrap <pre>…</pre> from earlier stream saves so <br> tags are honored.
            var preMatch = Regex.Match(
                lyrics.Trim(),
                @"^<pre[^>]*>(.*)</pre\s*>$",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (preMatch.Success)
                lyrics = preMatch.Groups[1].Value;

            // Normalize break tags so SearchViewModel's `<br>` splits keep working.
            lyrics = Regex.Replace(lyrics, @"<br\s*/?>", "<br>", RegexOptions.IgnoreCase);
            return lyrics;
        }

        class HymnStreamEvent
        {
            [JsonProperty("type")]
            public string Type { get; set; }

            [JsonProperty("processed")]
            public int Processed { get; set; }

            [JsonProperty("total")]
            public int Total { get; set; }

            [JsonProperty("hymn")]
            public HymnStreamPayload Hymn { get; set; }
        }

        class HymnStreamPayload
        {
            [JsonProperty("number")]
            public string Number { get; set; }

            [JsonProperty("title")]
            public string Title { get; set; }

            [JsonProperty("firstLine")]
            public string FirstLine { get; set; }

            [JsonProperty("lyrics")]
            public string Lyrics { get; set; }

            [JsonProperty("midiFileName")]
            public string MidiFileName { get; set; }

            [JsonProperty("author")]
            public string Author { get; set; }

            [JsonProperty("metre")]
            public string Metre { get; set; }

            [JsonProperty("tune")]
            public string Tune { get; set; }

            [JsonProperty("tuneComposer")]
            public string TuneComposer { get; set; }

            [JsonProperty("tuneKey")]
            public string TuneKey { get; set; }

            [JsonProperty("verses")]
            public JToken Verses { get; set; }

            [JsonProperty("verseRef")]
            public JToken VerseRef { get; set; }

            [JsonProperty("tags")]
            public JToken Tags { get; set; }

            [JsonProperty("year")]
            public string Year { get; set; }

            [JsonProperty("remark")]
            public string Remark { get; set; }
        }

        async Task<string> GetLyricsAsync(string number)
        {
            var bytes = await httpClient.GetByteArrayAsync($"{Globals.HYMN_URL}{number}");
            return DecodeLyrics(bytes);
        }

        static string DecodeLyrics(byte[] bytes)
        {
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return DecodeWindows1252(bytes);
            }
        }

        static string DecodeWindows1252(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length);
            foreach (var value in bytes)
            {
                builder.Append(value switch
                {
                    0x80 => '\u20AC',
                    0x82 => '\u201A',
                    0x83 => '\u0192',
                    0x84 => '\u201E',
                    0x85 => '\u2026',
                    0x86 => '\u2020',
                    0x87 => '\u2021',
                    0x88 => '\u02C6',
                    0x89 => '\u2030',
                    0x8A => '\u0160',
                    0x8B => '\u2039',
                    0x8C => '\u0152',
                    0x8E => '\u017D',
                    0x91 => '\u2018',
                    0x92 => '\u2019',
                    0x93 => '\u201C',
                    0x94 => '\u201D',
                    0x95 => '\u2022',
                    0x96 => '\u2013',
                    0x97 => '\u2014',
                    0x98 => '\u02DC',
                    0x99 => '\u2122',
                    0x9A => '\u0161',
                    0x9B => '\u203A',
                    0x9C => '\u0153',
                    0x9E => '\u017E',
                    0x9F => '\u0178',
                    _ => (char)value
                });
            }

            return builder.ToString();
        }

        public async Task<bool> SaveHymns(HymnList hymnList)
        {
            var folderPath = AppStorage.GetPath(folderName);
            Directory.CreateDirectory(folderPath);

            var finalPath = Path.Combine(folderPath, jsonFile);
            var tempPath = finalPath + ".tmp";

            // Stream serialize off the UI / download thread so the popup can keep updating,
            // and avoid building one giant in-memory JSON string for 800+ hymns.
            await Task.Run(() =>
            {
                using var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.SequentialScan);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                using var jsonWriter = new JsonTextWriter(writer) { Formatting = Formatting.None };
                var serializer = JsonSerializer.CreateDefault();
                serializer.Serialize(jsonWriter, hymnList);
                jsonWriter.Flush();
                writer.Flush();
                stream.Flush(true);
            }).ConfigureAwait(false);

            File.Move(tempPath, finalPath, overwrite: true);
            return true;
        }

        public async Task<bool> SaveMidi(Stream stream, string fileName)
        {
            var midiFolderPath = AppStorage.GetPath(folderName, folderMidiName);
            AppStorage.EnsureDirectory(midiFolderPath);
            var filePath = Path.Combine(midiFolderPath, fileName);
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            await stream.CopyToAsync(fileStream);
            return true;
        }

		public Task<bool> HymnListFileExists()
		{
            return Task.FromResult(File.Exists(AppStorage.GetPath(folderName, jsonFile)));
		}

		public async Task<HymnList> ReadHymns()
		{
            var filePath = AppStorage.GetPath(folderName, jsonFile);
            if (!File.Exists(filePath))
                return new HymnList();

            var settings = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            try
            {
                return await Task.Run(() =>
                {
                    var list = JsonConvert.DeserializeObject<HymnList>(settings) ?? new HymnList();
                    foreach (var hymn in list)
                    {
                        if (hymn == null)
                            continue;

                        hymn.Lyrics = SanitizeStoredLyrics(hymn.Lyrics);
                        hymn.Verses ??= Array.Empty<string>();
                        hymn.Tags ??= Array.Empty<string>();

                        if (hymn.Verses.Length == 0 && !string.IsNullOrWhiteSpace(hymn.VerseRef))
                            hymn.Verses = new[] { hymn.VerseRef.Trim() };
                    }
                    return list;
                }).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"ReadHymns corrupt file ({filePath}): {ex.Message}");
                await ClearCorruptHymnCache().ConfigureAwait(false);
                return new HymnList();
            }
        }

        async Task ClearCorruptHymnCache()
        {
            try
            {
                var filePath = AppStorage.GetPath(folderName, jsonFile);
                if (File.Exists(filePath))
                    File.Delete(filePath);

                var tempPath = filePath + ".tmp";
                if (File.Exists(tempPath))
                    File.Delete(tempPath);

                await ClearCheckpoint();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ClearCorruptHymnCache: {ex.Message}");
            }
        }

        public async Task<DownloadCheckpoint> LoadCheckpoint()
        {
            var path = AppStorage.GetPath(folderName, checkpointFile);
            if (!File.Exists(path))
                return null;

            try
            {
                var json = await File.ReadAllTextAsync(path);
                return JsonConvert.DeserializeObject<DownloadCheckpoint>(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadCheckpoint failed: {ex.Message}");
                return null;
            }
        }

        async Task SaveCheckpoint(DownloadCheckpoint checkpoint)
        {
            var path = AppStorage.GetPath(folderName, checkpointFile);
            AppStorage.EnsureDirectory(AppStorage.GetPath(folderName));
            await File.WriteAllTextAsync(path, JsonConvert.SerializeObject(checkpoint));
        }

        public async Task ClearCheckpoint()
        {
            var path = AppStorage.GetPath(folderName, checkpointFile);
            if (File.Exists(path))
                File.Delete(path);
            await Task.CompletedTask;
        }

        async Task PersistDownloadProgress(HymnList hymnList, int nextBaseIndex, bool forceSync, bool missingOnly = false)
        {
            if (hymnList.Count > 0)
                await SaveHymns(hymnList);

            await SaveCheckpoint(new DownloadCheckpoint
            {
                NextBaseIndex = nextBaseIndex,
                ForceSync = forceSync,
                MissingOnly = missingOnly,
                SavedHymnCount = hymnList.Count
            });
        }

        public async Task BackupHymnsForForceSync()
        {
            var source = AppStorage.GetPath(folderName, jsonFile);
            if (!File.Exists(source))
                return;

            var dest = AppStorage.GetPath(folderName, backupFile);
            AppStorage.EnsureDirectory(AppStorage.GetPath(folderName));
            File.Copy(source, dest, overwrite: true);
            await Task.CompletedTask;
        }

        public async Task<HymnList> ReadBackupHymns()
        {
            var path = AppStorage.GetPath(folderName, backupFile);
            if (!File.Exists(path))
                return await ReadHymns();

            var json = await File.ReadAllTextAsync(path);
            return await Task.Run(() => JsonConvert.DeserializeObject<HymnList>(json) ?? new HymnList());
        }

        public async Task<HymnList> DownloadAllMIDIs(HymnList hymnList, CRUD mode, IProgress<string> progress, CancellationToken cts)
        {
            var newList = new HymnList(hymnList);
            var dop = 6;
#if DEBUG
            await (new[] { "2", "77s", "888" }).ForEachAsync(dop, async (number, _) =>
            {
                try
                {
                    if (await DownloadMIDI(number, cts))
                    {
                        newList[number].MidiFileName = $"h{number}.mid";
                        progress.Report($"{Enum.GetName(mode.GetType(), mode)}d MIDI for #{number}");
                    }
                }
                catch (Exception ex)
                {

                }
            });
#else
            await hymnList.ForEachAsync(dop, async (hymn, _) =>
            {
                try
                {
                    if (await DownloadMIDI(hymn.Number, cts))
                    {
                        newList[hymn.Number].MidiFileName = $"h{hymn.Number}.mid";
                        progress.Report($"{Enum.GetName(mode.GetType(), mode)}d MIDI for #{hymn.Number}");
                    }
                }
                catch (Exception ex)
                {

                }
            });
#endif
            return newList;
        }

        public async Task<bool> DownloadMIDI(string number, CancellationToken cts)
        {
            bool ret;
            string dropboxArgs = "Dropbox-API-Arg";
            try
            {
                var jsonParam = new JObject();
                jsonParam["path"] = $"/Public/.midi/h{number}.mid";

                HttpRequestMessage requestMessage = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri("https://content.dropboxapi.com/2/files/download"),
                    Headers =
                    {
                        { HttpRequestHeader.Authorization.ToString(), "Bearer sl.BiIP5yX3dESIh30SAYU-C29MbwTbE_KQks_DRkxs2BP1QvdMIgjX8DQDRwL9ijNUgMPTZcVc6N8_AG1BGdH6pw-AtfIoxwDd3sHByN0m0kMjtVfS2XCHL179Hb-5H3c-V2qywdE" },
                        { dropboxArgs, JsonConvert.SerializeObject(jsonParam) }
                    }
                };
                requestMessage.Content = new StringContent("", System.Text.Encoding.UTF8, "application/octet-stream");

                var content = await httpClient2.SendAsync(requestMessage, HttpCompletionOption.ResponseContentRead, cts);
                if ((int)content.StatusCode >= 400)
                {
                    Debug.WriteLine(await content.Content.ReadAsStringAsync());
                    ret = false;
                }
                else
                {
                    var fileStream = await content.Content.ReadAsStreamAsync();
                    ret = await SaveMidi(fileStream, $"h{number}.mid");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                Debug.WriteLine(ex.StackTrace);
                ret = false;
            }
            return ret;
        }


        public static bool IsConnected()
        {
            return Connectivity.NetworkAccess == NetworkAccess.Internet;
        }

        public static async Task<bool> AudioExistsAsync(string url)
        {
            if (!IsConnected() || string.IsNullOrWhiteSpace(url))
                return false;

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await client.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsConnectedWifi()
        {
            var profiles = Connectivity.ConnectionProfiles;
            return profiles.Contains(ConnectionProfile.WiFi);
        }

        public static bool IsConnectedData()
        {
            var profiles = Connectivity.ConnectionProfiles;
            return profiles.Contains(ConnectionProfile.Cellular);
        }

        private async Task UpdateResyncVersionAsync()
        {
            try
            {
                var newVersion = await FirebaseHelper.Instance.RetrieveActiveSyncVersion();
                Preferences.Set(PreferencesVar.RESYNC_VERSION, newVersion.ToString());
                Globals.Instance.ResyncDetails.Clear();
            }
            catch (Exception)
            {
            }
        }
    }
}
