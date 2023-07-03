using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;   
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;

using MobiHymn2.Models;
using Newtonsoft.Json;
using PCLStorage;

using Xamarin.Essentials;
using MvvmHelpers;
using HtmlAgilityPack;

namespace MobiHymn2.Utils
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

        }

		public async Task<HymnList> DownloadHymns(
            string parentUrl,
            IProgress<string> progress,
            CancellationToken cts,
            HymnList origList = null,
            bool sync = false)
		{
			string[] tunes = new string[] { "", "s", "t", "f" };
			int i = 1;
			isDone = false;
            HymnList hymnList = new HymnList();
            HymnList syncables = new HymnList();

            while (!isDone)
			{
                if(cts.IsCancellationRequested)
                {
                    progress?.Report(message);
                    hymnList = new HymnList();

                    Globals.LogAppCenter((sync ? "Resyncing" : "Downloading") + " Cancelled");
                    return hymnList;
                }
                    
                Parallel.For(0, tunes.Length, async (j) =>
                {
                    try
                    {
                        if (cts.IsCancellationRequested)
                        {
                            progress?.Report(message);
                            hymnList = new HymnList();
                            isDone = true;
                            return;
                        }

                        var tune = tunes[j];
                        var number = $"{i}{tune}";

                        var lyrics = await httpClient.GetStringAsync($"{parentUrl}{number}");
                        lyrics = lyrics.Replace(Environment.NewLine, "<br/>");
                        lyrics = Regex.Replace(lyrics, "TAGS>.+<pre>", "TAGS><pre>");
                        lyrics = Regex.Replace(lyrics, "<pre>[^A-Z]+", "<pre>");
                        lyrics = Regex.Replace(lyrics, "\\r<br\\/>", "<br/>");
                        lyrics = Regex.Replace(lyrics, "<br\\/><\\/pre>", "</pre>");
                        lyrics = Regex.Replace(lyrics, "\\r", "<br/>");

                        if (string.IsNullOrEmpty(lyrics) || new Regex("Error:", RegexOptions.IgnoreCase).IsMatch(lyrics))
                        {
                            if (j > 0) return;
                            else
                            {
                                isDone = true;
                                return;
                            }
                        }

                        htmlDocument.LoadHtml(lyrics);
                        var root = htmlDocument.DocumentNode;
                        var preText = root.Descendants("pre").SingleOrDefault();

                        var newHymn = new Hymn
                        {
                            Lyrics = lyrics,
                            FirstLine = new Regex("<br>").Split(preText.InnerHtml)[0],
                            Title = tune == "f" ? number.Replace("f", " (4th tune)") :
                                    tune == "s" ? number.Replace("s", " (2nd tune)") : number.Replace("t", " (3rd tune)"),
                            Number = number
                        };
                        var origHymn = origList[newHymn.Number];
                        if (sync && (origHymn == null || origHymn.Number != newHymn.Number || origHymn.Lyrics != newHymn.Lyrics ||
                                newHymn.FirstLine != origHymn.FirstLine))
                            syncables.Add(newHymn);
                        hymnList.Add(newHymn);

                        string reportText = sync ? "Syncing" : "Downloaded";
                        progress?.Report($"{reportText} hymn #{number}...");

                    }
                    catch (Exception ex)
                    {
                        if (j > 0) return;
                        else isDone = true;
                    }
                });
				i++;
                await Task.Delay(100);
            }

            if (!sync || (sync && syncables.Count > 0))
            {
                await SaveHymns(hymnList);
            }

			return hymnList;
		}

        public async Task<bool> SaveHymns(HymnList hymnList)
        {
            var hymnJson = JsonConvert.SerializeObject(hymnList);
            IFolder rootFolder = PCLStorage.FileSystem.Current.LocalStorage;
            IFile file = await rootFolder.CreateFileAsync($"{folderName}/{jsonFile}", CreationCollisionOption.ReplaceExisting);
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

