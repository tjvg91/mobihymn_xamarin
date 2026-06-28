using System.Text.RegularExpressions;

namespace MobiHymn4.Utils;

public static class VoiceHymnParser
{
    static readonly Regex DirectHymnNumberRegex = new(@"\b(?<number>\d+)\s*(?<suffix>[fst])?\b", RegexOptions.IgnoreCase);
    static readonly Regex SpacedDigitSequenceRegex = new(@"\b(?<digits>(?:\d\s+)+\d)\b", RegexOptions.IgnoreCase);

    static readonly Dictionary<string, int> NumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zero"] = 0,
        ["oh"] = 0,
        ["o"] = 0,
        ["one"] = 1,
        ["two"] = 2,
        ["three"] = 3,
        ["four"] = 4,
        ["five"] = 5,
        ["six"] = 6,
        ["seven"] = 7,
        ["eight"] = 8,
        ["nine"] = 9,
        ["ten"] = 10,
        ["eleven"] = 11,
        ["twelve"] = 12,
        ["thirteen"] = 13,
        ["fourteen"] = 14,
        ["fifteen"] = 15,
        ["sixteen"] = 16,
        ["seventeen"] = 17,
        ["eighteen"] = 18,
        ["nineteen"] = 19,
        ["twenty"] = 20,
        ["thirty"] = 30,
        ["forty"] = 40,
        ["fourty"] = 40,
        ["fifty"] = 50,
        ["sixty"] = 60,
        ["seventy"] = 70,
        ["eighty"] = 80,
        ["ninety"] = 90
    };

    static readonly Dictionary<string, string> TuneSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["first"] = "",
        ["1st"] = "",
        ["one"] = "",
        ["second"] = "s",
        ["2nd"] = "s",
        ["s"] = "s",
        ["ess"] = "s",
        ["third"] = "t",
        ["3rd"] = "t",
        ["t"] = "t",
        ["tee"] = "t",
        ["fourth"] = "f",
        ["4th"] = "f",
        ["f"] = "f",
        ["eff"] = "f"
    };

    static readonly HashSet<string> TuneDescriptorWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "first",
        "1st",
        "second",
        "2nd",
        "s",
        "ess",
        "third",
        "3rd",
        "t",
        "tee",
        "fourth",
        "4th",
        "f",
        "eff"
    };

    static readonly HashSet<string> IgnoredWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "hymn",
        "number",
        "no",
        "tune",
        "version",
        "variation",
        "alternate",
        "alternative",
        "the",
        "a",
        "an",
        "and",
        "please",
        "go",
        "to",
        "open",
        "play",
        "find",
        "search"
    };

    public static bool TryParseHymnNumber(string phrase, out string hymnNumber)
    {
        hymnNumber = string.Empty;

        if (string.IsNullOrWhiteSpace(phrase))
            return false;

        var normalized = Normalize(phrase);
        var suffix = DetectTuneSuffix(normalized);

        if (TryParseSpacedDigitSequence(normalized, suffix, out hymnNumber))
            return true;

        var directMatch = DirectHymnNumberRegex.Match(normalized);

        if (directMatch.Success)
        {
            var directSuffix = directMatch.Groups["suffix"].Value.ToLowerInvariant();
            hymnNumber = directMatch.Groups["number"].Value + (string.IsNullOrEmpty(suffix) ? directSuffix : suffix);
            return true;
        }

        var tokens = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !IgnoredWords.Contains(token))
            .Where(token => !TuneDescriptorWords.Contains(token))
            .ToList();

        if (!TryParseSpokenNumber(tokens, out var number))
            return false;

        hymnNumber = number + suffix;
        return true;
    }

    static string Normalize(string phrase)
    {
        var lower = phrase.ToLowerInvariant().Replace('-', ' ');
        return Regex.Replace(lower, @"[^a-z0-9\s]", " ");
    }

    static string DetectTuneSuffix(string normalized)
    {
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var i = 0; i < tokens.Length; i++)
        {
            if (!TuneSuffixes.TryGetValue(tokens[i], out var suffix))
                continue;

            var isTuneContext =
                i + 1 < tokens.Length && (tokens[i + 1] == "tune" || tokens[i + 1] == "version" || tokens[i + 1] == "variation") ||
                i > 0 && (tokens[i - 1] == "tune" || tokens[i - 1] == "version" || tokens[i - 1] == "variation");

            if (isTuneContext || tokens[i] is "s" or "t" or "f" or "ess" or "tee" or "eff")
                return suffix;
        }

        return string.Empty;
    }

    static bool TryParseSpacedDigitSequence(string normalized, string suffix, out string hymnNumber)
    {
        hymnNumber = string.Empty;

        var match = SpacedDigitSequenceRegex.Match(normalized);
        if (!match.Success)
            return false;

        hymnNumber = Regex.Replace(match.Groups["digits"].Value, @"\s+", "") + suffix;
        return hymnNumber.Length > 0;
    }

    static bool TryParseSpokenNumber(IReadOnlyList<string> tokens, out int number)
    {
        number = 0;

        var numberTokens = tokens.Where(IsNumberToken).ToList();
        if (numberTokens.Count == 0)
            return false;

        if (TryParseDigitSequence(numberTokens, out number))
            return true;

        if (TryParseHundredShorthand(numberTokens, out number))
            return true;

        var current = 0;
        foreach (var token in numberTokens)
        {
            if (token == "hundred")
            {
                current = Math.Max(current, 1) * 100;
                continue;
            }

            if (!TryGetNumberWordValue(token, out var value))
                return false;

            current += value;
        }

        number = current;
        return number > 0;
    }

    static bool IsNumberToken(string token) =>
        token == "hundred" || NumberWords.ContainsKey(token) || IsSingleDigitToken(token);

    static bool IsSingleDigitToken(string token) =>
        token.Length == 1 && char.IsDigit(token[0]);

    static bool TryGetSingleDigit(string token, out int digit)
    {
        if (NumberWords.TryGetValue(token, out digit) && digit is >= 0 and <= 9)
            return true;

        if (IsSingleDigitToken(token))
        {
            digit = token[0] - '0';
            return true;
        }

        digit = 0;
        return false;
    }

    static bool TryGetNumberWordValue(string token, out int value)
    {
        if (NumberWords.TryGetValue(token, out value))
            return true;

        if (IsSingleDigitToken(token))
        {
            value = token[0] - '0';
            return true;
        }

        value = 0;
        return false;
    }

    static bool TryParseDigitSequence(IReadOnlyList<string> tokens, out int number)
    {
        number = 0;

        if (tokens.Count < 2)
            return false;

        var digits = new List<int>();
        foreach (var token in tokens)
        {
            if (!TryGetSingleDigit(token, out var digit))
                return false;

            digits.Add(digit);
        }

        number = int.Parse(string.Concat(digits));
        return number > 0;
    }

    static bool TryParseHundredShorthand(IReadOnlyList<string> tokens, out int number)
    {
        number = 0;

        if (tokens.Count is < 2 or > 3)
            return false;

        if (!NumberWords.TryGetValue(tokens[0], out var hundreds) || hundreds is < 1 or > 9)
            return false;

        if (!NumberWords.TryGetValue(tokens[1], out var tens) || tens < 20 || tens % 10 != 0)
            return false;

        var ones = 0;
        if (tokens.Count == 3 && (!NumberWords.TryGetValue(tokens[2], out ones) || ones is < 0 or > 9))
            return false;

        number = hundreds * 100 + tens + ones;
        return true;
    }
}
