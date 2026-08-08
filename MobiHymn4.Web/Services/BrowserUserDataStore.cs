using MobiHymn4.Shared;
using MobiHymn4.Shared.Models;
using MobiHymn4.Shared.Services;
using Newtonsoft.Json;

namespace MobiHymn4.Web.Services;

public sealed class BrowserUserDataStore : ILocalUserDataStore
{
    readonly IAppPreferences prefs;

    public BrowserUserDataStore(IAppPreferences prefs) => this.prefs = prefs;

    public Task<List<ShortHymn>> GetBookmarksAsync() =>
        Task.FromResult(DeserializeList(prefs.Get(PrefKeys.BookmarksJson)));

    public Task SaveBookmarksAsync(IEnumerable<ShortHymn> bookmarks)
    {
        prefs.Set(PrefKeys.BookmarksJson, JsonConvert.SerializeObject(bookmarks.ToList()));
        return Task.CompletedTask;
    }

    public Task<List<ShortHymn>> GetHistoryAsync() =>
        Task.FromResult(DeserializeList(prefs.Get(PrefKeys.HistoryJson)));

    public Task SaveHistoryAsync(IEnumerable<ShortHymn> history)
    {
        prefs.Set(PrefKeys.HistoryJson, JsonConvert.SerializeObject(history.Take(10).ToList()));
        return Task.CompletedTask;
    }

    public Task<UserSettingsCloudDoc> GetLocalSettingsAsync()
    {
        var json = prefs.Get(PrefKeys.SettingsJson);
        UserSettingsCloudDoc settings;
        if (string.IsNullOrWhiteSpace(json))
        {
            settings = BuildDefaults();
        }
        else
        {
            try
            {
                settings = JsonConvert.DeserializeObject<UserSettingsCloudDoc>(json) ?? BuildDefaults();
                // Match MAUI default (true) when older local JSON omitted keepAwake.
                if (json.IndexOf("keepAwake", StringComparison.OrdinalIgnoreCase) < 0)
                    settings.KeepAwake = true;
            }
            catch
            {
                settings = BuildDefaults();
            }
        }

        if (settings.HymnInputType is < 0 or > 2)
            settings.HymnInputType = ParseInputType(prefs.Get(PrefKeys.HymnInputType, "1"));

        // Keep the dedicated pref key mirrored to settings (settings JSON is source of truth).
        prefs.Set(PrefKeys.HymnInputType, settings.HymnInputType.ToString());
        return Task.FromResult(settings);
    }

    public Task SaveLocalSettingsAsync(UserSettingsCloudDoc settings, bool touchUpdatedAt = true)
    {
        if (touchUpdatedAt)
            settings.UpdatedAt = DateTimeOffset.UtcNow;
        prefs.Set(PrefKeys.SettingsJson, JsonConvert.SerializeObject(settings));
        prefs.Set(PrefKeys.LastHymnNumber, settings.LastHymnNumber ?? "");
        prefs.Set(PrefKeys.HymnInputType, settings.HymnInputType.ToString());
        prefs.SetBool(PrefKeys.DarkMode, settings.DarkMode);
        prefs.Set(PrefKeys.ActiveFont, settings.ActiveFont ?? "SFPro");
        prefs.Set(PrefKeys.ActiveFontSize, settings.ActiveFontSize.ToString("0.##"));
        prefs.Set(PrefKeys.ActiveTheme, settings.ActiveReadTheme ?? "#FFFFFF");
        prefs.Set(PrefKeys.ActiveAlignment, settings.ActiveAlignment.ToString());
        prefs.Set(PrefKeys.LetterSpacing, settings.ActiveLetterSpacing.ToString("0.##"));
        prefs.Set(PrefKeys.LineSpacing, settings.ActiveLineSpacing.ToString("0.##"));
        prefs.Set(PrefKeys.AgentMode, settings.AgentMode.ToString());
        prefs.Set(PrefKeys.AgentChatLimit, settings.AgentChatLimit.ToString());
        return Task.CompletedTask;
    }

    UserSettingsCloudDoc BuildDefaults() => new()
    {
        UpdatedAt = DateTimeOffset.UtcNow,
        LastHymnNumber = prefs.Get(PrefKeys.LastHymnNumber, "1"),
        HymnInputType = ParseInputType(prefs.Get(PrefKeys.HymnInputType, "1")),
        ActiveReadTheme = prefs.Get(PrefKeys.ActiveTheme, "#FFFFFF"),
        ActiveFont = prefs.Get(PrefKeys.ActiveFont, "Roboto"),
        ActiveFontSize = double.TryParse(prefs.Get(PrefKeys.ActiveFontSize, "20"), out var fs) ? fs : 20,
        ActiveAlignment = int.TryParse(prefs.Get(PrefKeys.ActiveAlignment, "0"), out var al) ? al : 0,
        ActiveLetterSpacing = double.TryParse(prefs.Get(PrefKeys.LetterSpacing, "0"), out var letters) ? letters : 0,
        ActiveLineSpacing = double.TryParse(prefs.Get(PrefKeys.LineSpacing, "1"), out var ls) ? ls : 1,
        KeepAwake = true,
        AgentChatLimit = int.TryParse(prefs.Get(PrefKeys.AgentChatLimit, "10"), out var lim) ? lim : 10
    };

    static int ParseInputType(string? raw) =>
        int.TryParse(raw, out var v) && v is >= 0 and <= 2 ? v : 1;

    static List<ShortHymn> DeserializeList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<ShortHymn>();
        try
        {
            return JsonConvert.DeserializeObject<List<ShortHymn>>(json) ?? new List<ShortHymn>();
        }
        catch
        {
            return new List<ShortHymn>();
        }
    }
}
