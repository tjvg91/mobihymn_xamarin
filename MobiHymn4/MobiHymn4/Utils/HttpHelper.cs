using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;   
using System.Text.RegularExpressions;
using System.Threading;

using MobiHymn4.Models;
using Newtonsoft.Json;
using PCLStorage;

using Xamarin.Essentials;
using MvvmHelpers;
using HtmlAgilityPack;
using Polly;
using System.Net;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Diagnostics;

namespace MobiHymn4.Utils
{
	public class HttpHelper
	{
		HttpClient httpClient;
        HttpClient httpClient2;
        HtmlDocument htmlDocument;

        bool isDone = true;
		string jsonFile = "lyrics.mb";
        string folderName = "mobihymn";
        string folderMidiName = "midi";
        string message = "Could not complete download";

        public HttpHelper()
		{
			httpClient = new HttpClient();
            httpClient2 = new HttpClient();
            htmlDocument = new HtmlDocument();
            httpClient.Timeout = TimeSpan.FromMinutes(30);
            httpClient2.Timeout = TimeSpan.FromMinutes(30);
        }

		public async Task<HymnList> DownloadHymns(
            IProgress<string> progress,
            CancellationToken cts,
            HymnList origList = null,
            bool forceSync = false,
            bool excludeMidi = false)
		{
			string[] tunes = new string[] { "", "s", "t", "f" };
			int i = 1;
			isDone = false;
            HymnList hymnList = new HymnList();
            HymnList syncables = new HymnList();

            var policy = Policy.Handle<TaskCanceledException>().Or<OperationCanceledException>()
                        .Or<WebException>().Or<TimeoutException>()
                        .WaitAndRetryAsync(20, retry => TimeSpan.FromSeconds(Math.Pow(2, retry)));


            while (!isDone)
			{
                if(cts.IsCancellationRequested)
                {
                    progress?.Report(message);
                    hymnList = new HymnList();

                    Globals.LogAppCenter((forceSync ? "Resyncing" : "Downloading") + " Cancelled");
                    return hymnList;
                }
                    
                var forRes = Parallel.For(0, tunes.Length, async (j, state) =>
                {
                    try
                    {
                        if (cts.IsCancellationRequested)
                        {
                            progress?.Report(message);
                            hymnList = new HymnList();
                            isDone = true;
                            state.Stop();
                            return;
                        }

                        var tune = tunes[j];
                        var number = $"{i}{tune}";

                        //var lyrics = await policy.ExecuteAsync(() => httpClient.GetStringAsync($"{parentUrl}{number}"));
                        var lyrics = await httpClient.GetStringAsync($"{Globals.HYMN_URL}{number}");
                        Hymn newHymn = new Hymn();

                        try
                        {
                            newHymn = processLyrics(ref lyrics, number);
                        }
                        catch(Exception)
                        {
                            if (string.IsNullOrEmpty(lyrics) || new Regex("Error:", RegexOptions.IgnoreCase).IsMatch(lyrics))
                            {
                                if (j == 0)
                                    isDone = true;
                                state.Stop();
                                return;
                            }
                        }
                        var origHymn = origList[newHymn.Number];
                        if (forceSync && (origHymn == null || origHymn.Number != newHymn.Number || origHymn.Lyrics != newHymn.Lyrics ||
                                newHymn.FirstLine != origHymn.FirstLine))
                            syncables.Add(newHymn);
                        hymnList.Add(newHymn);

                        //MIDI
                        if(!excludeMidi) await DownloadMIDI(number);

                        string reportText = forceSync ? "Syncing" : "Downloaded";
                        progress?.Report($"{reportText} hymn #{number}...");

                    }
                    catch (Exception ex)
                    {
                        state.Stop();
                        return;
                    }
                });
                if(forRes.IsCompleted)
                {
                    i++;
                    await Task.Delay(100);
                }
            }

            if (!forceSync || (forceSync && syncables.Count > 0))
                await SaveHymns(hymnList);
            UpdateResyncVersion();
            return hymnList;
		}

        public async Task<HymnList> SyncChanges(IProgress<string> progress, CancellationToken cts, ObservableRangeCollection<ResyncDetail> resyncDetails, HymnList origList)
        {
            HymnList updatedList = new HymnList(origList);
            foreach(ResyncDetail resyncDetail in resyncDetails)
            {
                if (cts.IsCancellationRequested)
                    break;
                switch(resyncDetail.Mode)
                {
                    case CRUD.Delete:
                        updatedList.RemoveAt(updatedList.FindIndex(hymn => hymn.Number.Equals(resyncDetail.Number)));
                        break;
                    case CRUD.Update:
                        if (resyncDetail.Number == "*")
                        {
                            if (resyncDetail.Type == ResyncType.Lyrics)
                                updatedList = await DownloadHymns(progress, cts, updatedList, false, true);
                            else
                                updatedList = DownloadAllMIDIs(updatedList, resyncDetail.Mode, progress, cts);
                        }
                        goto default;
                    case CRUD.Create:
                        if (resyncDetail.Type == ResyncType.Audio)
                        {
                            try
                            {
                                if (resyncDetail.Number == "*")
                                    updatedList = DownloadAllMIDIs(updatedList, resyncDetail.Mode, progress, cts);
                                else if (await DownloadMIDI(resyncDetail.Number))
                                    updatedList.Find(hymn => hymn.Number == resyncDetail.Number).MidiFileName = $"h{resyncDetail.Number}.mid";
                            }
                            catch (Exception ex)
                            {

                            }
                        }
                        goto default;
                    default:
                        if(resyncDetail.Type == ResyncType.Lyrics)
                        {
                            var number = resyncDetail.Number;
                            var tune = new Regex("[stf]").Match(number).Value;
                            try
                            {

                                var lyrics = await httpClient.GetStringAsync($"{Globals.HYMN_URL}{resyncDetail.Number}");

                                var newHymn = processLyrics(ref lyrics, number);

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
            }

            UpdateResyncVersion();
            return updatedList;
        }

        private Hymn processLyrics(ref string lyrics, string number)
        {
            lyrics = lyrics.Replace(Environment.NewLine, "<br/>");
            lyrics = Regex.Replace(lyrics, "TAGS>.+<pre>", "TAGS><pre>");
            lyrics = Regex.Replace(lyrics, "<pre>[^A-Z]+", "<pre>");
            lyrics = Regex.Replace(lyrics, "\\r<br\\/>", "<br/>");
            lyrics = Regex.Replace(lyrics, "<br\\/><\\/pre>", "</pre>");
            lyrics = Regex.Replace(lyrics, "\\r", "<br/>");

            if (string.IsNullOrEmpty(lyrics) || new Regex("Error:", RegexOptions.IgnoreCase).IsMatch(lyrics))
                throw new Exception(lyrics);

            htmlDocument.LoadHtml(lyrics);
            var root = htmlDocument.DocumentNode;
            var preText = root.Descendants("pre").SingleOrDefault();

            var newHymn = new Hymn
            {
                Lyrics = lyrics,
                FirstLine = new Regex("<br>").Split(preText.InnerHtml)[0],
                Title = number.ToTitle(),
                Number = number
            };
            return newHymn;
        }

        public async Task<bool> SaveHymns(HymnList hymnList)
        {
            var hymnJson = JsonConvert.SerializeObject(hymnList);
            IFolder rootFolder = PCLStorage.FileSystem.Current.LocalStorage;
            IFolder folder = await rootFolder.CreateFolderAsync($"{folderName}", CreationCollisionOption.ReplaceExisting);
            IFile file = await folder.CreateFileAsync($"{jsonFile}", CreationCollisionOption.ReplaceExisting);
            await file.WriteAllTextAsync(hymnJson);
            return true;
        }

        public async Task<bool> SaveMidi(Stream stream, string fileName)
        {
            IFolder rootFolder = PCLStorage.FileSystem.Current.LocalStorage;
            IFolder folder;
            IFolder folderMidi;

            if (await rootFolder.CheckExistsAsync($"{folderName}") == ExistenceCheckResult.FolderExists)
                folder = await rootFolder.GetFolderAsync($"{folderName}");
            else
                folder = await rootFolder.CreateFolderAsync($"{folderName}", CreationCollisionOption.ReplaceExisting);

            if (await folder.CheckExistsAsync($"{folderMidiName}") == ExistenceCheckResult.FolderExists)
                folderMidi = await folder.GetFolderAsync($"{folderMidiName}");
            else
                folderMidi = await folder.CreateFolderAsync($"{folderMidiName}", CreationCollisionOption.ReplaceExisting);
            await folderMidi.CreateFileAsync($"{fileName}", CreationCollisionOption.ReplaceExisting);

            using (var fileStream = new FileStream($"{rootFolder.Path}/{folderName}/{folderMidiName}/{fileName}", FileMode.Create, System.IO.FileAccess.Write))
                stream.CopyTo(fileStream);

            return true;
        }

		public async Task<bool> HymnListFileExists()
		{
            IFolder rootFolder = PCLStorage.FileSystem.Current.LocalStorage;
            var existingResult = await rootFolder.CheckExistsAsync($"mobihymn/{jsonFile}");
            return existingResult == ExistenceCheckResult.FileExists;
		}

		public async Task<HymnList> ReadHymns()
		{
			IFolder rootFolder = PCLStorage.FileSystem.Current.LocalStorage;

            HymnList hymnList = new HymnList();
            if (await HymnListFileExists())
			{
				IFolder folder = await rootFolder.GetFolderAsync("mobihymn");
				IFile file = await folder.GetFileAsync(jsonFile);
				var settings = await file.ReadAllTextAsync();
                hymnList = JsonConvert.DeserializeObject<HymnList>(settings);
			}
			return hymnList;

        }

        public HymnList DownloadAllMIDIs(HymnList hymnList, CRUD mode, IProgress<string> progress, CancellationToken cts)
        {
            var newList = new HymnList(hymnList);
#if DEBUG   
            Parallel.ForEach(new []{ "2", "77s", "888" }, async (number) =>
            {
                if (await DownloadMIDI(number))
                {
                    var newHymn = newList.Find(hymn => hymn.Number == number);
                    newHymn.MidiFileName = $"h{number}.mid";
                    progress.Report($"{Enum.GetName(mode.GetType(), mode)}d MIDI for #{number}");
                }
            });

#else

            Parallel.ForEach(hymnList, async (hymn) =>
            {
                if(await DownloadMIDI(hymn.Number))
                {
                    var newHymn = new Hymn(hymn);
                    newHymn.MidiFileName = $"h{hymn.Number}.mid";
                    newList.Find(hymn => hymn.Number == newHymn.Number) = new Hymn(newHymn);
                    progress.Report($"{Enum.GetName(mode.GetType(), mode)}d MIDI for #{newHymn.Title}");
                }
            });
#endif
            return newList;
        }

        public async Task<bool> DownloadMIDI(string number)
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
                        { HttpRequestHeader.Authorization.ToString(), "Bearer sl.BiEhCA_3XuZeYtWh6icc_CZwSHjg36lKdUr24xcneXULafxF-7-AUYl9aC2LWSWy20DuCLHXlYI-lBvA9-a24A1JHVXIjRrFJegV7z_xoAUwsDp5stkF2UBTJAijsws_vG4C6zHMA0E" },
                        { dropboxArgs, JsonConvert.SerializeObject(jsonParam) }
                    }
                };
                requestMessage.Content = new StringContent("", System.Text.Encoding.UTF8, "application/octet-stream");

                var content = await httpClient2.SendAsync(requestMessage);
                if ((int)content.StatusCode >= 400)
                {
                    Debug.WriteLine(content.Content.ReadAsStringAsync());
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
            Preferences.Set(PreferencesVar.RESYNC_VERSION, (await new FirebaseHelper().RetrieveActiveSyncVersion()).ToString());
            Globals.Instance.ResyncDetails.Clear();
        }
    }
}
