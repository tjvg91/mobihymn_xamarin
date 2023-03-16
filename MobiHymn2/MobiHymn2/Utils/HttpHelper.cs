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

namespace MobiHymn2.Utils
{
	public class HttpHelper
	{
		HttpClient httpClient;
		bool isDone = true;
		string jsonFile = "lyrics.mb";
        string folderName = "mobihymn";
        string message = "Could not complete download";

        public HttpHelper()
		{
			httpClient = new HttpClient();
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

                        var lyrics = await httpClient.GetStringAsync($"{parentUrl}{number}.txt");
                        if (string.IsNullOrEmpty(lyrics))
                        {
                            if (j > 0) return;
                            else isDone = true;
                        }
                        var newHymn = new Hymn
                        {
                            Lyrics = lyrics,
                            FirstLine = new Regex(Environment.NewLine).Split(lyrics)[0],
                            Title = tune == "f" ? number.Replace("f", " (4th tune)") :
                                    tune == "s" ? number.Replace("s", " (2nd tune)") : number.Replace("t", " (3rd tune)"),
                            Number = number
                        };
                        var origHymn = origList[newHymn.Number];
                        if (sync && (origHymn == null || origHymn.Number != newHymn.Number || origHymn.Lyrics != newHymn.Lyrics))
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
            IFolder mobihymnFolder = await rootFolder.CreateFolderAsync(folderName, CreationCollisionOption.OpenIfExists);
            IFile file = await rootFolder.CreateFileAsync($"{folderName}/{jsonFile}", CreationCollisionOption.ReplaceExisting);
            await file.WriteAllTextAsync(hymnJson);
            return true;
        }

		public async Task<bool> HymnListFileExists()
		{
            IFolder rootFolder = PCLStorage.FileSystem.Current.LocalStorage;
            //IFile file = await rootFolder.GetFileAsync($"mobihymn/{jsonFile}");
            //await file.DeleteAsync();
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

