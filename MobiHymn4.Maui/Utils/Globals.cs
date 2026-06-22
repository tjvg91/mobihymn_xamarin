using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using MobiHymn4.Models;
using MobiHymn4.Views.Popups;

using Microsoft.Maui.Controls;

using CommunityToolkit.Maui.Views;

using MvvmHelpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;

namespace MobiHymn4.Utils
{
    public class Globals
    {
        [JsonIgnore]
        public Color Primary = Color.FromArgb("F5D200");
        [JsonIgnore]
        public Color PrimaryText = Color.FromArgb("2D2D2D");
        [JsonIgnore]
        public Color Gray = Color.FromArgb("464646");
        [JsonIgnore]
        public Color Brown = Color.FromArgb("8b6220");
        [JsonIgnore]
        public Color White = Color.FromArgb("ffffff");
        [JsonIgnore]
        public Color Sepia = Color.FromArgb("faebd7");
        [JsonIgnore]
        public Color Green = Color.FromArgb("009200");
        [JsonIgnore]
        public Color Orange = Color.FromArgb("DF7D38");
        [JsonIgnore]
        public Color Blue = Color.FromArgb("337DEF");
        [JsonIgnore]
        public Color Purple = Color.FromArgb("7237C5");
        [JsonIgnore]
        public Color Pink = Color.FromArgb("FF0073");
        [JsonIgnore]
        public uint Duration = 500;

        [JsonIgnore]
        public List<ThemeSettings> ThemeList = new List<ThemeSettings>();

        private string folderRootName = "mobihymn";
        private string settingsName = "settings.json";
        private string folderMidiName = "midi";

        [JsonIgnore]
        private bool suppressSettingsSave;

        [JsonIgnore]
        private readonly SemaphoreSlim settingsSaveLock = new SemaphoreSlim(1, 1);

        [JsonIgnore]
        private bool initComplete;

        [JsonIgnore]
        int downloadInFlight;

        [JsonIgnore]
        public bool InitInProgress => Volatile.Read(ref downloadInFlight) != 0;

        [JsonIgnore]
        public bool HasIncompleteDownloadOnDisk { get; private set; }

        [JsonIgnore]
        public bool IsDownloadUiActive { get; private set; }

        [JsonIgnore]
        public string LastDownloadProgressMessage { get; private set; }

        public bool TryBeginDownloadOperation() =>
            Interlocked.CompareExchange(ref downloadInFlight, 1, 0) == 0;

        public void EndDownloadOperation() =>
            Interlocked.Exchange(ref downloadInFlight, 0);

        [Obsolete("Use TryBeginDownloadOperation / EndDownloadOperation.")]
        public void MarkDownloadOperationInProgress(bool inProgress)
        {
            if (inProgress)
                TryBeginDownloadOperation();
            else
                EndDownloadOperation();
        }

        [JsonIgnore]
        public bool InitComplete => initComplete;

        [JsonIgnore]
        public CancellationTokenSource CTS = new CancellationTokenSource();

        [JsonIgnore]
        public List<FontSettings> FontList = new List<FontSettings>();

        [JsonIgnore]
        public int NewVersion = -1;

        private ObservableRangeCollection<ResyncDetail> resyncDetails = new ObservableRangeCollection<ResyncDetail>();
        [JsonIgnore]
        public ObservableRangeCollection<ResyncDetail> ResyncDetails
        {
            get => resyncDetails;
            set => resyncDetails = value;
        }

        [JsonIgnore]
        public static string HYMN_URL = "http://157.230.9.81/hymn/tim.dna?q=";
        public static string HYMN_AUDIO_URL = "http://157.230.9.81/hymn/audio/gccsatx/";

        public static string GetHymnAudioUrl(string hymnNumber) =>
            $"{HYMN_AUDIO_URL}{hymnNumber}.mp3";

        private Globals()
        {
            ActiveThemeText = PrimaryText;
            ThemeList.Add(new ThemeSettings
            {
                Background = White,
                Foreground = PrimaryText
            });
            ThemeList.Add(new ThemeSettings
            {
                Background = Sepia,
                Foreground = PrimaryText
            });
            ThemeList.Add(new ThemeSettings
            {
                Background = Brown,
                Foreground = White
            });
            ThemeList.Add(new ThemeSettings
            {
                Background = Gray,
                Foreground = White
            });
            ThemeList.Add(new ThemeSettings
            {
                Background = PrimaryText,
                Foreground = White
            });
            ThemeList.Add(new ThemeSettings
            {
                Background = Green,
                Foreground = Primary,
            });
            ThemeList.Add(new ThemeSettings
            {
                Background = Orange,
                Foreground = White
            });
            ThemeList.Add(new ThemeSettings
            {
                Background = Blue,
                Foreground = Primary,
            });
            ThemeList.Add(new ThemeSettings
            {
                Background = Purple,
                Foreground = Primary
            });
            ThemeList.Add(new ThemeSettings
            {
                Background = Pink,
                Foreground = White
            });

            if (DeviceInfo.Platform == DevicePlatform.iOS)
                FontList.Add(new FontSettings
                {
                    Name = "SFPro"
                });
            else
                FontList.Add(new FontSettings
                {
                    Name = "Roboto"
                });
            FontList.Add(new FontSettings
            {
                Name = "NotoSerif"
            });
            FontList.Add(new FontSettings
            {
                Name = "ChelseaMarket"
            });
            FontList.Add(new FontSettings
            {
                Name = "UnifrakturMaguntia"
            });
            FontList.Add(new FontSettings
            {
                Name = "StyleScript"
            });
            FontList.Add(new FontSettings
            {
                Name = "Frosty",
                CharacterSpacing = 1.5
            });
            FontList.Add(new FontSettings
            {
                Name = "KGKissMeSlowly",
                CharacterSpacing = 1.25
            });
            FontList.Add(new FontSettings
            {
                Name = "KGMelonheadz",
                CharacterSpacing = 1.5
            });
            FontList.Add(new FontSettings
            {
                Name = "KGWhattheTeacherWants"
            });
            FontList.Add(new FontSettings
            {
                Name = "Cookie"
            });

            BookmarkList.CollectionChanged += BookmarkList_CollectionChanged;
            hymnInputType = (InputType)Preferences.Get(PreferencesVar.HYMN_INPUT_TYPE, (int)InputType.Numpad);
        }

        #region Properties
        private static Globals instance = null;
        [JsonIgnore]
        public static Globals Instance
        {
            get
            {
                instance ??= new Globals();
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

        private bool isFetchingSyncDetails;
        [JsonIgnore]
        public bool IsFetchingSyncDetails
        {
            get => isFetchingSyncDetails;
            set
            {
                isFetchingSyncDetails = value;
                OnIsFetchingSyncDetailsChanged(value);
            }
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
            set
            {
                if (bookmarkList != null)
                    bookmarkList.CollectionChanged -= BookmarkList_CollectionChanged;

                bookmarkList = value ?? new ObservableRangeCollection<ShortHymn>();
                bookmarkList.CollectionChanged += BookmarkList_CollectionChanged;
            }
        }

        private ObservableRangeCollection<string> searchList = new ObservableRangeCollection<string>();
        public ObservableRangeCollection<string> SearchList
        {
            get { return searchList; }
            set { searchList = value; }
        }

        private InputType hymnInputType;
        public InputType HymnInputType
        {
            get { return hymnInputType; }
            set
            {
                if (hymnInputType != value)
                {
                    hymnInputType = value;
                    Preferences.Set(PreferencesVar.HYMN_INPUT_TYPE, (int)value);
                    OnHymnInputTypeChanged(value);
                    if (!suppressSettingsSave)
                        SaveSettings();
                }
            }
        }

        private Hymn activeHymn;
        public Hymn ActiveHymn
        {
            get { return activeHymn; }
            set
            {
                if (value == null)
                    return;

                if (activeHymn == null || !activeHymn.Equals(value))
                {
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
                    SaveSettings();
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
                    activeAlignment = value;
                    OnAlignmentChanged(activeAlignment);
                    SaveSettings();
                }
            }
        }

        private Color activeThemeText;
        [JsonIgnore]
        public Color ActiveThemeText {
            get => activeThemeText;
            private set => activeThemeText = value;
        }

        private Color activeReadTheme = Colors.White;
        public Color ActiveReadTheme
        {
            get { return activeReadTheme; }
            set
            {
                if (!activeReadTheme.Equals(value))
                {
                    activeReadTheme = value;

                    ActiveThemeText = ThemeList.Find(theme => theme.Background.Equals(value))?.Foreground ?? PrimaryText;
                    OnActiveReadThemeChanged(value);
                    SaveSettings();
                }
            }
        }

        private double activeFontSize = 20;
        public double ActiveFontSize
        {
            get => activeFontSize;
            set
            {
                if (Math.Abs(activeFontSize - value) > 0.01)
                {
                    activeFontSize = value;
                    OnActiveFontSizeChanged(activeFontSize);
                    SaveSettings();
                }
            }
        }

        private string activeFont = DeviceInfo.Platform == DevicePlatform.Android ? "Roboto" : "SFPro";
        public string ActiveFont
        {
            get => activeFont;
            set
            {
                if (activeFont != value)
                {
                    activeFont = value;
                    OnActiveFontChanged(value);
                    SaveSettings();
                }
            }
        }

        private bool darkMode = false;
        public bool DarkMode
        {
            get => darkMode;
            set
            {
                if (darkMode == value)
                    return;

                darkMode = value;
                Application.Current.UserAppTheme = value ? AppTheme.Dark : AppTheme.Light;
                Preferences.Set(PreferencesVar.DARK_MODE, value);
                OnDarkModeChanged(value);
            }
        }

        private bool keepAwake = true;
        public bool KeepAwake
        {
            get => keepAwake;
            set
            {
                if (keepAwake == value)
                    return;

                keepAwake = value;
                DeviceDisplay.KeepScreenOn = value;
                Preferences.Set(PreferencesVar.KEEP_AWAKE, value);
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
        public event EventHandler IsFetchingSyncDetailsChanged;

        private void OnActiveHymnChanged(Hymn value)
        {
            RaiseOnMainThread(() => ActiveHymnChanged?.Invoke(value, EventArgs.Empty));
        }
        private void OnHymnInputTypeChanged(InputType value)
        {
            if (HymnInputTypeChanged != null) HymnInputTypeChanged(value, EventArgs.Empty);
        }
        private void OnBookmarksChanged(ObservableRangeCollection<ShortHymn> value, EventArgs eventArgs = null)
        {
            var args = eventArgs ?? EventArgs.Empty;
            RaiseOnMainThread(() => BookmarksChanged?.Invoke(value, args));
        }
        private void OnHistoryChanged(ObservableRangeCollection<ShortHymn> value)
        {
            RaiseOnMainThread(() => HistoryChanged?.Invoke(value, EventArgs.Empty));
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
            IsDownloadUiActive = true;
            RefreshIncompleteDownloadState();
            RaiseOnMainThread(() => DownloadStarted?.Invoke(value, EventArgs.Empty));
        }
        private void OnDownloadProgressed(string value)
        {
            LastDownloadProgressMessage = value;
            RaiseOnMainThread(() => DownloadProgressed?.Invoke(value, EventArgs.Empty));
        }
        public void OnDownloadError(string value)
        {
            IsDownloadUiActive = false;
            LastDownloadProgressMessage = null;
            RefreshIncompleteDownloadState();
            RaiseOnMainThread(() => DownloadError?.Invoke(value, EventArgs.Empty));
        }
        public void OnInitFinished(string value)
        {
            initComplete = true;
            IsDownloadUiActive = false;
            LastDownloadProgressMessage = null;
            RefreshIncompleteDownloadState();
            RaiseOnMainThread(() => InitFinished?.Invoke(value, EventArgs.Empty));
        }

        static void RaiseOnMainThread(Action action)
        {
            if (MainThread.IsMainThread)
                action();
            else
                MainThread.BeginInvokeOnMainThread(action);
        }

        void EnsureDownloadCancellationReady()
        {
            try
            {
                if (CTS.IsCancellationRequested)
                {
                    CTS.Dispose();
                    CTS = new CancellationTokenSource();
                }
            }
            catch
            {
                CTS = new CancellationTokenSource();
            }
        }
        public void OnIsFetchingSyncDetailsChanged(bool value)
        {
            if (IsFetchingSyncDetailsChanged != null) IsFetchingSyncDetailsChanged(value, EventArgs.Empty);
        }

        private void BookmarkList_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnBookmarksChanged(sender as ObservableRangeCollection<ShortHymn>, e);
        }
        #endregion

        #region Methods
        public async Task<bool> HasIncompleteDownload()
        {
            return await new HttpHelper().LoadCheckpoint() != null;
        }

        public bool IsDownloadRecoveryPending
        {
            get
            {
                if (IsDownloadUiActive || HasIncompleteDownloadOnDisk)
                    return true;
#if ANDROID
                if (DownloadForegroundService.IsRunning)
                    return true;
#endif
                return false;
            }
        }

        public void RefreshIncompleteDownloadState()
        {
            HasIncompleteDownloadOnDisk = File.Exists(
                AppStorage.GetPath(folderRootName, "download_checkpoint.json"));

#if ANDROID
            var serviceRunning = DownloadForegroundService.IsRunning;
#else
            var serviceRunning = false;
#endif

            if (HasIncompleteDownloadOnDisk || serviceRunning)
            {
                var becameActive = !IsDownloadUiActive;
                IsDownloadUiActive = true;
                if (becameActive && !InitInProgress)
                    RaiseOnMainThread(() => DownloadStarted?.Invoke(string.Empty, EventArgs.Empty));
            }
        }

        public void TryResumeInitAfterRelaunch()
        {
            if (Preferences.Get(PreferencesVar.IS_NEW, true) || InitInProgress)
                return;

            RefreshIncompleteDownloadState();

#if ANDROID
            var serviceRunning = DownloadForegroundService.IsRunning;
#else
            var serviceRunning = false;
#endif

            // Only auto-resume after a cold start / force-close — not for in-session syncs.
            if (!HasIncompleteDownloadOnDisk && !serviceRunning)
                return;

            Init();
        }

        async Task WaitForResyncDetailsAsync(int maxWaitMs = 30000)
        {
            var waited = 0;
            while (waited < maxWaitMs)
            {
                if (!IsFetchingSyncDetails && ResyncDetails.Count > 0)
                    return;

                await Task.Delay(200);
                waited += 200;
            }
        }

        async Task EnsureHymnsAndSettingsLoadedAsync()
        {
            var httpHelper = new HttpHelper();
            if ((HymnList == null || HymnList.Count == 0) && await httpHelper.HymnListFileExists())
                HymnList = await httpHelper.ReadHymns();

            if (await LoadSettings())
                RestoreActiveHymnFromList(rebindOnly: true);
        }

        public async Task<bool> DownloadReadHymns(bool forceSync = false)
        {
            HttpHelper httpHelper = new HttpHelper();
            var checkpoint = await httpHelper.LoadCheckpoint();

            // Resume interrupted full download (first install or force resync)
            if (checkpoint != null && checkpoint.NextSyncDetailIndex == null)
            {
                if (!HttpHelper.IsConnected())
                {
                    OnDownloadError("Please connect to download resources.");
                    return true;
                }

                EnsureDownloadCancellationReady();
                OnDownloadStarted("");
                var resumeProgress = new Progress<string>(report => OnDownloadProgressed(report));
                var origForSync = checkpoint.ForceSync ? await httpHelper.ReadBackupHymns() : null;
                HymnList = await httpHelper.DownloadHymns(resumeProgress, CTS.Token, origForSync, checkpoint.ForceSync);
                return true;
            }

            var exists = await httpHelper.HymnListFileExists();

            if (exists && !forceSync)
                HymnList = await httpHelper.ReadHymns();
            else if (HttpHelper.IsConnected())
            {
                if (forceSync && exists)
                    await httpHelper.BackupHymnsForForceSync();

                EnsureDownloadCancellationReady();
                OnDownloadStarted("");
                Progress<string> downloadProgress = new Progress<string>((report) =>
                {
                    OnDownloadProgressed(report);
                });
                HymnList = await httpHelper.DownloadHymns(downloadProgress, CTS.Token, forceSync ? HymnList : null, forceSync);
            }
            else
            {
                var errorMessage = "Please connect to download resources.";
                OnDownloadError(errorMessage);
            }

            return true;
        }

        public async Task<bool> ResyncHymns()
        {
            if (!TryBeginDownloadOperation())
                return false;

            try
            {
                EnsureDownloadCancellationReady();
                OnDownloadStarted("");
                HymnList = await ResyncHymnsInternal(new HttpHelper());
                return true;
            }
            finally
            {
                EndDownloadOperation();
            }
        }

        public async Task<bool> ResyncSelectedHymns(string hymnNumbersInput)
        {
            if (!TryBeginDownloadOperation())
                return false;

            try
            {
                if (!HttpHelper.IsConnected())
                {
                    OnDownloadError("Please connect to download resources.");
                    return false;
                }

                await EnsureHymnsAndSettingsLoadedAsync();
                if (HymnList == null || HymnList.Count == 0)
                {
                    OnDownloadError("No hymns loaded.");
                    return false;
                }

                var numbers = ParseHymnNumberList(hymnNumbersInput).ToList();
                if (numbers.Count == 0)
                {
                    OnDownloadError("Enter valid hymn numbers (e.g. 132 or 77s, 801).");
                    return false;
                }

                EnsureDownloadCancellationReady();
                OnDownloadStarted("");

                var httpHelper = new HttpHelper();
                var progress = new Progress<string>(report => OnDownloadProgressed(report));
                var (succeeded, failed) = await httpHelper.SyncHymns(
                    numbers, HymnList, progress, CTS.Token, saveToDisk: false);

                if (succeeded == 0)
                {
                    OnDownloadError(failed > 0
                        ? "Could not re-sync the selected hymns."
                        : "No hymns were re-synced.");
                    return false;
                }

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    SortHymnListInPlace(HymnList);
                    if (activeHymn != null &&
                        numbers.Any(n => string.Equals(n, activeHymn.Number, StringComparison.OrdinalIgnoreCase)))
                    {
                        RestoreActiveHymnFromList(rebindOnly: true);
                    }

                    RefreshBookmarkFirstLines();
                });

                await httpHelper.SaveHymns(HymnList);
                SaveSettings();
                RefreshIncompleteDownloadState();
                OnDownloadProgressed(failed > 0
                    ? $"Re-synced {succeeded} hymn(s); {failed} failed."
                    : $"Re-synced {succeeded} hymn(s).");
                OnInitFinished("sync");
                return true;
            }
            catch (Exception ex)
            {
                OnDownloadError(ex.Message);
                return false;
            }
            finally
            {
                EndDownloadOperation();
            }
        }

        public static IEnumerable<string> ParseHymnNumberList(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                yield break;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var number = part.ToLowerInvariant();
                if (!seen.Add(number))
                    continue;

                if (System.Text.RegularExpressions.Regex.IsMatch(number, @"^\d+[stf]?$"))
                    yield return number;
            }
        }

        async Task<HymnList> ResyncHymnsInternal(HttpHelper httpHelper)
        {
            var checkpoint = await httpHelper.LoadCheckpoint();
            Progress<string> downloadProgress = new Progress<string>((report) =>
            {
                OnDownloadProgressed(report);
            });

            var startIndex = checkpoint?.NextSyncDetailIndex ?? 0;
            if (startIndex >= ResyncDetails.Count)
                startIndex = 0;
            return await httpHelper.SyncChanges(
                downloadProgress,
                CTS.Token,
                ResyncDetails,
                HymnList,
                startIndex);
        }

        public async void Init()
        {
            if (!TryBeginDownloadOperation())
                return;

            var httpHelper = new HttpHelper();
            DownloadCheckpoint checkpoint;
            try
            {
                checkpoint = await httpHelper.LoadCheckpoint();
            }
            catch
            {
                EndDownloadOperation();
                throw;
            }

            RefreshIncompleteDownloadState();

            if (initComplete && HymnList?.Count > 0 && checkpoint == null)
            {
                OnInitFinished(null);
                return;
            }

            try
            {
                if (checkpoint?.NextSyncDetailIndex != null)
                {
                    await EnsureHymnsAndSettingsLoadedAsync();
                    await WaitForResyncDetailsAsync();
                    if (ResyncDetails.Count == 0)
                    {
                        OnDownloadError("Could not resume sync. Please try again from Settings.");
                        return;
                    }

                    OnDownloadStarted("");
                    EnsureDownloadCancellationReady();
                    HymnList = await ResyncHymnsInternal(httpHelper);
                    await FinishAfterDownloadAsync(isUserSync: true);
                }
                else if (await DownloadReadHymns())
                {
                    await FinishAfterDownloadAsync(isUserSync: checkpoint?.ForceSync ?? false);
                }
            }
            catch (Exception ex)
            {
                OnDownloadError(ex.Message);
            }
            finally
            {
                EndDownloadOperation();
                RefreshIncompleteDownloadState();
            }
        }

        /// <summary>
        /// After any download or sync, sort hymns and restore user preferences without resetting them.
        /// </summary>
        public async Task FinishAfterDownloadAsync(bool isUserSync)
        {
            if (HymnList?.Count > 0)
                HymnList = SortHymnList(HymnList);

            var settingsFileExists = SettingsFileExists();
            var settingsLoaded = await LoadSettings();

            if (settingsLoaded)
                RestoreActiveHymnFromList(rebindOnly: true);
            else if (!settingsFileExists && HymnList?.Count > 0 && activeHymn == null)
            {
                ActiveHymn = HymnList[0];
                activeReadTheme = Colors.White;
            }
            else
                RestoreActiveHymnFromList(rebindOnly: true);

            RefreshBookmarkFirstLines();
            if (settingsLoaded || !settingsFileExists)
                SaveSettings();
            RefreshIncompleteDownloadState();
            OnInitFinished(isUserSync ? "sync" : null);
        }

        static HymnList SortHymnList(HymnList list)
        {
            SortHymnListInPlace(list);
            return list;
        }

        static void SortHymnListInPlace(HymnList list)
        {
            if (list == null || list.Count < 2)
                return;

            var sorted = list.OrderBy(HymnSortKey).ToList();
            list.Clear();
            list.AddRange(sorted);
        }

        static double HymnSortKey(Hymn hymn)
        {
            if (string.IsNullOrWhiteSpace(hymn?.Number))
                return double.MaxValue;

            var normalized = hymn.Number.Replace("f", ".4").Replace("s", ".2").Replace("t", ".3");
            return double.TryParse(normalized, out var value) ? value : double.MaxValue;
        }

        void RefreshBookmarkFirstLines()
        {
            if (HymnList == null || BookmarkList == null)
                return;

            var changed = false;
            foreach (var bookmark in BookmarkList)
            {
                var hymn = HymnList.FirstOrDefault(h => h?.Number == bookmark.Number);
                if (hymn == null || hymn.FirstLine == bookmark.Line)
                    continue;

                bookmark.Line = hymn.FirstLine;
                changed = true;
            }

            if (changed)
                OnBookmarksChanged(BookmarkList);
        }

        void RestoreActiveHymnFromList(bool rebindOnly = false)
        {
            if (HymnList == null || HymnList.Count == 0)
                return;

            var hymn = (activeHymn == null || string.IsNullOrEmpty(activeHymn.Number))
                ? HymnList[0]
                : HymnList.FirstOrDefault(h => h?.Number == activeHymn.Number) ?? HymnList[0];

            if (rebindOnly)
            {
                activeHymn = hymn;
                OnActiveHymnChanged(hymn);
                return;
            }

            ActiveHymn = hymn;
        }

        public bool IsBookmarked()
        {
            return (from x in BookmarkList where x.Number == ActiveHymn.Number select x).Any();
        }

        public void AddBookmark(string groupName = "General")
        {
            if (!IsBookmarked())
            {
                var val = new Models.ShortHymn
                {
                    Number = ActiveHymn.Number,
                    Line = ActiveHymn.FirstLine,
                    BookmarkGroup = groupName
                };
                BookmarkList.Add(val);
                OnBookmarksChanged(BookmarkList);
            }
        }

        public bool RemoveBookmark(ShortHymn hymn)
        {
            try
            {
                var res = BookmarkList.Remove(hymn);
                OnBookmarksChanged(BookmarkList);
                return res;
            }
            catch (Exception)
            {
               return false;
            }
        }

        public bool RemoveBookmarks(IEnumerable<ShortHymn> hymns)
        {
            try
            {
                var bookmarks = hymns?.ToList() ?? new List<ShortHymn>();
                if (bookmarks.Count == 0)
                    return false;

                foreach (var bookmark in bookmarks)
                    BookmarkList.Remove(bookmark);

                OnBookmarksChanged(BookmarkList);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool RemoveBookmark()
        {
            try
            {
                var filtered = BookmarkList
                                    .Where(bk => bk.Number == ActiveHymn.Number).First();
                var res = BookmarkList.Remove(filtered);
                OnBookmarksChanged(BookmarkList);
                return res;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool RemoveBookmarkGroup(string groupName)
        {
            try
            {
                var bookmarks = BookmarkList
                    .Where(bookmark => bookmark.BookmarkGroup == groupName)
                    .ToList();

                if (bookmarks.Count == 0)
                    return false;

                foreach (var bookmark in bookmarks)
                    BookmarkList.Remove(bookmark);

                OnBookmarksChanged(BookmarkList);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async void SaveSettings()
        {
            if (suppressSettingsSave)
                return;

            try
            {
                await settingsSaveLock.WaitAsync();
                if (suppressSettingsSave)
                    return;

                var settings = JsonConvert.SerializeObject(Globals.Instance);
                var folderPath = AppStorage.GetPath(folderRootName);
                Directory.CreateDirectory(folderPath);
                await File.WriteAllTextAsync(Path.Combine(folderPath, settingsName), settings);
            }
            catch (Exception)
            {
            }
            finally
            {
                if (settingsSaveLock.CurrentCount == 0)
                    settingsSaveLock.Release();
            }
        }

        public bool SettingsFileExists() =>
            File.Exists(AppStorage.GetPath(folderRootName, settingsName));

        public async Task<bool> LoadSettings()
        {
            suppressSettingsSave = true;
            var previousHymnInputType = hymnInputType;
            var previousActiveHymn = activeHymn;
            var previousActiveReadTheme = activeReadTheme;
            var previousActiveThemeText = activeThemeText;
            var previousActiveAlignment = activeAlignment;
            var previousHistoryList = HistoryList;
            var previousBookmarkList = BookmarkList;
            var previousSearchList = SearchList;
            var previousActiveFontSize = activeFontSize;
            var previousActiveFont = activeFont;
            var previousDarkMode = darkMode;
            var previousKeepAwake = keepAwake;
            var previousIsOrientationLocked = isOrientationLocked;

            try
            {
                var filePath = AppStorage.GetPath(folderRootName, settingsName);
                if (!File.Exists(filePath))
                    return false;

                var settings = await File.ReadAllTextAsync(filePath);
                var json = await Task.Run(() => JsonConvert.DeserializeObject<Dictionary<string, object>>(settings));
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
                            ActiveReadTheme = ParseSavedColor(entry.Value);
                            break;
                        case nameof(ActiveAlignment):
                            ActiveAlignment = (TextAlignment)int.Parse(entry.Value + "");
                            break;
                        case nameof(HistoryList):
                            HistoryList = ((JArray)entry.Value).Select(x => new ShortHymn
                            {
                                Number = (string)x["Number"],
                                TimeStamp = x["TimeStamp"]?.ToObject<DateTime>() ?? DateTime.UtcNow,
                                Line = (string)x["Line"]
                            }).ToObservableRangeCollection();
                            break;
                        case nameof(BookmarkList):
                            BookmarkList = ((JArray)entry.Value).Select(x => new ShortHymn
                            {
                                Number = (string)x["Number"],
                                TimeStamp = x["TimeStamp"]?.ToObject<DateTime>() ?? DateTime.UtcNow,
                                Line = (string)x["Line"],
                                BookmarkGroup = (string)x["BookmarkGroup"] ?? "General"
                            }).ToObservableRangeCollection();
                            break;
                        case nameof(SearchList):
                            SearchList = ((JArray)entry.Value).Select(x => (string)x).ToObservableRangeCollection();
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
                            IsOrientationLocked = (bool)entry.Value;
                            break;
                        default:
                            var property = GetType().GetProperty(entry.Key);
                            if (property?.CanWrite == true)
                                property.SetValue(this, entry.Value, null);
                            break;
                    }
                }

                return true;
            }
            catch (Exception)
            {
                hymnInputType = previousHymnInputType;
                activeHymn = previousActiveHymn;
                activeReadTheme = previousActiveReadTheme;
                activeThemeText = previousActiveThemeText;
                activeAlignment = previousActiveAlignment;
                HistoryList = previousHistoryList;
                BookmarkList = previousBookmarkList;
                SearchList = previousSearchList;
                activeFontSize = previousActiveFontSize;
                activeFont = previousActiveFont;
                darkMode = previousDarkMode;
                keepAwake = previousKeepAwake;
                isOrientationLocked = previousIsOrientationLocked;
                return false;
            }
            finally
            {
                suppressSettingsSave = false;
            }
        }

        private Color ParseSavedColor(object value)
        {
            try
            {
                if (value is JObject colorObject)
                {
                    var parsed = colorObject.ToColor();
                    if (parsed is Color savedColor)
                        return savedColor;
                }

                var colorString = value?.ToString();
                if (!string.IsNullOrWhiteSpace(colorString))
                    return Color.FromArgb(colorString);
            }
            catch (Exception)
            {
            }

            return Colors.White;
        }
        public void ForceBookmarkChangedEvent()
        {
            OnBookmarksChanged(BookmarkList);
        }
        #endregion

        #region ToastPopup
        public static async void ShowToastPopup(string source, string label)
        {
            ToastPopup toastPopup = new ToastPopup
            {
                PopupAnim = source,
                PopupLabel = label,
                CanBeDismissedByTappingOutsideOfPopup = false,
            };
            Application.Current.MainPage.ShowPopup(toastPopup);
            await new TaskFactory().StartNew(() => { Thread.Sleep(2000); });

            MainThread.BeginInvokeOnMainThread(() => { toastPopup.Close(null); });
        }

        public static async void ShowToastPopup(string source, string label, double size)
        {
            ToastPopup toastPopup = new ToastPopup
            {
                PopupAnim = source,
                PopupLabel = label,
                PopupAnimSize = size,
                CanBeDismissedByTappingOutsideOfPopup = false,
            };

            Application.Current.MainPage.ShowPopup(toastPopup);

            await new TaskFactory().StartNew(() => { Thread.Sleep(2000); });
            MainThread.BeginInvokeOnMainThread(() => { toastPopup.Close(null); });
        }

        public static async void ShowToastPopup(string source, string label, double size, Rect layoutBounds)
        {
            ToastPopup toastPopup = new ToastPopup
            {
                PopupAnim = source,
                PopupLabel = label,
                PopupAnimSize = size,
                LayoutBounds = layoutBounds,
                CanBeDismissedByTappingOutsideOfPopup = false,
            };

            Application.Current.MainPage.ShowPopup(toastPopup);

            await new TaskFactory().StartNew(() => { Thread.Sleep(2000); });
            MainThread.BeginInvokeOnMainThread(() => { toastPopup.Close(null); });
        }

        #endregion
    }
}
