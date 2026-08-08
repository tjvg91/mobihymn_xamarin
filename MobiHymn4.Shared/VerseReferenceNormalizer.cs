using System.Text.RegularExpressions;

namespace MobiHymn4.Shared;

/// <summary>Expands bible book aliases (Gen → Genesis) for verse search — mirrors MAUI SearchViewModel.</summary>
public static class VerseReferenceNormalizer
{
    public static string Normalize(string value)
    {
        var input = Regex.Replace((value ?? string.Empty).Trim(), @"\s+", " ");
        if (input.Length == 0)
            return input;

        foreach (var book in BibleBooks)
        {
            foreach (var alias in book.Aliases.OrderByDescending(a => a.Length))
            {
                if (!input.StartsWith(alias, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (input.Length > alias.Length)
                {
                    var next = input[alias.Length];
                    if (!char.IsWhiteSpace(next) && !char.IsDigit(next) && next != ':')
                        continue;
                }

                return book.Canonical + input.Substring(alias.Length);
            }
        }

        return input;
    }

    public static (int Book, int Chapter, int Verse) SortKey(string reference)
    {
        var normalized = Normalize(reference);

        for (var bookIndex = 0; bookIndex < BibleBooks.Length; bookIndex++)
        {
            var canonical = BibleBooks[bookIndex].Canonical;
            if (!normalized.StartsWith(canonical, StringComparison.OrdinalIgnoreCase))
                continue;

            if (normalized.Length > canonical.Length
                && !char.IsWhiteSpace(normalized[canonical.Length])
                && !char.IsDigit(normalized[canonical.Length]))
                continue;

            var location = normalized.Substring(canonical.Length).TrimStart();
            var match = Regex.Match(location, @"^(?<chapter>\d+)(?:\s*:\s*(?<verse>\d+))?");
            var chapter = match.Success
                && int.TryParse(match.Groups["chapter"].Value, out var parsedChapter)
                    ? parsedChapter
                    : int.MaxValue;
            var verse = match.Success
                && int.TryParse(match.Groups["verse"].Value, out var parsedVerse)
                    ? parsedVerse
                    : 0;

            return (bookIndex, chapter, verse);
        }

        return (int.MaxValue, int.MaxValue, int.MaxValue);
    }

    static readonly (string Canonical, string[] Aliases)[] BibleBooks =
    {
        ("Genesis", new[] { "Genesis", "Gen", "Ge", "Gn" }),
        ("Exodus", new[] { "Exodus", "Exod", "Exo", "Ex" }),
        ("Leviticus", new[] { "Leviticus", "Lev", "Lv" }),
        ("Numbers", new[] { "Numbers", "Num", "Nm" }),
        ("Deuteronomy", new[] { "Deuteronomy", "Deut", "Dt" }),
        ("Joshua", new[] { "Joshua", "Josh", "Jos" }),
        ("Judges", new[] { "Judges", "Judg", "Jdg" }),
        ("Ruth", new[] { "Ruth", "Ru" }),
        ("1 Samuel", new[] { "1 Samuel", "1 Sam", "1Sa", "I Samuel" }),
        ("2 Samuel", new[] { "2 Samuel", "2 Sam", "2Sa", "II Samuel" }),
        ("1 Kings", new[] { "1 Kings", "1 Kgs", "1 Ki", "1Ki", "I Kings" }),
        ("2 Kings", new[] { "2 Kings", "2 Kgs", "2 Ki", "2Ki", "II Kings" }),
        ("1 Chronicles", new[] { "1 Chronicles", "1 Chron", "1 Chr", "1Ch", "I Chronicles" }),
        ("2 Chronicles", new[] { "2 Chronicles", "2 Chron", "2 Chr", "2Ch", "II Chronicles" }),
        ("Ezra", new[] { "Ezra", "Ezr" }),
        ("Nehemiah", new[] { "Nehemiah", "Neh" }),
        ("Esther", new[] { "Esther", "Est" }),
        ("Job", new[] { "Job" }),
        ("Psalms", new[] { "Psalms", "Psalm", "Psa", "Ps" }),
        ("Proverbs", new[] { "Proverbs", "Prov", "Pr" }),
        ("Ecclesiastes", new[] { "Ecclesiastes", "Eccles", "Eccl", "Ecc" }),
        ("Song of Solomon", new[] { "Song of Solomon", "Song of Songs", "Song", "SOS" }),
        ("Isaiah", new[] { "Isaiah", "Isa" }),
        ("Jeremiah", new[] { "Jeremiah", "Jer" }),
        ("Lamentations", new[] { "Lamentations", "Lam" }),
        ("Ezekiel", new[] { "Ezekiel", "Ezek", "Ezk" }),
        ("Daniel", new[] { "Daniel", "Dan" }),
        ("Hosea", new[] { "Hosea", "Hos" }),
        ("Joel", new[] { "Joel" }),
        ("Amos", new[] { "Amos" }),
        ("Obadiah", new[] { "Obadiah", "Obad" }),
        ("Jonah", new[] { "Jonah", "Jon" }),
        ("Micah", new[] { "Micah", "Mic" }),
        ("Nahum", new[] { "Nahum", "Nah" }),
        ("Habakkuk", new[] { "Habakkuk", "Hab" }),
        ("Zephaniah", new[] { "Zephaniah", "Zeph" }),
        ("Haggai", new[] { "Haggai", "Hag" }),
        ("Zechariah", new[] { "Zechariah", "Zech" }),
        ("Malachi", new[] { "Malachi", "Mal" }),
        ("Matthew", new[] { "Matthew", "Matt", "Mt" }),
        ("Mark", new[] { "Mark", "Mrk", "Mk" }),
        ("Luke", new[] { "Luke", "Lk" }),
        ("John", new[] { "John", "Jn" }),
        ("Acts", new[] { "Acts", "Act" }),
        ("Romans", new[] { "Romans", "Rom" }),
        ("1 Corinthians", new[] { "1 Corinthians", "1 Cor", "1Co", "I Corinthians" }),
        ("2 Corinthians", new[] { "2 Corinthians", "2 Cor", "2Co", "II Corinthians" }),
        ("Galatians", new[] { "Galatians", "Gal" }),
        ("Ephesians", new[] { "Ephesians", "Eph" }),
        ("Philippians", new[] { "Philippians", "Phil", "Php" }),
        ("Colossians", new[] { "Colossians", "Col" }),
        ("1 Thessalonians", new[] { "1 Thessalonians", "1 Thess", "1 Th", "1Th", "I Thessalonians" }),
        ("2 Thessalonians", new[] { "2 Thessalonians", "2 Thess", "2 Th", "2Th", "II Thessalonians" }),
        ("1 Timothy", new[] { "1 Timothy", "1 Tim", "1Ti", "I Timothy" }),
        ("2 Timothy", new[] { "2 Timothy", "2 Tim", "2Ti", "II Timothy" }),
        ("Titus", new[] { "Titus", "Tit" }),
        ("Philemon", new[] { "Philemon", "Phlm", "Phm" }),
        ("Hebrews", new[] { "Hebrews", "Heb" }),
        ("James", new[] { "James", "Jas" }),
        ("1 Peter", new[] { "1 Peter", "1 Pet", "1Pe", "I Peter" }),
        ("2 Peter", new[] { "2 Peter", "2 Pet", "2Pe", "II Peter" }),
        ("1 John", new[] { "1 John", "1 Jn", "1Jn", "I John" }),
        ("2 John", new[] { "2 John", "2 Jn", "2Jn", "II John" }),
        ("3 John", new[] { "3 John", "3 Jn", "3Jn", "III John" }),
        ("Jude", new[] { "Jude" }),
        ("Revelation", new[] { "Revelation", "Rev" })
    };
}
