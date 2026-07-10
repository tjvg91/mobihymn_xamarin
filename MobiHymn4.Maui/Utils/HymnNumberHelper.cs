using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MobiHymn4.Models;

namespace MobiHymn4.Utils;

public static class HymnNumberHelper
{
    static readonly Regex SanitizeRegex = new(@"^(\d*)([fst]?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex BrSplitRegex = new("<br\\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex TagStripRegex = new("<[^>]+>", RegexOptions.Compiled);
    static readonly Regex PreContentRegex = new(@"<pre[^>]*>(.*?)</pre>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    const int MinTextQueryLength = 2;

    /// <summary>Leading hymn number prefix (digits + optional f/s/t suffix) without mutating display text.</summary>
    public static string ExtractNumberPrefix(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var match = SanitizeRegex.Match(text.Trim());
        if (!match.Success)
            return string.Empty;

        var digits = match.Groups[1].Value;
        if (string.IsNullOrEmpty(digits))
            return string.Empty;

        return (digits + match.Groups[2].Value).ToLowerInvariant();
    }

    public static bool IsNumberQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        return char.IsDigit(query.Trim()[0]);
    }

    public static Hymn FindExact(string query, HymnList hymns) =>
        hymns?.FirstOrDefault(h => h?.Number != null &&
            string.Equals(h.Number, query?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static Hymn ResolveHymn(string query, HymnList hymns)
    {
        if (hymns == null || hymns.Count == 0 || string.IsNullOrWhiteSpace(query))
            return null;

        var trimmed = query.Trim();
        if (IsNumberQuery(trimmed))
        {
            var numberPrefix = ExtractNumberPrefix(trimmed);
            if (!string.IsNullOrEmpty(numberPrefix))
            {
                var exact = FindExact(numberPrefix, hymns);
                if (exact != null)
                    return exact;
            }
        }

        var suggestions = GetSuggestions(trimmed, hymns, 2);
        if (suggestions.Count != 1)
            return null;

        return FindExact(suggestions[0].Number, hymns);
    }

    public static IReadOnlyList<HymnSuggestion> GetSuggestions(string query, HymnList hymns, int maxCount = 8)
    {
        if (hymns == null || hymns.Count == 0 || string.IsNullOrWhiteSpace(query))
            return Array.Empty<HymnSuggestion>();

        var trimmed = query.Trim();
        if (IsNumberQuery(trimmed))
        {
            var numberPrefix = ExtractNumberPrefix(trimmed);
            if (!string.IsNullOrEmpty(numberPrefix))
            {
                return hymns
                    .Where(h => !string.IsNullOrEmpty(h?.Number) &&
                        h.Number.StartsWith(numberPrefix, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(h => HymnNumberSortKey(h.Number))
                    .Select(h => new HymnSuggestion
                    {
                        Number = h.Number,
                        Line = h.FirstLine ?? h.Title ?? string.Empty,
                        LineFirst = false,
                    })
                    .Take(maxCount)
                    .ToList();
            }
        }

        return GetTextSuggestions(trimmed, hymns, maxCount);
    }

    static IReadOnlyList<HymnSuggestion> GetTextSuggestions(string query, HymnList hymns, int maxCount)
    {
        var stripped = NormalizeTextQuery(query).StripPunctuation();
        if (stripped.Length < MinTextQueryLength)
            return Array.Empty<HymnSuggestion>();

        Regex regex;
        try
        {
            regex = new Regex(Regex.Escape(stripped), RegexOptions.IgnoreCase);
        }
        catch (ArgumentException)
        {
            return Array.Empty<HymnSuggestion>();
        }

        return hymns
            .SelectMany(h => GetMatchingLines(h, regex))
            .OrderBy(s => s.Line.StripPunctuation(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => HymnNumberSortKey(s.Number))
            .GroupBy(s => (Line: s.Line.StripPunctuation(), s.Number))
            .Select(g => g.First())
            .Take(maxCount)
            .ToList();
    }

    static string NormalizeTextQuery(string query) =>
        WhitespaceRegex.Replace(query?.Trim() ?? string.Empty, " ");

    static IEnumerable<HymnSuggestion> GetMatchingLines(Hymn hymn, Regex regex)
    {
        if (hymn == null)
            yield break;

        foreach (var line in GetSearchableLines(hymn))
        {
            if (!LineMatches(line, regex))
                continue;

            yield return new HymnSuggestion
            {
                Number = hymn.Number,
                Line = CleanDisplayLine(line),
                LineFirst = true,
            };
        }
    }

    static IEnumerable<string> GetSearchableLines(Hymn hymn)
    {
        if (!string.IsNullOrWhiteSpace(hymn.Title))
            yield return hymn.Title;

        if (!string.IsNullOrWhiteSpace(hymn.FirstLine))
            yield return hymn.FirstLine;

        if (string.IsNullOrEmpty(hymn.Lyrics))
            yield break;

        foreach (var line in ExtractLyricLines(hymn.Lyrics))
            yield return line;
    }

    static IEnumerable<string> ExtractLyricLines(string lyrics)
    {
        var source = lyrics ?? string.Empty;
        var preMatch = PreContentRegex.Match(source);
        if (preMatch.Success)
            source = preMatch.Groups[1].Value;

        foreach (var part in BrSplitRegex.Split(source))
        {
            var line = TagStripRegex.Replace(part, string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(line))
                yield return line;
        }
    }

    static string CleanDisplayLine(string line) =>
        TagStripRegex.Replace(line ?? string.Empty, string.Empty).Trim();

    static bool LineMatches(string line, Regex regex)
    {
        var cleaned = CleanDisplayLine(line);
        return !string.IsNullOrWhiteSpace(cleaned) && regex.IsMatch(cleaned.StripPunctuation());
    }

    public static double HymnNumberSortKey(string number)
    {
        if (string.IsNullOrWhiteSpace(number))
            return double.MaxValue;

        var normalized = number
            .Replace("f", ".4", StringComparison.OrdinalIgnoreCase)
            .Replace("s", ".2", StringComparison.OrdinalIgnoreCase)
            .Replace("t", ".3", StringComparison.OrdinalIgnoreCase);
        return double.TryParse(normalized, out var value) ? value : double.MaxValue;
    }
}
