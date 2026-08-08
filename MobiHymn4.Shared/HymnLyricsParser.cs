using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using MobiHymn4.Shared.Models;
using Newtonsoft.Json.Linq;

namespace MobiHymn4.Shared;

public static class HymnLyricsParser
{
    public static Hymn ParseHtmlLyrics(string lyrics, string number)
    {
        lyrics = lyrics.Replace(Environment.NewLine, "<br/>");
        lyrics = Regex.Replace(lyrics, "TAGS>.+<pre>", "TAGS><pre>");
        lyrics = Regex.Replace(lyrics, "<pre>[^A-Z]+", "<pre>");
        lyrics = Regex.Replace(lyrics, "\\r<br\\/>", "<br/>");
        lyrics = Regex.Replace(lyrics, "<br\\/><\\/pre>", "</pre>");
        lyrics = Regex.Replace(lyrics, "\\r", "<br/>");

        if (string.IsNullOrEmpty(lyrics) || Regex.IsMatch(lyrics, "Error:", RegexOptions.IgnoreCase))
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(lyrics) ? "Empty lyrics response." : lyrics);

        var doc = new HtmlDocument();
        doc.LoadHtml(lyrics);
        var preText = doc.DocumentNode.Descendants("pre").SingleOrDefault();
        if (preText == null)
            throw new InvalidOperationException($"No lyrics body for hymn #{number}");

        var firstLine = Regex.Split(preText.InnerHtml, "<br\\s*/?>", RegexOptions.IgnoreCase)
            .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))
            ?.Trim() ?? number;

        return new Hymn
        {
            Lyrics = SanitizeStoredLyrics(lyrics),
            FirstLine = firstLine,
            Title = number,
            Number = number.Trim().ToLowerInvariant()
        };
    }

    public static Hymn? TryParseJsonRecord(string json, string fallbackNumber)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        JObject obj;
        try { obj = JObject.Parse(json); }
        catch { return null; }

        // List/page responses have "hymns"; single-record has "number"
        if (obj["hymns"] != null && obj["number"] == null)
            return null;

        return HymnFromJsonObject(obj, fallbackNumber);
    }

    public static Hymn? HymnFromJsonObject(JObject obj, string fallbackNumber = "")
    {
        if (obj == null) return null;

        var number = (obj.Value<string>("number") ?? fallbackNumber).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(number)) return null;

        var rawLyrics = NormalizeNewlines(obj.Value<string>("lyrics") ?? "");
        var firstLine = NullIfWhiteSpace(obj.Value<string>("firstLine"))
            ?? rawLyrics.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim()
            ?? number;

        var htmlBody = string.IsNullOrEmpty(rawLyrics)
            ? ""
            : WebUtility.HtmlEncode(rawLyrics).Replace("\n", "<br>");

        return new Hymn
        {
            Number = number,
            Title = number,
            Name = NullIfWhiteSpace(obj.Value<string>("title")),
            FirstLine = firstLine,
            Lyrics = htmlBody,
            MidiFileName = NullIfWhiteSpace(obj.Value<string>("midiFileName")),
            Author = NullIfWhiteSpace(obj.Value<string>("author")),
            Metre = NullIfWhiteSpace(obj.Value<string>("metre")),
            Tune = NullIfWhiteSpace(obj.Value<string>("tune")),
            TuneComposer = NullIfWhiteSpace(obj.Value<string>("tuneComposer")),
            TuneKey = NullIfWhiteSpace(obj.Value<string>("tuneKey")),
            Verses = StringArray(obj["verses"] ?? obj["verseRef"]),
            Tags = StringArray(obj["tags"]),
            Year = NullIfWhiteSpace(obj.Value<string>("year")),
            Remark = NullIfWhiteSpace(obj.Value<string>("remark"))
        };
    }

    static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    static string[] StringArray(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
            return Array.Empty<string>();

        IEnumerable<string?> values = token.Type == JTokenType.Array
            ? token.Values<string>()
            : new[] { token.Value<string>() };

        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    static string NormalizeNewlines(string lyrics)
    {
        if (string.IsNullOrEmpty(lyrics)) return "";
        return lyrics
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace('\u2028', '\n')
            .Replace('\u2029', '\n')
            .Replace('\uFFFD', '\'');
    }

    /// <summary>Plain-text lyric lines for searching (strips HTML).</summary>
    public static IEnumerable<string> LyricLines(string? lyricsHtmlOrText)
    {
        if (string.IsNullOrWhiteSpace(lyricsHtmlOrText))
            yield break;

        var text = lyricsHtmlOrText
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", "");
        text = WebUtility.HtmlDecode(text);

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (!string.IsNullOrWhiteSpace(line))
                yield return line;
        }
    }

    public static string SanitizeStoredLyrics(string lyrics)
    {
        if (string.IsNullOrEmpty(lyrics))
            return lyrics;

        lyrics = lyrics.Replace('\uFFFD', '\'');
        while (lyrics.StartsWith("TAGS>", StringComparison.OrdinalIgnoreCase))
            lyrics = lyrics[5..];

        var preMatch = Regex.Match(
            lyrics.Trim(),
            @"^<pre[^>]*>(.*)</pre\s*>$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (preMatch.Success)
            lyrics = preMatch.Groups[1].Value;

        lyrics = Regex.Replace(lyrics, @"<br\s*/?>", "<br>", RegexOptions.IgnoreCase);
        // Strip any leftover pre wrappers so the reader font is not forced to monospace
        lyrics = Regex.Replace(lyrics, @"</?pre\b[^>]*>", "", RegexOptions.IgnoreCase);
        return lyrics;
    }

    public static string DecodeLyricsBytes(byte[] bytes)
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
}
