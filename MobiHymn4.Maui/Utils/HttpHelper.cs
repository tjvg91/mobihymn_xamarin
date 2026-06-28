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
        readonly object downloadLock = new object();

        bool isDone = true;
		string jsonFile = "lyrics.mb";
        string backupFile = "lyrics_backup.mb";
        string checkpointFile = "download_checkpoint.json";
        string folderName = "mobihymn";
        string folderMidiName = "midi";
        string message = "Could not complete download";
        const int SaveEveryBaseIndices = 5;

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
			string[] tunes = new string[] { "", "s", "t", "f" };
            HymnList hymnList;
            HymnList syncables = new HymnList();
            int i;
            bool resumed = false;

            if (trackCheckpoint)
            {
                var checkpoint = await LoadCheckpoint();
                if (checkpoint != null && checkpoint.NextSyncDetailIndex == null
                    && checkpoint.ForceSync == forceSync && checkpoint.MissingOnly == skipExisting)
                {
                    hymnList = await ReadHymns();
                    i = Math.Max(1, checkpoint.NextBaseIndex);
                    resumed = hymnList.Count > 0 || i > 1;
                    if (resumed)
                        progress?.Report($"Resuming download from hymn #{i}…");
                }
                else
                {
                    hymnList = skipExisting && origList != null ? new HymnList(origList) : new HymnList();
                    i = 1;
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
                i = 1;
            }

			isDone = false;

            while (!isDone)
			{
                if (cts.IsCancellationRequested)
                {
                    progress?.Report(message);
                    if (trackCheckpoint)
                        await PersistDownloadProgress(hymnList, i, forceSync, skipExisting);
                    return hymnList;
                }

                await tunes.ForEachAsync(4, async (tune, j) =>
                {
                    try
                    {
                        if (cts.IsCancellationRequested)
                        {
                            progress?.Report(message);
                            lock (downloadLock) { isDone = true; }
                            return;
                        }

                        var number = $"{i}{tune}";

                        if (skipExisting && hymnList.Any(h => h.Number == number))
                            return;

                        // Skip tunes already saved when resuming the same base index
                        if (resumed && hymnList.Any(h => h.Number == number))
                            return;

                        var lyrics = await GetLyricsAsync(number);
                        Hymn newHymn;

                        try
                        {
                            newHymn = ProcessLyrics(ref lyrics, number);
                        }
                        catch (Exception)
                        {
                            if (string.IsNullOrEmpty(lyrics) || new Regex("Error:", RegexOptions.IgnoreCase).IsMatch(lyrics))
                            {
                                if (j == 0)
                                    lock (downloadLock) { isDone = true; }
                                return;
                            }
                            throw;
                        }

                        lock (downloadLock)
                        {
                            var origHymn = origList?[newHymn.Number];
                            if (forceSync && (origHymn == null || origHymn.Number != newHymn.Number || origHymn.Lyrics != newHymn.Lyrics ||
                                    newHymn.FirstLine != origHymn.FirstLine))
                                syncables.Add(newHymn);
                            hymnList.Add(newHymn);
                        }

                        if (!excludeMidi) await DownloadMIDI(number, cts);

                        string reportText = skipExisting ? "Downloaded" : forceSync ? "Syncing" : "Downloaded";
                        progress?.Report($"{reportText} hymn #{number}...");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"DownloadHymns #{i}{tune}: {ex.Message}");
                    }
                });

                resumed = false;
                i++;

                if (!isDone && trackCheckpoint && (i == 2 || i % SaveEveryBaseIndices == 0))
                    await PersistDownloadProgress(hymnList, i, forceSync, skipExisting);
            }

            if (skipExisting || !forceSync || (forceSync && syncables.Count > 0))
                await SaveHymns(hymnList);

            if (trackCheckpoint)
                await ClearCheckpoint();

            if (trackCheckpoint && updateResyncVersion)
                UpdateResyncVersion();
            return hymnList;
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
                UpdateResyncVersion();
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
            var hymnJson = JsonConvert.SerializeObject(hymnList);
            var folderPath = AppStorage.GetPath(folderName);
            Directory.CreateDirectory(folderPath);
            await File.WriteAllTextAsync(Path.Combine(folderPath, jsonFile), hymnJson);
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

            var settings = await File.ReadAllTextAsync(filePath);
            return await Task.Run(() => JsonConvert.DeserializeObject<HymnList>(settings) ?? new HymnList());
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

        private async void UpdateResyncVersion()
        {
            try
            {
                var newVersion = await FirebaseHelper.Instance.RetrieveActiveSyncVersion();
                Preferences.Set(PreferencesVar.RESYNC_VERSION, newVersion.ToString());
                Globals.Instance.ResyncDetails.Clear();
            }
            catch (Exception ex)
            {

            }
        }
    }
}
