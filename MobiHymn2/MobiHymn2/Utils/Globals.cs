﻿using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

using MobiHymn2.Models;

using Xamarin.Forms;
using Xamarin.Essentials;
using Xamarin.CommunityToolkit.Extensions;

using MvvmHelpers;
using Newtonsoft.Json;
using PCLStorage;
using Newtonsoft.Json.Linq;
using Microsoft.AppCenter.Crashes;
using MobiHymn2.Views.Popups;
using Microsoft.AppCenter.Analytics;

namespace MobiHymn2.Utils
{
	public class Globals
    {
        [JsonIgnore]
        public Color PrimaryText = Color.FromHex("2D2D2D");
        [JsonIgnore]
        public Color Gray = Color.FromHex("464646");
        [JsonIgnore]
        public Color Brown = Color.FromHex("8b6220");
        [JsonIgnore]
        public uint Duration = 500;

        private string settingsName = "settings.json";

        [JsonIgnore]
        public CancellationTokenSource CTS = new CancellationTokenSource();

        private Globals()
        {
            ActiveThemeText = PrimaryText;
        }
        #region Properties
        private static Globals instance = null;
        [JsonIgnore]
        public static Globals Instance
        {
            get
            {
                if (instance == null) instance = new Globals();
                return instance;
            }
        }

        private HymnList hymnList = new HymnList();
        [JsonIgnore]
		public HymnList HymnList
		{
			get { return hymnList; }
			set { hymnList = value; }
		}

        private ObservableRangeCollection<ShortHymn> historyList = new ObservableRangeCollection<ShortHymn>();
        public ObservableRangeCollection<ShortHymn> HistoryList
        {
            get { return historyList; }
            set { historyList = value; }
        }

        private ObservableRangeCollection<ShortHymn> bookmarkList = new ObservableRangeCollection<ShortHymn>();
        public ObservableRangeCollection<ShortHymn> BookmarkList
        {
            get { return bookmarkList; }
            set { bookmarkList = value; }
        }

        private InputType hymnInputType = InputType.Numpad;
        public InputType HymnInputType
        {
			get { return hymnInputType; }
            set
            {
                if (hymnInputType != value)
                {
                    LogAppCenter(
                        "Set Hymn Input Type", "Input Type",
                        Enum.GetName(typeof(InputType), value)
                    );
                    hymnInputType = value;
                    OnHymnInputTypeChanged(value);
                }
            }
        }

        private Hymn activeHymn;
        public Hymn ActiveHymn
        {
            get { return activeHymn; }
            set
            {
                if (activeHymn == null || !activeHymn.Equals(value))
                {
                    LogAppCenter(
                        "Set Active Hymn", "Active Hymn",
                        JObject.FromObject(value).ToString(Formatting.None)
                    );
                    activeHymn = value;
                    var newHymn = new ShortHymn
                    {
                        Number = value.Number,
                        Line = value.FirstLine
                    };
                    HistoryList = HistoryList.Where(x => x.Number != newHymn.Number).ToObservableRangeCollection();
                    HistoryList.Insert(0, newHymn);
                    if (HistoryList.Count > 10) HistoryList.RemoveAt(HistoryList.Count - 1);
                    OnActiveHymnChanged(value);
                    OnHistoryChanged(HistoryList);
                }
            }
        }

        private TextAlignment activeAlignment;
        public TextAlignment ActiveAlignment
        {
            get { return activeAlignment; }
            set
            {
                if (activeAlignment != value)
                {
                    LogAppCenter(
                        "Set Active Alignment", "Active Alignment",
                        Enum.GetName(typeof(TextAlignment), value)
                    );
                    activeAlignment = value;
                    OnAlignmentChanged(activeAlignment);
                }
            }
        }

        private Color activeThemeText;
        [JsonIgnore]
        public Color ActiveThemeText {
            get => activeThemeText;
            private set => activeThemeText = value;
        }

        private Color activeReadTheme = Color.White;
        public Color ActiveReadTheme
        {
            get { return activeReadTheme; }
            set
            {
                if (!activeReadTheme.Equals(value))
                {
                    LogAppCenter(
                        "Set Active Read Theme", "Active Read Theme",
                        value.ToHex()
                    );
                    activeReadTheme = value;


                    if (value == PrimaryText || value == Gray || value == Brown)
                        ActiveThemeText = Color.White;
                    else
                        ActiveThemeText = PrimaryText;
                    OnActiveReadThemeChanged(value);
                }
            }
        }

        private double activeFontSize = 20;
        public double ActiveFontSize
        {
            get => activeFontSize;
            set
            {
                LogAppCenter(
                    "Set Active Font Size", "Active Font Size",
                    value.ToString()
                );
                activeFontSize = value;
                OnActiveFontSizeChanged(activeFontSize);
            }
        }

        private string activeFont = Device.RuntimePlatform == Device.Android ? "Roboto" : "SFPro";
        public string ActiveFont
        {
            get => activeFont;
            set
            {
                LogAppCenter(
                    "Set Active Font", "Active Font",
                    activeFont,
                    value
                );
                activeFont = value;
                OnActiveFontChanged(value);
            }
        }

        private bool darkMode = false;
        public bool DarkMode
        {
            get => darkMode;
            set
            {
                LogAppCenter(
                    "Set Dark Mode", "Dark Mode",
                    value.ToString()
                );
                darkMode = value;
                Application.Current.UserAppTheme = value ? OSAppTheme.Dark : OSAppTheme.Light;
                Preferences.Set("darkMode", value);
                OnDarkModeChanged(value);
            }
        }

        private bool keepAwake = true;
        public bool KeepAwake
        {
            get => keepAwake;
            set
            {
                LogAppCenter(
                    "Set Keep Awake", "Keep Awake",
                    value.ToString()
                );
                keepAwake = value;
                DeviceDisplay.KeepScreenOn = value;
                OnKeepAwakeChanged(value);
            }
        }

        private bool isOrientationLocked = false;
        public bool IsOrientationLocked
        {
            get => isOrientationLocked;
            set
            {
                isOrientationLocked = value;
                OnOrientationLockedChanged(value);
            }
        }
        #endregion

        #region Events
        public event EventHandler ActiveHymnChanged;
        public event EventHandler HymnInputTypeChanged;
        public event EventHandler InitFinished;
        public event EventHandler DownloadStarted;
        public event EventHandler DownloadProgressed;
        public event EventHandler DownloadError;
        public event EventHandler ActiveReadThemeChanged;
        public event EventHandler ActiveAlignmentChanged;
        public event EventHandler ActiveFontSizeChanged;
        public event EventHandler ActiveFontChanged;
        public event EventHandler BookmarksChanged;
        public event EventHandler HistoryChanged;
        public event EventHandler DarkModeChanged;
        public event EventHandler KeepAwakeChanged;
        public event EventHandler OrientationLockedChanged;

        private void OnActiveHymnChanged(Hymn value)
        {
            if (ActiveHymnChanged != null) ActiveHymnChanged(value, EventArgs.Empty);
        }
        private void OnHymnInputTypeChanged(InputType value)
        {
            if (HymnInputTypeChanged != null) HymnInputTypeChanged(value, EventArgs.Empty);
        }
        private void OnBookmarksChanged(ObservableRangeCollection<ShortHymn> value)
        {
            if (BookmarksChanged != null) BookmarksChanged(value, EventArgs.Empty);
        }
        private void OnHistoryChanged(ObservableRangeCollection<ShortHymn> value)
        {
            if (HistoryChanged != null) HistoryChanged(value, EventArgs.Empty);
        }
        private void OnAlignmentChanged(TextAlignment value)
        {
            if (ActiveAlignmentChanged != null) ActiveAlignmentChanged(value, EventArgs.Empty);
        }
        private void OnActiveReadThemeChanged(Color value)
        {
            if (ActiveReadThemeChanged != null) ActiveReadThemeChanged(value, EventArgs.Empty);
        }
        private void OnActiveFontChanged(string value)
        {
            if (ActiveFontChanged != null) ActiveFontChanged(value, EventArgs.Empty);
        }
        private void OnActiveFontSizeChanged(double value)
        {
            if (ActiveFontSizeChanged != null) ActiveFontSizeChanged(value, EventArgs.Empty);
        }
        private void OnDarkModeChanged(bool value)
        {
            if (DarkModeChanged != null) DarkModeChanged(value, EventArgs.Empty);
        }
        private void OnKeepAwakeChanged(bool value)
        {
            if (KeepAwakeChanged != null) KeepAwakeChanged(value, EventArgs.Empty);
        }
        private void OnOrientationLockedChanged(bool value)
        {
            if (OrientationLockedChanged != null) OrientationLockedChanged(value, EventArgs.Empty);
        }
        private void OnDownloadStarted(string value)
        {
            if (DownloadStarted != null) DownloadStarted(value, EventArgs.Empty);
        }
        private void OnDownloadProgressed(string value)
        {
            if (DownloadProgressed != null) DownloadProgressed(value, EventArgs.Empty);
        }
        private void OnDownloadError(string value)
        {
            if (DownloadError != null) DownloadError(value, EventArgs.Empty);
        }
        public void OnInitFinished(string value)
        {
            if (InitFinished != null) InitFinished(value, EventArgs.Empty);
        }
        #endregion

        #region Methods
        public async Task<bool> DownloadHymns(bool sync = false)
        {
            LogAppCenter((sync ? "Resync" : "Download") + " Starting");
            HttpHelper httpHelper = new HttpHelper();

            var hymnUrl = "http://157.230.9.81/hymn/tim.dna?q=";
            var exists = await httpHelper.HymnListFileExists();

            if (exists && !sync)
                HymnList = await httpHelper.ReadHymns();
            else if (HttpHelper.IsConnected())
            {
                OnDownloadStarted("");
                Progress<string> downloadProgress = new Progress<string>((report) =>
                {
                    OnDownloadProgressed(report);
                });
                HymnList = await httpHelper.DownloadHymns(hymnUrl, downloadProgress, CTS.Token, HymnList, sync);
            }
            else
            {
                var errorMessage = "Please connect to download resources.";
                Crashes.TrackError(new Exception(errorMessage));
                OnDownloadError(errorMessage);
            }

            LogAppCenter((sync ? "Resync" : "Download") + " Finished", "Hymn Count", HymnList.Count().ToString());
            return true;
        }

        public async void Init()
        {
            LogAppCenter("Init Started");
            try
            {
                if(await DownloadHymns())
                {
                    HymnList = (from x in HymnList
                                orderby double.Parse(x.Number.Replace("f", ".4").Replace("s", ".2").Replace("t", ".3"))
                                select x).ToHymnList();

                    if (!(await LoadSettings()))
                    {
                        LogAppCenter("New device");
                        activeHymn = HymnList[0];
                        activeReadTheme = Color.White;
                    }
                    LogAppCenter("Init Finished");
                    OnInitFinished(null);
                }
            }
            catch (Exception ex)
            {
                OnDownloadError(ex.Message);
                Crashes.TrackError(ex);
            }
        }

        public bool IsBookmarked()
        {
            return (from x in BookmarkList where x.Number == ActiveHymn.Number select x).Any();
        }

        public void AddBookmark()
        {
            if (!IsBookmarked())
            {
                LogAppCenter("Adding bookmark...");
                var val = new Models.ShortHymn
                {
                    Number = ActiveHymn.Number,
                    Line = ActiveHymn.FirstLine,
                };
                BookmarkList.Add(val);
                LogAppCenter("Bookmark Added", "Bookmark", JObject.FromObject(val).ToString(Formatting.None));
                OnBookmarksChanged(BookmarkList);
            }
        }

        public bool RemoveBookmark(ShortHymn hymn)
        {
            try
            {
                LogAppCenter("Removing bookmark...");
                var res = BookmarkList.Remove(hymn);
                LogAppCenter("Bookmark Removed", "Bookmark", JObject.FromObject(hymn).ToString(Formatting.None));
                OnBookmarksChanged(BookmarkList);
                return res;
            }
            catch (Exception ex)
            {
                Crashes.TrackError(ex);
                return false;
            }
        }

        public bool RemoveBookmark()
        {
            try
            {
                LogAppCenter("Removing bookmark...");
                var filtered = BookmarkList
                                    .Where(bk => bk.Number == ActiveHymn.Number).First();
                var res = BookmarkList.Remove(filtered);
                LogAppCenter("Bookmark Removed", "Bookmark", JObject.FromObject(filtered).ToString(Formatting.None));
                OnBookmarksChanged(BookmarkList);
                return res;
            }
            catch (Exception ex)
            {
                Crashes.TrackError(ex);
                return false;
            }
        }

        public async void SaveSettings()
        {
            LogAppCenter("Saving settings...");
            try
            {
                var settings = JsonConvert.SerializeObject(Globals.Instance);
                IFolder rootFolder = PCLStorage.FileSystem.Current.LocalStorage;
                IFolder folder = await rootFolder.CreateFolderAsync("mobihymn", CreationCollisionOption.OpenIfExists);
                IFile file = await folder.CreateFileAsync(settingsName, CreationCollisionOption.ReplaceExisting);
                await file.WriteAllTextAsync(settings);
                LogAppCenter("Settings saved", "Settings", settings);
            }
            catch (Exception ex)
            {
                Crashes.TrackError(ex);
            }
        }

        public async Task<bool> LoadSettings()
        {
            LogAppCenter("Loading settings...");
            try
            {

                IFolder rootFolder = PCLStorage.FileSystem.Current.LocalStorage;
                if (await rootFolder.CheckExistsAsync($"mobihymn/{settingsName}") == ExistenceCheckResult.FileExists)
                {
                    IFolder folder = await rootFolder.GetFolderAsync("mobihymn");
                    IFile file = await folder.GetFileAsync(settingsName);
                    var settings = await file.ReadAllTextAsync();
                    var json = JsonConvert.DeserializeObject<Dictionary<string, object>>(settings);
                    foreach (KeyValuePair<string, object> entry in json)
                    {
                        switch(entry.Key)
                        {
                            case nameof(HymnInputType):
                                HymnInputType = (InputType)int.Parse(entry.Value + "");
                                break;
                            case nameof(ActiveHymn):
                                activeHymn = ((JObject)(entry.Value)).ToObject<Hymn>();
                                break;
                            case nameof(ActiveReadTheme):
                                ActiveReadTheme = ((JObject)(entry.Value)).ToColor().Value;
                                break;
                            case nameof(ActiveAlignment):
                                ActiveAlignment = (TextAlignment)int.Parse(entry.Value + "");
                                break;
                            case nameof(HistoryList):
                                HistoryList = ((JArray)entry.Value).Select(x => new ShortHymn
                                {
                                    Number = (string)x["Number"],
                                    TimeStamp = (DateTime)x["TimeStamp"],
                                    Line = (string)x["Line"]
                                }).ToObservableRangeCollection();
                                break;
                            case nameof(BookmarkList):
                                BookmarkList = ((JArray)entry.Value).Select(x => new ShortHymn
                                {
                                    Number = (string)x["Number"],
                                    TimeStamp = (DateTime)(x["TimeStamp"]),
                                    Line = (string)x["Line"]
                                }).ToObservableRangeCollection();
                                break;
                            case nameof(ActiveFontSize):
                                ActiveFontSize = double.Parse(entry.Value + "");
                                break;
                            case nameof(ActiveFont):
                                ActiveFont = entry.Value + "";
                                break;
                            case nameof(DarkMode):
                                DarkMode = (bool)entry.Value;
                                break;
                            case nameof(KeepAwake):
                                KeepAwake = (bool)entry.Value;
                                break;
                            case nameof(IsOrientationLocked):
                                KeepAwake = (bool)entry.Value;
                                break;
                            default:
                                Globals.Instance.GetType().GetProperty(entry.Key).SetValue(entry.Value, null);
                                break;
                        }
                    }
                    LogAppCenter("Settings loaded", "Settings", settings);

                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Crashes.TrackError(ex);
                return false;
            }
            
        }
        #endregion

        #region ToastPopup
        public static void ShowToastPopup(ContentPage context, string source, string label)
        {
            ToastPopup toastPopup = new ToastPopup
            {
                PopupAnim = source,
                PopupLabel = label,
                IsLightDismissEnabled = false,
            };
            context.Navigation.ShowPopup(toastPopup);
            Task.Delay(2000).ContinueWith((t) =>
            {
                toastPopup.Dismiss(null);
            });
        }

        public static void ShowToastPopup(ContentPage context, string source, string label, double size, Rectangle layoutBounds)
        {
            ToastPopup toastPopup = new ToastPopup
            {
                PopupAnim = source,
                PopupLabel = label,
                PopupAnimSize = size,
                LayoutBounds = layoutBounds,
                IsLightDismissEnabled = false,
            };
            context.Navigation.ShowPopup(toastPopup);
            Task.Delay(2000).ContinueWith((t) =>
            {
                toastPopup.Dismiss(null);
            });
        }
        #endregion

        #region AppCenter
        public static void LogAppCenter(string title)
        {

            Analytics.TrackEvent(title, new Dictionary<string, string>
            {
                { "Device Name", DeviceInfo.Name },
                { "Device Manufacturer", DeviceInfo.Manufacturer },
                { "Device Model", DeviceInfo.Model },
                { "Device OS Version", DeviceInfo.VersionString }
            });
        }

        public static void LogAppCenter(string title, string valueName, string value)
        {

            Analytics.TrackEvent(title, new Dictionary<string, string>
            {
                { "Device Name", DeviceInfo.Name },
                { "Device Manufacturer", DeviceInfo.Manufacturer },
                { "Device Model", DeviceInfo.Model },
                { "Device OS Version", DeviceInfo.VersionString },
                { valueName, value }
            });
        }

        public static void LogAppCenter(string title, string valueName, string oldValue, string newValue)
        {

            Analytics.TrackEvent(title, new Dictionary<string, string>
            {
                { "Device Name", DeviceInfo.Name },
                { "Device Manufacturer", DeviceInfo.Manufacturer },
                { "Device Model", DeviceInfo.Model },
                { "Device OS Version", DeviceInfo.VersionString },
                { $"Old {valueName}", oldValue },
                { $"New {valueName}", newValue }
            });
        }
        #endregion
    }
}

