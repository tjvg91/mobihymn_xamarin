using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;   
using System.Collections.Generic;
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

namespace MobiHymn4.Utils
{
	public class HttpHelper
	{
		HttpClient httpClient;
        HtmlDocument htmlDocument;

        bool isDone = true;
		string jsonFile = "lyrics.mb";
        string folderName = "mobihymn";
        string message = "Could not complete download";

        public HttpHelper()
		{
			httpClient = new HttpClient();
            htmlDocument = new HtmlDocument();
            httpClient.Timeout = TimeSpan.FromMinutes(30);
        }

		public async Task<HymnList> DownloadHymns(
            IProgress<string> progress,
            CancellationToken cts,
            HymnList origList = null,
            bool forceSync = false)
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
            {
                await SaveHymns(hymnList);
            }

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
                    case CRUD.Create:
                    case CRUD.Update:

                        if (resyncDetail.Number == "*")
                            return await DownloadHymns(progress, cts, origList);

                        var number = resyncDetail.Number;
                        var tune = new Regex("[stf]").Match(number).Value;
                        var lyrics = await httpClient.GetStringAsync($"{Globals.HYMN_URL}{resyncDetail.Number}");
                        
                        var newHymn = processLyrics(ref lyrics, number);

                        var origHymn = origList[number];

                        if(resyncDetail.Mode == CRUD.Create && origList[number] == null)
                        {
                            var intNum = int.Parse(new Regex("[0-9]+").Match(number).Value);
                            var intLastNum = int.Parse(new Regex("[0-9]+").Match(origList.Last().Number).Value);

                            if (intNum - intLastNum == 1) updatedList.Add(newHymn);
                            else updatedList.Insert(origList.FindIndex(hymn => hymn.Number == intNum + ""), newHymn);
                        }
                        else if(origList[number] != null && !origHymn.Lyrics.Equals(newHymn.Lyrics))
                            updatedList[number] = newHymn;

                        progress.Report($"{Enum.GetName(resyncDetail.Mode.GetType(), resyncDetail.Mode)}d {newHymn.Title}");
                        break;
                    default:
                        break;
                }
            }
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
    }
}

