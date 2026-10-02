using System.Globalization;
using MobiHymn4.Shared;
using MobiHymn4.Shared.Models;
using MobiHymn4.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MobiHymn4.Web.Services;

public sealed class AppState
{
    readonly IHymnLyricsSource lyrics;
    readonly ILocalUserDataStore store;
    readonly IAppPreferences prefs;
    readonly IServiceProvider services;

    public AppState(
        IHymnLyricsSource lyrics,
        ILocalUserDataStore store,
        IAppPreferences prefs,
        IServiceProvider services)
    {
        this.lyrics = lyrics;
        this.store = store;
        this.prefs = prefs;
        this.services = services;
    }

    IUserSettingsSyncService Sync => services.GetRequiredService<IUserSettingsSyncService>();

    public event Action? Changed;
    public event Action<ToastRequest>? ToastRequested;

    public sealed record ToastRequest(string LottieSrc, string Message, int DurationMs = 1500);

    /// <summary>Latest toast request; also raised via <see cref="Changed"/> so UI never misses it.</summary>
    public ToastRequest? PendingToast { get; private set; }
    public int ToastVersion { get; private set; }

    public void ShowToast(string lottieSrc, string message, int durationMs = 1500)
    {
        PendingToast = new ToastRequest(lottieSrc, message, durationMs);
        ToastVersion++;
        ToastRequested?.Invoke(PendingToast);
        Notify();
    }

    public Hymn? ActiveHymn { get; private set; }
    public List<ShortHymn> Bookmarks { get; private set; } = new();
    public List<ShortHymn> History { get; private set; } = new();
    public UserSettingsCloudDoc Settings { get; private set; } = new();
    public bool IsBusy { get; private set; }
    public string? StatusMessage { get; private set; }

    public bool DarkMode => Settings.DarkMode;

    /// <summary>Compact radial vs expanded sheet. Device-local; survives hymn changes and remounts.</summary>
    public bool MidiPanelExpanded => prefs.GetBool(PrefKeys.MidiPanelExpanded, false);

    public void SetMidiPanelExpanded(bool value) =>
        prefs.SetBool(PrefKeys.MidiPanelExpanded, value);

    /// <summary>MP3 play/pause fade length in seconds (0.5–2, half-second steps). Device-local.</summary>
    public double Mp3FadeSeconds
    {
        get
        {
            var raw = prefs.Get(PrefKeys.Mp3FadeSeconds, "1");
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                return 1;
            return Math.Clamp(Math.Round(seconds * 2) / 2, 0.5, 2);
        }
    }

    public void SetMp3FadeSeconds(double seconds)
    {
        var value = Math.Clamp(Math.Round(seconds * 2) / 2, 0.5, 2);
        prefs.Set(PrefKeys.Mp3FadeSeconds, value.ToString("0.#", CultureInfo.InvariantCulture));
    }

    public string ReaderTheme => Settings.ActiveReadTheme ?? "#FFFFFF";
    public string ReaderFont
    {
        get
        {
            // Keys match MAUI SettingsPopup / MauiProgram font aliases.
            var key = string.IsNullOrWhiteSpace(Settings.ActiveFont)
                ? "Roboto"
                : Settings.ActiveFont.Trim();
            return key switch
            {
                "Roboto" => "Roboto, system-ui, sans-serif",
                "SFPro" => "SFPro, -apple-system, system-ui, sans-serif",
                "NotoSerif" => "NotoSerif, Georgia, serif",
                "ChelseaMarket" => "ChelseaMarket, cursive",
                "UnifrakturMaguntia" => "UnifrakturMaguntia, fantasy",
                "StyleScript" => "StyleScript, cursive",
                "Cookie" => "Cookie, cursive",
                "Frosty" => "Frosty, cursive",
                "KGKissMeSlowly" => "KGKissMeSlowly, cursive",
                "KGMelonheadz" => "KGMelonheadz, cursive",
                "KGWhattheTeacherWants" => "KGWhattheTeacherWants, cursive",
                "DancingScript" => "DancingScript, cursive",
                _ => key
            };
        }
    }
    public double ReaderFontSize => Settings.ActiveFontSize <= 0 ? 20 : Settings.ActiveFontSize;
    public double ReaderLineSpacing => Settings.ActiveLineSpacing <= 0 ? 1 : Settings.ActiveLineSpacing;
    public double ReaderLetterSpacing => Settings.ActiveLetterSpacing;
    /// <summary>0 = left, 1 = center, 2 = right (mirrors MAUI TextAlignment).</summary>
    public string ReaderTextAlign => Settings.ActiveAlignment switch
    {
        1 => "center",
        2 => "right",
        _ => "left"
    };

    /// <summary>0 = Grid, 1 = Numpad, 2 = Voice (mirrors MAUI InputType).</summary>
    public int HymnInputType =>
        Settings.HymnInputType is >= 0 and <= 2 ? Settings.HymnInputType : 1;

    /// <summary>
    /// MAUI ReadViewModel.IsReadView — when false, top bar + FAB hide for immersive reading.
    /// </summary>
    public bool IsReadView { get; private set; } = true;

    public void SetReadView(bool visible)
    {
        if (IsReadView == visible) return;
        IsReadView = visible;
        Notify();
    }

    public void ToggleReadView() => SetReadView(!IsReadView);

    public async Task SetHymnInputTypeAsync(int value)
    {
        if (value is < 0 or > 2) value = 1;
        prefs.Set(PrefKeys.HymnInputType, value.ToString());
        await UpdateSettingsAsync(s => s.HymnInputType = value);
    }

    public async Task InitializeAsync()
    {
        Bookmarks = await store.GetBookmarksAsync();
        History = await store.GetHistoryAsync();
        Settings = await store.GetLocalSettingsAsync();
        prefs.Set(PrefKeys.HymnInputType, HymnInputType.ToString());
        Notify();
    }

    /// <summary>
    /// Moves bookmarks and history entries to the hymn number that now holds their saved
    /// first line (e.g. after a hymn is inserted and later numbers shift up).
    /// Safe to call repeatedly; entries that already match, or whose first line is shared
    /// by several hymns, are left alone.
    /// </summary>
    public async Task RepairRenumberedReferencesAsync(CancellationToken cancellationToken = default)
    {
        if (Bookmarks.Count == 0 && History.Count == 0)
            return;

        HymnCatalogMeta meta;
        try
        {
            meta = await lyrics.GetCatalogMetaAsync(cancellationToken);
        }
        catch
        {
            return;
        }

        // Letters/digits only, so straight, curly, or garbled apostrophes still match.
        static string LineKey(string? line) =>
            new string((line ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

        var numbersByLine = (meta.Hymns ?? new List<HymnCatalogEntry>())
            .Where(h => !string.IsNullOrWhiteSpace(h.Number) && !string.IsNullOrWhiteSpace(h.FirstLine))
            .GroupBy(h => LineKey(h.FirstLine))
            .Where(g => g.Key.Length > 0)
            .ToDictionary(g => g.Key, g => g.Select(h => h.Number).ToList());
        if (numbersByLine.Count == 0)
            return;

        bool Repair(ShortHymn item)
        {
            var line = LineKey(item.Line);
            if (line.Length == 0 || !numbersByLine.TryGetValue(line, out var numbers))
                return false;
            if (numbers.Count != 1 || numbers.Contains(item.Number, StringComparer.OrdinalIgnoreCase))
                return false;
            item.Number = numbers[0];
            return true;
        }

        var bookmarksChanged = false;
        foreach (var b in Bookmarks)
            bookmarksChanged |= Repair(b);
        var historyChanged = false;
        foreach (var h in History)
            historyChanged |= Repair(h);

        if (!bookmarksChanged && !historyChanged)
            return;

        if (bookmarksChanged)
        {
            Bookmarks = Bookmarks
                .GroupBy(b => $"{b.Number}|{b.BookmarkGroup}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(b => b.TimeStamp).First())
                .OrderByDescending(b => b.TimeStamp)
                .ToList();
            await store.SaveBookmarksAsync(Bookmarks);
        }

        if (historyChanged)
        {
            History = History
                .GroupBy(h => h.Number, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(h => h.TimeStamp).First())
                .OrderByDescending(h => h.TimeStamp)
                .ToList();
            await store.SaveHistoryAsync(History);
        }

        Sync.SchedulePush();
        Notify();
    }

    public async Task<Hymn?> OpenHymnAsync(string number)
    {
        try
        {
            IsBusy = true;
            StatusMessage = null;
            Notify();

            var hymn = await lyrics.GetHymnAsync(number);
            ActiveHymn = hymn;
            Settings.LastHymnNumber = hymn.Number;
            prefs.Set(PrefKeys.LastHymnNumber, hymn.Number);

            History = History
                .Where(h => !string.Equals(h.Number, hymn.Number, StringComparison.OrdinalIgnoreCase))
                .ToList();
            History.Insert(0, new ShortHymn
            {
                Number = hymn.Number,
                Line = hymn.FirstLine,
                TimeStamp = DateTime.UtcNow
            });
            if (History.Count > 10)
                History = History.Take(10).ToList();

            await store.SaveHistoryAsync(History);
            await store.SaveLocalSettingsAsync(Settings);
            Sync.SchedulePush();
            return hymn;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            return null;
        }
        finally
        {
            IsBusy = false;
            Notify();
        }
    }

    public async Task<bool> AddBookmarkAsync(string hymnNumber, string groupName, string? line = null)
    {
        groupName = groupName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(hymnNumber) || string.IsNullOrWhiteSpace(groupName))
            return false;

        if (Bookmarks.Any(b => string.Equals(b.Number, hymnNumber, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (string.IsNullOrWhiteSpace(line) && ActiveHymn != null
            && string.Equals(ActiveHymn.Number, hymnNumber, StringComparison.OrdinalIgnoreCase))
            line = ActiveHymn.FirstLine;

        Bookmarks.Insert(0, new ShortHymn
        {
            Number = hymnNumber,
            Line = line ?? "",
            BookmarkGroup = groupName,
            TimeStamp = DateTime.UtcNow
        });
        await store.SaveBookmarksAsync(Bookmarks);
        Sync.SchedulePush();
        Notify();
        return true;
    }

    public async Task RemoveBookmarkAsync(string hymnNumber)
    {
        Bookmarks = Bookmarks
            .Where(b => !string.Equals(b.Number, hymnNumber, StringComparison.OrdinalIgnoreCase))
            .ToList();
        await store.SaveBookmarksAsync(Bookmarks);
        Sync.SchedulePush();
        Notify();
    }

    public async Task<int> RemoveBookmarksAsync(IEnumerable<string> hymnNumbers)
    {
        var remove = new HashSet<string>(
            hymnNumbers.Where(n => !string.IsNullOrWhiteSpace(n)),
            StringComparer.OrdinalIgnoreCase);
        if (remove.Count == 0)
            return 0;

        var before = Bookmarks.Count;
        Bookmarks = Bookmarks.Where(b => !remove.Contains(b.Number)).ToList();
        var removed = before - Bookmarks.Count;
        if (removed == 0)
            return 0;

        await store.SaveBookmarksAsync(Bookmarks);
        Sync.SchedulePush();
        Notify();
        return removed;
    }

    public async Task<int> MoveBookmarksAsync(IEnumerable<string> hymnNumbers, string groupName)
    {
        var key = string.IsNullOrWhiteSpace(groupName) ? "General" : groupName.Trim();
        var move = new HashSet<string>(
            hymnNumbers.Where(n => !string.IsNullOrWhiteSpace(n)),
            StringComparer.OrdinalIgnoreCase);
        if (move.Count == 0)
            return 0;

        var moved = 0;
        foreach (var b in Bookmarks)
        {
            if (!move.Contains(b.Number))
                continue;
            b.BookmarkGroup = key;
            moved++;
        }

        if (moved == 0)
            return 0;

        await store.SaveBookmarksAsync(Bookmarks);
        Sync.SchedulePush();
        Notify();
        return moved;
    }

    public async Task<int> RemoveBookmarkGroupAsync(string groupName)
    {
        var key = string.IsNullOrWhiteSpace(groupName) ? "General" : groupName.Trim();
        var before = Bookmarks.Count;
        Bookmarks = Bookmarks
            .Where(b =>
            {
                var g = string.IsNullOrWhiteSpace(b.BookmarkGroup) ? "General" : b.BookmarkGroup.Trim();
                return !string.Equals(g, key, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
        var removed = before - Bookmarks.Count;
        if (removed == 0)
            return 0;

        await store.SaveBookmarksAsync(Bookmarks);
        Sync.SchedulePush();
        Notify();
        return removed;
    }

    public bool IsBookmarked(string? number) =>
        !string.IsNullOrWhiteSpace(number)
        && Bookmarks.Any(b => string.Equals(b.Number, number, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string> BookmarkGroupNames() =>
        Bookmarks
            .Select(b => string.IsNullOrWhiteSpace(b.BookmarkGroup) ? "General" : b.BookmarkGroup)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();

    public IReadOnlyList<ShortHymn> BookmarksInGroup(string groupName)
    {
        var key = string.IsNullOrWhiteSpace(groupName) ? "General" : groupName.Trim();
        return Bookmarks
            .Where(b =>
            {
                var g = string.IsNullOrWhiteSpace(b.BookmarkGroup) ? "General" : b.BookmarkGroup.Trim();
                return string.Equals(g, key, StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(b => b.Line, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task UpdateSettingsAsync(Action<UserSettingsCloudDoc> mutate)
    {
        mutate(Settings);
        await store.SaveLocalSettingsAsync(Settings);
        Sync.SchedulePush();
        Notify();
    }

    public (int TempoOffset, int Transpose) GetMidiPreferences(string? hymnNumber)
    {
        if (string.IsNullOrWhiteSpace(hymnNumber))
            return (0, 0);

        var item = (Settings.MidiPreferences ?? new List<MidiHymnPreferenceCloudDoc>())
            .FirstOrDefault(p => string.Equals(
                p.Number, hymnNumber.Trim(), StringComparison.OrdinalIgnoreCase));
        return item == null
            ? (0, 0)
            : (Math.Clamp(item.TempoOffset, -60, 60), Math.Clamp(item.Transpose, -6, 5));
    }

    public async Task SetMidiPreferencesAsync(
        string? hymnNumber,
        int tempoOffset,
        int transpose)
    {
        var number = hymnNumber?.Trim() ?? "";
        if (number.Length == 0)
            return;

        tempoOffset = Math.Clamp(
            (int)(Math.Round(tempoOffset / 5.0) * 5),
            -60,
            60);
        transpose = Math.Clamp(transpose, -6, 5);
        Settings.MidiPreferences ??= new List<MidiHymnPreferenceCloudDoc>();

        var item = Settings.MidiPreferences.FirstOrDefault(p =>
            string.Equals(p.Number, number, StringComparison.OrdinalIgnoreCase));
        if (item != null
            && item.TempoOffset == tempoOffset
            && item.Transpose == transpose)
            return;

        if (item == null)
        {
            item = new MidiHymnPreferenceCloudDoc { Number = number };
            Settings.MidiPreferences.Add(item);
        }

        item.Number = number;
        item.TempoOffset = tempoOffset;
        item.Transpose = transpose;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        // Keep the account document bounded while retaining the most recently
        // adjusted hymns. Explicit zeroes are kept so resets sync across devices.
        Settings.MidiPreferences = Settings.MidiPreferences
            .Where(p => !string.IsNullOrWhiteSpace(p.Number))
            .OrderByDescending(p => p.UpdatedAt)
            .Take(500)
            .ToList();

        await store.SaveLocalSettingsAsync(Settings);
        Sync.SchedulePush();
    }

    /// <summary>Live reader font size while pinch-zooming (MAUI 15–40). Persist on gesture end.</summary>
    public Task SetReaderFontSizeAsync(double size, bool persist)
    {
        size = Math.Clamp(size, 15, 40);
        if (Math.Abs(Settings.ActiveFontSize - size) < 0.05 && !persist)
            return Task.CompletedTask;

        Settings.ActiveFontSize = size;
        // Avoid StateHasChanged spam during pinch — DOM already updated by read-pinch.js.
        // Persist path still notifies so settings UI stays in sync.
        if (!persist)
            return Task.CompletedTask;

        Notify();
        return PersistSettingsAsync();
    }

    async Task PersistSettingsAsync()
    {
        await store.SaveLocalSettingsAsync(Settings);
        Sync.SchedulePush();
        Notify();
    }

    public async Task ApplyCloudSettingsAsync(
        UserSettingsCloudDoc? cloud,
        bool replaceAccountData,
        bool preferCloudReaderPrefs = false)
    {
        if (cloud == null)
        {
            if (!replaceAccountData)
                return;

            // Switching to an account with no backup — factory defaults, not the previous account.
            await ResetToDefaultSettingsAsync();
            return;
        }

        var localInputType = HymnInputType;
        var prefRaw = prefs.Get(PrefKeys.HymnInputType, "");
        if (int.TryParse(prefRaw, out var prefInput) && prefInput is >= 0 and <= 2)
            localInputType = prefInput;

        if (replaceAccountData)
        {
            Settings = CloneSettings(cloud);
            if (Settings.HymnInputType is < 0 or > 2)
                Settings.HymnInputType = 1;
            Bookmarks = cloud.Bookmarks?.Select(FromCloud).Where(x => x != null).Cast<ShortHymn>().ToList()
                ?? new List<ShortHymn>();
            History = cloud.History?.Select(FromCloud).Where(x => x != null).Cast<ShortHymn>().ToList()
                ?? new List<ShortHymn>();
        }
        else
        {
            var localUpdatedText = prefs.Get(PrefKeys.CloudUpdatedAt, "");
            var cloudIsNewer = !DateTimeOffset.TryParse(localUpdatedText, out var localUpdated)
                || cloud.UpdatedAt == default
                || cloud.UpdatedAt >= localUpdated;

            // After login, always take cloud reader prefs (don't let a stale local timestamp block sync).
            if (preferCloudReaderPrefs || cloudIsNewer)
                ApplyReaderPrefsFromCloud(cloud);

            // Keep device input mode on same-account merge unless adopting cloud prefs.
            if (!preferCloudReaderPrefs)
                Settings.HymnInputType = localInputType;

            Bookmarks = MergeBookmarks(Bookmarks, cloud.Bookmarks);
            History = MergeHistory(History, cloud.History);
            Settings.MidiPreferences = MergeMidiPreferences(
                Settings.MidiPreferences,
                cloud.MidiPreferences);
        }

        await store.SaveLocalSettingsAsync(Settings, touchUpdatedAt: false);
        await store.SaveBookmarksAsync(Bookmarks);
        await store.SaveHistoryAsync(History);
        prefs.Set(PrefKeys.HymnInputType, Settings.HymnInputType.ToString());
        if (!string.IsNullOrWhiteSpace(Settings.LastHymnNumber))
            prefs.Set(PrefKeys.LastHymnNumber, Settings.LastHymnNumber);
        Notify();
        await RepairRenumberedReferencesAsync();
    }

    /// <summary>
    /// Wipe account-scoped localStorage keys and restore factory defaults in memory.
    /// Keeps <see cref="PrefKeys.CloudOwnerUid"/> so the next login can detect an account switch.
    /// </summary>
    public async Task ClearAccountLocalStorageAndResetAsync()
    {
        foreach (var key in PrefKeys.AccountScopedKeys)
            prefs.Remove(key);
        prefs.ClearPrefix(PrefKeys.GroupMutePrefix);

        Settings = CreateFactoryDefaults();
        Bookmarks = new List<ShortHymn>();
        History = new List<ShortHymn>();
        await store.SaveLocalSettingsAsync(Settings, touchUpdatedAt: true);
        await store.SaveBookmarksAsync(Bookmarks);
        await store.SaveHistoryAsync(History);
        Notify();
    }

    /// <summary>
    /// Clear account-scoped data and restore factory reader prefs (used when adopting an
    /// account that has no Firestore settings doc yet).
    /// </summary>
    public Task ResetToDefaultSettingsAsync() => ClearAccountLocalStorageAndResetAsync();

    public static UserSettingsCloudDoc CreateFactoryDefaults() => new()
    {
        UpdatedAt = DateTimeOffset.UtcNow,
        LastHymnNumber = "1",
        ActiveReadTheme = "#FFFFFF",
        ActiveAlignment = 0,
        ActiveFontSize = 20,
        ActiveFont = "Roboto",
        ActiveLetterSpacing = 0,
        ActiveLineSpacing = 1,
        DarkMode = false,
        KeepAwake = true,
        HymnInputType = 1,
        AgentMode = 0,
        AgentChatLimit = 10,
        History = new List<ShortHymnCloudDoc>(),
        Bookmarks = new List<ShortHymnCloudDoc>(),
        Searches = new List<string>(),
        MidiPreferences = new List<MidiHymnPreferenceCloudDoc>()
    };

    void ApplyReaderPrefsFromCloud(UserSettingsCloudDoc cloud)
    {
        if (!string.IsNullOrWhiteSpace(cloud.ActiveReadTheme))
            Settings.ActiveReadTheme = cloud.ActiveReadTheme;
        Settings.ActiveAlignment = cloud.ActiveAlignment;
        if (cloud.ActiveFontSize > 0)
            Settings.ActiveFontSize = cloud.ActiveFontSize;
        if (!string.IsNullOrWhiteSpace(cloud.ActiveFont))
            Settings.ActiveFont = cloud.ActiveFont;
        Settings.ActiveLetterSpacing = Math.Clamp(cloud.ActiveLetterSpacing, 0, 1);
        if (cloud.ActiveLineSpacing > 0)
            Settings.ActiveLineSpacing = cloud.ActiveLineSpacing;
        Settings.DarkMode = cloud.DarkMode;
        Settings.KeepAwake = cloud.KeepAwake;
        Settings.AgentMode = cloud.AgentMode;
        if (cloud.AgentChatLimit > 0)
            Settings.AgentChatLimit = cloud.AgentChatLimit;
        if (cloud.HymnInputType is >= 0 and <= 2)
            Settings.HymnInputType = cloud.HymnInputType;
        if (!string.IsNullOrWhiteSpace(cloud.LastHymnNumber))
            Settings.LastHymnNumber = cloud.LastHymnNumber;
        if (cloud.UpdatedAt != default)
            Settings.UpdatedAt = cloud.UpdatedAt;
        if (cloud.Searches is { Count: > 0 })
            Settings.Searches = cloud.Searches.Where(s => !string.IsNullOrWhiteSpace(s)).Take(10).ToList();
    }

    static UserSettingsCloudDoc CloneSettings(UserSettingsCloudDoc cloud) => new()
    {
        UpdatedAt = cloud.UpdatedAt,
        LastHymnNumber = cloud.LastHymnNumber ?? "",
        ActiveReadTheme = string.IsNullOrWhiteSpace(cloud.ActiveReadTheme) ? "#FFFFFF" : cloud.ActiveReadTheme,
        ActiveAlignment = cloud.ActiveAlignment,
        ActiveFontSize = cloud.ActiveFontSize <= 0 ? 20 : cloud.ActiveFontSize,
        ActiveFont = cloud.ActiveFont ?? "SFPro",
        ActiveLetterSpacing = cloud.ActiveLetterSpacing,
        ActiveLineSpacing = cloud.ActiveLineSpacing <= 0 ? 1 : cloud.ActiveLineSpacing,
        DarkMode = cloud.DarkMode,
        KeepAwake = cloud.KeepAwake,
        HymnInputType = cloud.HymnInputType,
        AgentMode = cloud.AgentMode,
        AgentChatLimit = cloud.AgentChatLimit <= 0 ? 10 : cloud.AgentChatLimit,
        History = cloud.History?.ToList() ?? new List<ShortHymnCloudDoc>(),
        Bookmarks = cloud.Bookmarks?.ToList() ?? new List<ShortHymnCloudDoc>(),
        Searches = cloud.Searches?.ToList() ?? new List<string>(),
        MidiPreferences = cloud.MidiPreferences?
            .Where(p => !string.IsNullOrWhiteSpace(p.Number))
            .Select(CloneMidiPreference)
            .ToList()
            ?? new List<MidiHymnPreferenceCloudDoc>()
    };

    static MidiHymnPreferenceCloudDoc CloneMidiPreference(MidiHymnPreferenceCloudDoc p) => new()
    {
        Number = p.Number,
        TempoOffset = p.TempoOffset,
        Transpose = p.Transpose,
        UpdatedAt = p.UpdatedAt
    };

    static List<MidiHymnPreferenceCloudDoc> MergeMidiPreferences(
        List<MidiHymnPreferenceCloudDoc>? local,
        List<MidiHymnPreferenceCloudDoc>? cloud)
    {
        var map = new Dictionary<string, MidiHymnPreferenceCloudDoc>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in local ?? Enumerable.Empty<MidiHymnPreferenceCloudDoc>())
        {
            if (string.IsNullOrWhiteSpace(item.Number)) continue;
            map[item.Number.Trim()] = CloneMidiPreference(item);
        }

        foreach (var item in cloud ?? Enumerable.Empty<MidiHymnPreferenceCloudDoc>())
        {
            if (string.IsNullOrWhiteSpace(item.Number)) continue;
            var number = item.Number.Trim();
            if (!map.TryGetValue(number, out var existing)
                || item.UpdatedAt >= existing.UpdatedAt)
                map[number] = CloneMidiPreference(item);
        }

        return map.Values
            .OrderByDescending(p => p.UpdatedAt)
            .Take(500)
            .ToList();
    }

    static List<ShortHymn> MergeBookmarks(List<ShortHymn> local, List<ShortHymnCloudDoc>? cloud)
    {
        var result = local.ToList();
        foreach (var b in cloud ?? Enumerable.Empty<ShortHymnCloudDoc>())
        {
            var item = FromCloud(b);
            if (item == null) continue;
            if (!result.Any(x =>
                    string.Equals(x.Number, item.Number, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.BookmarkGroup, item.BookmarkGroup, StringComparison.OrdinalIgnoreCase)))
                result.Add(item);
        }

        return result;
    }

    static List<ShortHymn> MergeHistory(List<ShortHymn> local, List<ShortHymnCloudDoc>? cloud)
    {
        var map = new Dictionary<string, ShortHymn>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in local)
        {
            if (string.IsNullOrWhiteSpace(h.Number)) continue;
            map[h.Number] = h;
        }

        foreach (var c in cloud ?? Enumerable.Empty<ShortHymnCloudDoc>())
        {
            var item = FromCloud(c);
            if (item == null) continue;
            if (!map.TryGetValue(item.Number, out var existing) || item.TimeStamp > existing.TimeStamp)
                map[item.Number] = item;
        }

        return map.Values
            .OrderByDescending(h => h.TimeStamp)
            .Take(10)
            .ToList();
    }

    public UserSettingsCloudDoc BuildCloudDocument()
    {
        Settings.UpdatedAt = DateTimeOffset.UtcNow;
        Settings.History = History.Take(10).Select(ToCloud).ToList();
        Settings.Bookmarks = Bookmarks.Select(ToCloud).ToList();
        return Settings;
    }

    static ShortHymn? FromCloud(ShortHymnCloudDoc? doc)
    {
        if (doc == null || string.IsNullOrWhiteSpace(doc.Number))
            return null;
        return new ShortHymn
        {
            Number = doc.Number,
            Line = doc.Line ?? "",
            TimeStamp = doc.TimeStamp.UtcDateTime,
            BookmarkGroup = string.IsNullOrWhiteSpace(doc.BookmarkGroup) ? "General" : doc.BookmarkGroup
        };
    }

    static ShortHymnCloudDoc ToCloud(ShortHymn hymn) => new()
    {
        Number = hymn.Number,
        Line = hymn.Line,
        TimeStamp = new DateTimeOffset(hymn.TimeStamp.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(hymn.TimeStamp, DateTimeKind.Utc)
            : hymn.TimeStamp.ToUniversalTime()),
        BookmarkGroup = hymn.BookmarkGroup
    };

    void Notify() => Changed?.Invoke();
}
