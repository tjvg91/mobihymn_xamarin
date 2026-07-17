using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;

using HtmlAgilityPack;
using MobiHymn4.Models;
using MobiHymn4.Utils;

using Microsoft.Maui.Controls;


namespace MobiHymn4.ViewModels
{
	public class SearchViewModel : MvvmHelpers.BaseViewModel
    {
        private Globals globalInstance = Globals.Instance;
        string lastSearchText = string.Empty;
        bool searchRunning;

        private bool isSearched;
        public bool IsSearched
        {
            get => isSearched;
            set
            {
                isSearched = value;
                SetProperty(ref isSearched, value, nameof(IsSearched));
                OnPropertyChanged();
            }
        }
        private ObservableCollection<ShortHymn> items;
        public ObservableCollection<ShortHymn> Items
        {
            get => items;
            set
            {
                items = value;
                SetProperty(ref items, value, nameof(Items));
                OnPropertyChanged();
            }
        }

        private ObservableCollection<SearchResultGroup> groups = new();
        public ObservableCollection<SearchResultGroup> Groups
        {
            get => groups;
            private set => SetProperty(ref groups, value);
        }

        private int itemCount;
        public int ItemCount
        {
            get => itemCount;
            private set
            {
                itemCount = value;
                ItemCountString = $"{(value == 0 ? "No" : value.ToString())} result{(value > 1 ? "s" : "")} found";
                SetProperty(ref itemCount, value, nameof(ItemCount));
                OnPropertyChanged();
            }
        }

        private string itemCountString;
        public string ItemCountString
        {
            get => itemCountString;
            private set
            {
                itemCountString = value;
                SetProperty(ref itemCountString, value, nameof(ItemCountString));
                OnPropertyChanged();
            }
        }

        private SearchType selectedSearchType = SearchType.Lyrics;
        public SearchType SelectedSearchType
        {
            get => selectedSearchType;
            set
            {
                if (!SetProperty(ref selectedSearchType, value))
                    return;

                OnPropertyChanged(nameof(SearchPlaceholder));
                OnPropertyChanged(nameof(IsLyricsSelected));
                OnPropertyChanged(nameof(IsAuthorComposerSelected));
                OnPropertyChanged(nameof(IsMetreSelected));
                OnPropertyChanged(nameof(IsKeySelected));
                OnPropertyChanged(nameof(IsVerseSelected));
                OnPropertyChanged(nameof(IsGroupedSearchType));
                OnPropertyChanged(nameof(IsFlatSearchType));

                if (!string.IsNullOrWhiteSpace(lastSearchText) && !IsBusy)
                    SearchHymns.Execute(lastSearchText);
            }
        }

        public bool IsLyricsSelected => SelectedSearchType == SearchType.Lyrics;
        public bool IsAuthorComposerSelected => SelectedSearchType == SearchType.AuthorComposer;
        public bool IsMetreSelected => SelectedSearchType == SearchType.Metre;
        public bool IsKeySelected => SelectedSearchType == SearchType.Key;
        public bool IsVerseSelected => SelectedSearchType == SearchType.Verse;
        public bool IsGroupedSearchType =>
            SelectedSearchType is SearchType.AuthorComposer or SearchType.Metre or SearchType.Key;
        public bool IsFlatSearchType => !IsGroupedSearchType;

        public string SearchPlaceholder => SelectedSearchType switch
        {
            SearchType.AuthorComposer => "e.g. Charles Wesley",
            SearchType.Metre => "e.g. 8.7.8.7",
            SearchType.Key => "e.g. E flat, Eb, or C#",
            SearchType.Verse => "e.g. Genesis 1 or Gen 1",
            _ => "e.g. amazing grace"
        };

        public event EventHandler OnSearchStarted;
        public event EventHandler OnSearchFinished;

        public SearchViewModel()
        {
            Items = new ObservableCollection<ShortHymn>();
            IsSearched = false;

            Title = "Search";
        }

        private ICommand _searchHymns;
        public ICommand SearchHymns
        {
            get
            {
                return _searchHymns ??= new Microsoft.Maui.Controls.Command<string>(
                    async text => await RunSearchAsync(text));
            }
        }

        async Task RunSearchAsync(string text)
        {
            if (searchRunning)
                return;

            searchRunning = true;
            lastSearchText = text ?? string.Empty;
            var searchType = SelectedSearchType;

            try
            {
                IsBusy = true;
                IsSearched = false;
                Items = new ObservableCollection<ShortHymn>();
                Groups = new ObservableCollection<SearchResultGroup>();
                OnSearchFinished?.Invoke(null, EventArgs.Empty);

                if (!string.IsNullOrWhiteSpace(text))
                {
                    globalInstance.SearchList.Add(text);
                    if (globalInstance.SearchList.Count > 10)
                        globalInstance.SearchList = globalInstance.SearchList.Skip(1).ToObservableRangeCollection();
                }

                // Let MAUI render the busy state before starting CPU work.
                await Task.Yield();
                await Task.Delay(32);

                var result = await Task.Run(() => Search(text, searchType));

                Items = result.Items;
                Groups = result.Groups;
                ItemCount = result.IsGrouped
                    ? Groups.Sum(group => group.Hymns.Count)
                    : Items.Count;
                IsSearched = true;
                OnSearchFinished?.Invoke(Items, EventArgs.Empty);
            }
            finally
            {
                IsBusy = false;
                searchRunning = false;
            }
        }

        private ICommand selectSearchTypeCommand;
        public ICommand SelectSearchTypeCommand =>
            selectSearchTypeCommand ??= new Command<SearchType>(type => SelectedSearchType = type);

        private ICommand toggleGroupCommand;
        public ICommand ToggleGroupCommand =>
            toggleGroupCommand ??= new Command<SearchResultGroup>(group =>
            {
                if (group != null)
                    group.IsExpanded = !group.IsExpanded;
            });

        private ICommand searchItemSelected;
        public ICommand SearchItemSelected
        {
            get
            {
                return searchItemSelected ?? (searchItemSelected = new Microsoft.Maui.Controls.Command<ShortHymn>(async (shortHymn) =>
                {
                    globalInstance.ActiveHymn = (from x in globalInstance.HymnList
                                                where x.Number == shortHymn.Number
                                                select x).First();

                    await Shell.Current.GoToAsync($"//{Routes.READ}");
                }));
            }
        }

        private SearchWorkResult Search(string text, SearchType searchType)
        {
            var query = (text ?? string.Empty).Trim().StripPunctuation();
            if (string.IsNullOrWhiteSpace(query))
                return SearchWorkResult.Empty(IsGrouped(searchType));

            Regex pattern;
            try
            {
                pattern = new Regex(Regex.Escape(query), RegexOptions.IgnoreCase);
            }
            catch (Exception)
            {
                return SearchWorkResult.Empty(IsGrouped(searchType));
            }

            try
            {
                var htmlDocument = new HtmlDocument();
                var results = new List<ShortHymn>();
                var metadataHits = new List<MetadataSearchHit>();

                foreach (var hymn in globalInstance.HymnList ?? new HymnList())
                {
                    if (hymn == null || string.IsNullOrEmpty(hymn.Number))
                        continue;

                    switch (searchType)
                    {
                        case SearchType.AuthorComposer:
                            AddGroupedMetadataMatch(metadataHits, pattern, hymn, "Author", hymn.Author);
                            AddGroupedMetadataMatch(metadataHits, pattern, hymn, "Composer", hymn.TuneComposer);
                            break;
                        case SearchType.Metre:
                            AddGroupedMetadataMatch(metadataHits, pattern, hymn, "Metre", hymn.Metre);
                            break;
                        case SearchType.Key:
                            AddGroupedKeyMatch(metadataHits, text, hymn);
                            break;
                        case SearchType.Verse:
                            AddVerseMatch(results, text, hymn);
                            break;
                        default:
                            var lyricsHtml = ParsedLyrics(htmlDocument, hymn.Lyrics);
                            foreach (var line in new Regex("<br>").Split(lyricsHtml ?? string.Empty))
                            {
                                if (string.IsNullOrWhiteSpace(line))
                                    continue;

                                if (!pattern.IsMatch(line.StripPunctuation()))
                                    continue;

                                results.Add(new ShortHymn
                                {
                                    Number = hymn.Number,
                                    Line = line.Trim()
                                });
                            }
                            break;
                    }
                }

                var orderedResults = searchType == SearchType.Verse
                    ? results
                        .OrderBy(r => VerseSortKey(r.Line))
                        .ThenBy(r => r.Line, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(r => HymnSortNumber(r.Number))
                        .ThenBy(r => r.Number)
                    : results
                        .OrderBy(r => r.Line.StripPunctuation())
                        .ThenBy(r => r.Number);

                var flatResults = orderedResults
                    .GroupBy(r => (Line: r.Line.StripPunctuation(), r.Number))
                    .Select(g => g.First())
                    .ToList()
                    .ToObservableCollection();

                var groupedResults = metadataHits
                    .GroupBy(
                        hit => $"{hit.Category}\0{hit.GroupValue}",
                        StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group.First().GroupValue, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(group => group.First().Category, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new SearchResultGroup
                    {
                        GroupValue = group.First().GroupValue,
                        Category = group.First().Category,
                        Hymns = group
                            .GroupBy(hit => hit.Hymn.Number, StringComparer.OrdinalIgnoreCase)
                            .Select(hymnGroup => hymnGroup.First().Hymn)
                            .OrderBy(hymn => HymnSortNumber(hymn.Number))
                            .ThenBy(hymn => hymn.Number)
                            .Select(hymn => new ShortHymn
                            {
                                Number = hymn.Number,
                                Line = string.IsNullOrWhiteSpace(hymn.FirstLine)
                                    ? hymn.Name ?? "No first line available"
                                    : hymn.FirstLine
                            })
                            .ToObservableCollection()
                    })
                    .ToObservableCollection();

                return new SearchWorkResult
                {
                    Items = flatResults,
                    Groups = groupedResults,
                    IsGrouped = IsGrouped(searchType)
                };
            }
            catch (Exception)
            {
                return SearchWorkResult.Empty(IsGrouped(searchType));
            }
        }

        static bool IsGrouped(SearchType searchType) =>
            searchType is SearchType.AuthorComposer or SearchType.Metre or SearchType.Key;

        static int HymnSortNumber(string number)
        {
            var match = Regex.Match(number ?? string.Empty, @"^\d+");
            return match.Success && int.TryParse(match.Value, out var value)
                ? value
                : int.MaxValue;
        }

        static void AddGroupedMetadataMatch(
            List<MetadataSearchHit> results,
            Regex pattern,
            Hymn hymn,
            string label,
            string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !pattern.IsMatch(value.StripPunctuation()))
                return;

            results.Add(new MetadataSearchHit
            {
                Category = label,
                GroupValue = value.Trim(),
                Hymn = hymn
            });
        }

        static void AddGroupedKeyMatch(
            List<MetadataSearchHit> results,
            string query,
            Hymn hymn)
        {
            if (string.IsNullOrWhiteSpace(hymn.TuneKey))
                return;

            bool matches;
            if (TryCanonicalizeMusicKey(query, out var canonicalQuery))
            {
                matches = TryCanonicalizeMusicKey(hymn.TuneKey, out var canonicalKey)
                    && canonicalKey == canonicalQuery;
            }
            else
            {
                var normalizedQuery = NormalizeMusicKeyText(query);
                var normalizedKey = NormalizeMusicKeyText(hymn.TuneKey);
                matches = !string.IsNullOrWhiteSpace(normalizedQuery)
                    && normalizedKey.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            if (!matches)
                return;

            results.Add(new MetadataSearchHit
            {
                Category = "Key",
                GroupValue = hymn.TuneKey.Trim(),
                Hymn = hymn
            });
        }

        static bool TryCanonicalizeMusicKey(string value, out string canonical)
        {
            canonical = null;
            var normalized = NormalizeMusicKeyText(value);
            if (normalized.Length == 0)
                return false;

            var keyMatch = Regex.Match(
                normalized,
                @"^(?<root>[a-g])(?<accidental>flat|sharp)?(?<mode>m|minor|maj|major)?$",
                RegexOptions.IgnoreCase);
            if (!keyMatch.Success)
                return false;

            var rootPitch = keyMatch.Groups["root"].Value.ToLowerInvariant() switch
            {
                "c" => 0,
                "d" => 2,
                "e" => 4,
                "f" => 5,
                "g" => 7,
                "a" => 9,
                "b" => 11,
                _ => 0
            };

            var accidental = keyMatch.Groups["accidental"].Value;
            if (accidental.Equals("sharp", StringComparison.OrdinalIgnoreCase))
                rootPitch++;
            else if (accidental.Equals("flat", StringComparison.OrdinalIgnoreCase))
                rootPitch--;

            rootPitch = (rootPitch + 12) % 12;

            var mode = keyMatch.Groups["mode"].Value;
            var canonicalMode = mode.Equals("m", StringComparison.OrdinalIgnoreCase)
                || mode.Equals("minor", StringComparison.OrdinalIgnoreCase)
                    ? "minor"
                    : "major";

            canonical = $"{rootPitch}:{canonicalMode}";
            return true;
        }

        static string NormalizeMusicKeyText(string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0)
                return normalized;

            normalized = normalized
                .Replace("♭", " flat ", StringComparison.Ordinal)
                .Replace("♯", " sharp ", StringComparison.Ordinal)
                .Replace("#", " sharp ", StringComparison.Ordinal);

            // Convert compact spellings such as Eb and F# to the same form as
            // “E flat” and “F sharp”.
            normalized = Regex.Replace(
                normalized,
                @"^([A-Ga-g])\s*[bB](?=\s*(?:m|minor|major|maj)?\s*$)",
                "$1 flat ",
                RegexOptions.IgnoreCase);

            normalized = Regex.Replace(normalized, @"\s+", string.Empty);
            normalized = new string(normalized
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
            return normalized;
        }

        static void AddMetadataMatch(
            List<ShortHymn> results,
            Regex pattern,
            Hymn hymn,
            string label,
            string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            if (!pattern.IsMatch(value.StripPunctuation()))
                return;

            results.Add(new ShortHymn
            {
                Number = hymn.Number,
                Line = $"{label}: {value.Trim()}"
            });
        }

        static void AddVerseMatch(List<ShortHymn> results, string query, Hymn hymn)
        {
            var normalizedQuery = NormalizeVerseReference(query).StripPunctuation();
            if (string.IsNullOrWhiteSpace(normalizedQuery))
                return;

            foreach (var verse in hymn.GetVerseReferences())
            {
                var normalizedReference = NormalizeVerseReference(verse).StripPunctuation();
                if (normalizedReference.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                results.Add(new ShortHymn
                {
                    Number = hymn.Number,
                    Line = verse.Trim()
                });
            }
        }

        static string NormalizeVerseReference(string value)
        {
            var input = Regex.Replace((value ?? string.Empty).Trim(), @"\s+", " ");
            if (input.Length == 0)
                return input;

            foreach (var book in BibleBooks)
            {
                foreach (var alias in book.Aliases.OrderByDescending(alias => alias.Length))
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

        static (int Book, int Chapter, int Verse) VerseSortKey(string reference)
        {
            var normalized = NormalizeVerseReference(reference);

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

        static string ParsedLyrics(HtmlDocument htmlDocument, string lyrics)
        {
            if (string.IsNullOrEmpty(lyrics))
                return string.Empty;

            htmlDocument.LoadHtml(lyrics);
            var preText = htmlDocument.DocumentNode.Descendants("pre").SingleOrDefault();
            return preText != null ? preText.InnerHtml : lyrics;
        }

        sealed class MetadataSearchHit
        {
            public string Category { get; set; }
            public string GroupValue { get; set; }
            public Hymn Hymn { get; set; }
        }

        sealed class SearchWorkResult
        {
            public ObservableCollection<ShortHymn> Items { get; set; } = new();
            public ObservableCollection<SearchResultGroup> Groups { get; set; } = new();
            public bool IsGrouped { get; set; }

            public static SearchWorkResult Empty(bool isGrouped) => new()
            {
                IsGrouped = isGrouped
            };
        }
    }
}
