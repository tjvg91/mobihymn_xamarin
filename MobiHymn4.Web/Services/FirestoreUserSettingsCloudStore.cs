using System.Text.Json;
using MobiHymn4.Shared;
using MobiHymn4.Shared.Models;

namespace MobiHymn4.Web.Services;

public sealed class FirestoreUserSettingsCloudStore : IUserSettingsCloudStore
{
    readonly FirebaseJs firebase;

    public FirestoreUserSettingsCloudStore(FirebaseJs firebase) => this.firebase = firebase;

    public Task EnsureReadyAsync() => firebase.EnsureReadyAsync();

    public async Task<UserSettingsCloudDoc?> GetSettingsAsync(string uid)
    {
        await firebase.EnsureReadyAsync();
        var raw = await firebase.GetDocAsync(SettingsPath(uid));
        return MapSettings(raw);
    }

    public async Task SetSettingsAsync(string uid, UserSettingsCloudDoc doc)
    {
        await firebase.EnsureReadyAsync();
        await firebase.SetDocAsync(SettingsPath(uid), ToFirestorePayload(doc));
    }

    static string SettingsPath(string uid) =>
        $"{FirestorePaths.Users}/{uid}/{FirestorePaths.AppData}/{FirestorePaths.SettingsDoc}";

    internal static object ToFirestorePayload(UserSettingsCloudDoc doc) => new
    {
        updatedAt = doc.UpdatedAt.ToUniversalTime().ToString("O"),
        lastHymnNumber = doc.LastHymnNumber ?? "",
        activeReadTheme = string.IsNullOrWhiteSpace(doc.ActiveReadTheme) ? "#FFFFFF" : doc.ActiveReadTheme,
        activeAlignment = doc.ActiveAlignment,
        activeFontSize = doc.ActiveFontSize <= 0 ? 20 : doc.ActiveFontSize,
        activeFont = doc.ActiveFont ?? "",
        activeLetterSpacing = doc.ActiveLetterSpacing,
        activeLineSpacing = doc.ActiveLineSpacing <= 0 ? 1 : doc.ActiveLineSpacing,
        darkMode = doc.DarkMode,
        keepAwake = doc.KeepAwake,
        hymnInputType = doc.HymnInputType,
        agentMode = doc.AgentMode,
        agentChatLimit = doc.AgentChatLimit <= 0 ? 10 : doc.AgentChatLimit,
        history = (doc.History ?? new List<ShortHymnCloudDoc>()).Select(h => new
        {
            number = h.Number ?? "",
            line = h.Line ?? "",
            timeStamp = h.TimeStamp.ToUniversalTime().ToString("O"),
            bookmarkGroup = string.IsNullOrWhiteSpace(h.BookmarkGroup) ? "General" : h.BookmarkGroup
        }).ToList(),
        bookmarks = (doc.Bookmarks ?? new List<ShortHymnCloudDoc>()).Select(h => new
        {
            number = h.Number ?? "",
            line = h.Line ?? "",
            timeStamp = h.TimeStamp.ToUniversalTime().ToString("O"),
            bookmarkGroup = string.IsNullOrWhiteSpace(h.BookmarkGroup) ? "General" : h.BookmarkGroup
        }).ToList(),
        searches = (doc.Searches ?? new List<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Take(10)
            .ToList(),
        midiPreferences = (doc.MidiPreferences ?? new List<MidiHymnPreferenceCloudDoc>())
            .Where(p => !string.IsNullOrWhiteSpace(p.Number))
            .Select(p => new
            {
                number = p.Number.Trim(),
                tempoOffset = p.TempoOffset,
                transpose = p.Transpose,
                updatedAt = p.UpdatedAt.ToUniversalTime().ToString("O")
            })
            .ToList()
    };

    internal static UserSettingsCloudDoc? MapSettings(JsonElement? raw)
    {
        if (raw == null || raw.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        var d = raw.Value;
        return new UserSettingsCloudDoc
        {
            UpdatedAt = GetDateTime(d, "updatedAt") ?? default,
            LastHymnNumber = GetString(d, "lastHymnNumber") ?? "",
            ActiveReadTheme = GetString(d, "activeReadTheme") ?? "#FFFFFF",
            ActiveAlignment = GetInt(d, "activeAlignment"),
            ActiveFontSize = GetDouble(d, "activeFontSize", 20),
            ActiveFont = GetString(d, "activeFont") ?? "SFPro",
            ActiveLetterSpacing = GetDouble(d, "activeLetterSpacing"),
            ActiveLineSpacing = GetDouble(d, "activeLineSpacing", 1),
            DarkMode = GetBool(d, "darkMode"),
            KeepAwake = GetBool(d, "keepAwake"),
            HymnInputType = GetInt(d, "hymnInputType", 1),
            AgentMode = GetInt(d, "agentMode"),
            AgentChatLimit = GetInt(d, "agentChatLimit", 10),
            History = MapHymnList(d, "history"),
            Bookmarks = MapHymnList(d, "bookmarks"),
            Searches = MapStringList(d, "searches"),
            MidiPreferences = MapMidiPreferences(d)
        };
    }

    static List<MidiHymnPreferenceCloudDoc> MapMidiPreferences(JsonElement d)
    {
        if (!TryGetProp(d, "midiPreferences", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return new List<MidiHymnPreferenceCloudDoc>();

        var list = new List<MidiHymnPreferenceCloudDoc>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var number = GetString(item, "number")?.Trim();
            if (string.IsNullOrWhiteSpace(number)) continue;
            list.Add(new MidiHymnPreferenceCloudDoc
            {
                Number = number,
                TempoOffset = GetInt(item, "tempoOffset"),
                Transpose = GetInt(item, "transpose"),
                UpdatedAt = GetDateTime(item, "updatedAt") ?? default
            });
        }

        return list;
    }

    static List<ShortHymnCloudDoc> MapHymnList(JsonElement d, string name)
    {
        if (!TryGetProp(d, name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return new List<ShortHymnCloudDoc>();

        var list = new List<ShortHymnCloudDoc>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var number = GetString(item, "number");
            if (string.IsNullOrWhiteSpace(number)) continue;
            list.Add(new ShortHymnCloudDoc
            {
                Number = number,
                Line = GetString(item, "line") ?? "",
                TimeStamp = GetDateTime(item, "timeStamp") ?? default,
                BookmarkGroup = GetString(item, "bookmarkGroup") ?? "General"
            });
        }

        return list;
    }

    static List<string> MapStringList(JsonElement d, string name)
    {
        if (!TryGetProp(d, name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return new List<string>();
        return arr.EnumerateArray()
            .Select(x => x.GetString()?.Trim() ?? "")
            .Where(x => x.Length > 0)
            .Take(10)
            .ToList();
    }

    static bool TryGetProp(JsonElement d, string name, out JsonElement value)
    {
        if (d.TryGetProperty(name, out value))
            return true;
        var pascal = char.ToUpperInvariant(name[0]) + name[1..];
        return d.TryGetProperty(pascal, out value);
    }

    static string? GetString(JsonElement d, string name) =>
        TryGetProp(d, name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    static bool GetBool(JsonElement d, string name)
    {
        if (!TryGetProp(d, name, out var p)) return false;
        if (p.ValueKind == JsonValueKind.True) return true;
        if (p.ValueKind == JsonValueKind.False) return false;
        if (p.ValueKind == JsonValueKind.String
            && bool.TryParse(p.GetString(), out var b))
            return b;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n))
            return n != 0;
        return false;
    }

    static int GetInt(JsonElement d, string name, int fallback = 0)
    {
        if (!TryGetProp(d, name, out var p)) return fallback;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n)) return n;
        if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var s)) return s;
        return fallback;
    }

    static double GetDouble(JsonElement d, string name, double fallback = 0)
    {
        if (!TryGetProp(d, name, out var p)) return fallback;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var n)) return n;
        if (p.ValueKind == JsonValueKind.String && double.TryParse(p.GetString(), out var s)) return s;
        return fallback;
    }

    static DateTimeOffset? GetDateTime(JsonElement d, string name)
    {
        if (!TryGetProp(d, name, out var p))
            return null;
        if (p.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(p.GetString(), out var parsed))
            return parsed;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var ms))
            return DateTimeOffset.FromUnixTimeMilliseconds(ms);
        if (p.ValueKind == JsonValueKind.Object)
        {
            if (p.TryGetProperty("seconds", out var secEl) && secEl.TryGetInt64(out var sec))
                return DateTimeOffset.FromUnixTimeSeconds(sec);
            if (p.TryGetProperty("_seconds", out var secEl2) && secEl2.TryGetInt64(out var sec2))
                return DateTimeOffset.FromUnixTimeSeconds(sec2);
        }

        return null;
    }
}
