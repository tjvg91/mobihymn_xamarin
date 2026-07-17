using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using MobiHymn4.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MobiHymn4.Views.Popups
{
    public partial class CatalogDiffPopup : Popup
    {
        const int MaxNumbersShown = 12;
        const int MaxPropPreview = 120;
        const int MaxUiSections = 200;

        public const string ResultSync = "sync";

        public CatalogDiffPopup()
        {
            InitializeComponent();
            CanBeDismissedByTappingOutsideOfPopup = true;
            Opened += CatalogDiffPopup_Opened;
        }

        void CatalogDiffPopup_Opened(object sender, EventArgs e)
        {
            var display = DeviceDisplay.MainDisplayInfo;
            Size = new Size(display.Width / display.Density, display.Height / display.Density);
        }

        public void Bind(CatalogDiff diff) =>
            BindAsync(diff).GetAwaiter().GetResult();

        public async Task BindAsync(CatalogDiff diff)
        {
            try
            {
                var palette = await MainThread.InvokeOnMainThreadAsync(() => DiffPalette.Current);
                var sections = await Task.Run(() => BuildSections(diff, palette)).ConfigureAwait(false);
                var added = diff?.AddedCount ?? 0;
                var modified = diff?.ModifiedCount ?? 0;
                var removed = diff?.RemovedCount ?? 0;

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    SetSummaryChips(added, modified, removed);
                    changesList.ItemsSource = sections;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CatalogDiffPopup.Bind failed: {ex}");
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    SetSummaryChips(0, 0, 0);
                    changesList.ItemsSource = new List<DiffSectionVm>
                    {
                        BuildInfoSection(DiffPalette.Current, "Error", Truncate(ex.Message, 200))
                    };
                });
            }
        }

        readonly struct DiffPalette
        {
            public Color Added { get; init; }
            public Color Removed { get; init; }
            public Color Modified { get; init; }
            public Color Info { get; init; }

            public static DiffPalette Current => new()
            {
                Added = (Color)Application.Current.Resources["Green"],
                Removed = (Color)Application.Current.Resources["Red"],
                Modified = (Color)Application.Current.Resources["Orange"],
                Info = Colors.Gray
            };
        }

        // The list is virtualized (CollectionView), so realized rows stay bounded even
        // when a catalog-wide change (e.g. every hymn re-tagged) produces hundreds of groups.
        static List<DiffSectionVm> BuildSections(CatalogDiff diff, DiffPalette palette)
        {
            var sections = new List<DiffSectionVm>();
            if (diff == null || diff.ChangeCount == 0)
                return sections;

            if (diff.AddedCount > 0)
            {
                sections.Add(BuildNumberSection(
                    "Added",
                    "Will be downloaded",
                    diff.AddedCount,
                    palette.Added,
                    diff.Added?.Numbers,
                    diff.AddedCount,
                    "New on the server — these hymns will be downloaded when you sync."));
            }

            if (diff.RemovedCount > 0)
            {
                sections.Add(BuildNumberSection(
                    "Removed",
                    "Will be deleted locally",
                    diff.RemovedCount,
                    palette.Removed,
                    diff.Removed?.Numbers,
                    diff.RemovedCount,
                    "No longer on the server — these hymns will be removed from this device when you sync."));
            }

            if (diff.ModifiedGroups != null && diff.ModifiedGroups.Count > 0)
            {
                var shown = 0;
                foreach (var group in diff.ModifiedGroups)
                {
                    if (shown >= MaxUiSections)
                    {
                        sections.Add(BuildInfoSection(
                            palette,
                            "More changes…",
                            $"{diff.ModifiedGroups.Count - shown} additional group(s) not shown. Sync to apply all."));
                        break;
                    }

                    var section = BuildModifiedGroupSection(palette, group);
                    if (section == null)
                        continue;

                    sections.Add(section);
                    shown++;
                }
            }
            else if (diff.Modified != null && diff.Modified.Count > 0)
            {
                // Legacy flat list: collapse by changed prop names so we never create 800+ rows.
                var shown = 0;
                foreach (var group in CollapseLegacyModified(diff.Modified))
                {
                    if (shown >= MaxUiSections)
                    {
                        sections.Add(BuildInfoSection(palette, "More changes…", "Additional modifications not shown. Sync to apply all."));
                        break;
                    }

                    var section = BuildModifiedGroupSection(palette, group);
                    if (section == null)
                        continue;

                    sections.Add(section);
                    shown++;
                }
            }

            return sections;
        }

        void SetSummaryChips(int added, int modified, int removed)
        {
            chipAdded.IsVisible = added > 0;
            lblChipAdded.Text = $"+ {added} added";

            chipModified.IsVisible = modified > 0;
            lblChipModified.Text = $"~ {modified} modified";

            chipRemoved.IsVisible = removed > 0;
            lblChipRemoved.Text = $"− {removed} removed";
        }

        static DiffSectionVm BuildNumberSection(
            string kind, string title, int count, Color accent, IList<string> numbers, int totalCount, string detail)
        {
            var numberSample = FormatNumberSample(numbers, totalCount);
            return new DiffSectionVm
            {
                KindLabel = kind,
                Title = title,
                CountBadge = count.ToString(),
                AccentColor = accent,
                Detail = string.IsNullOrEmpty(numberSample) ? detail : $"{detail}\n\nHymns: {numberSample}"
            };
        }

        static DiffSectionVm BuildModifiedGroupSection(DiffPalette palette, CatalogModifiedGroup group)
        {
            var propsLabel = group.ChangedProps != null && group.ChangedProps.Count > 0
                ? string.Join(" · ", group.ChangedProps.Select(HumanizePropName))
                : "Properties";

            var vm = new DiffSectionVm
            {
                KindLabel = "Modified",
                Title = propsLabel,
                CountBadge = group.Count.ToString(),
                AccentColor = palette.Modified,
                Detail = $"Affects {FormatNumberSample(group.Numbers, group.Count)}"
            };

            if (group.ValueVaries)
            {
                vm.Note = "Values differ per hymn — showing a few examples:";
                foreach (var example in (group.Examples ?? new List<CatalogModifiedExample>()).Take(3))
                {
                    var built = BuildExample($"Hymn #{example.Number}", example.Props);
                    if (built.Props.Count > 0)
                        vm.Examples.Add(built);
                }
            }
            else if (group.Props != null && group.Props.Count > 0)
            {
                var built = BuildExample(null, group.Props);
                if (built.Props.Count > 0)
                    vm.Examples.Add(built);
            }

            // Encoding-only diffs (e.g. &#34; vs ") decode to the same text — drop the section.
            // Real / substantial text differences still produce Props and are shown.
            if (vm.Examples.Count == 0 && (group.Props != null || group.ValueVaries))
                return null;

            return vm;
        }

        static DiffExampleVm BuildExample(string header, Dictionary<string, CatalogPropertyChange> props)
        {
            var vm = new DiffExampleVm { Header = header };
            if (props == null)
                return vm;

            var previews = new List<string>();
            var orderedProps = props.OrderBy(item => item.Key).ToList();
            foreach (var property in orderedProps)
            {
                var oldToken = property.Value?.Old;
                var newToken = property.Value?.New;
                var oldText = FormatValue(oldToken);
                var newText = FormatValue(newToken);
                // Only hide when decoded display text is identical (no substantial difference).
                if (string.Equals(oldText, newText, StringComparison.Ordinal))
                    continue;

                var oldEmpty = IsEmptyToken(oldToken) || oldText == "(empty)";
                var newEmpty = IsEmptyToken(newToken) || newText == "(empty)";

                vm.Props.Add(new DiffPropVm
                {
                    Name = HumanizePropName(property.Key),
                    ShowName = true,
                    OldText = oldText,
                    NewText = newText,
                    IsNew = oldEmpty && !newEmpty,
                    IsRemoved = newEmpty && !oldEmpty
                });

                if (!newEmpty)
                    previews.Add(newText);
                else if (!oldEmpty)
                    previews.Add(oldText);
            }

            vm.Preview = previews.Count > 0
                ? Truncate(string.Join(" · ", previews), 80)
                : string.Join(" · ", vm.Props.Select(p => p.Name));

            return vm;
        }

        static DiffSectionVm BuildInfoSection(DiffPalette palette, string title, string detail) => new()
        {
            KindLabel = "Note",
            Title = title,
            AccentColor = palette.Info,
            Detail = detail
        };

        static string HumanizePropName(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return "Property";

            return key.Trim().ToLowerInvariant() switch
            {
                "title" or "name" => "Title",
                "firstline" => "First line",
                "lyrics" => "Lyrics",
                "author" => "Author",
                "metre" or "meter" => "Metre",
                "tune" => "Tune",
                "tunecomposer" => "Tune composer",
                "tunekey" => "Tune key",
                "midifilename" => "MIDI file",
                "verseref" or "verses" => "Verses",
                "tags" => "Tags",
                "year" => "Year",
                "remark" => "Remark",
                "number" => "Number",
                _ => SplitCamelCase(key)
            };
        }

        static string SplitCamelCase(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var sb = new System.Text.StringBuilder();
            sb.Append(char.ToUpperInvariant(text[0]));
            for (var i = 1; i < text.Length; i++)
            {
                if (char.IsUpper(text[i]) && !char.IsUpper(text[i - 1]))
                    sb.Append(' ');
                sb.Append(text[i]);
            }
            return sb.ToString();
        }

        static IEnumerable<CatalogModifiedGroup> CollapseLegacyModified(IEnumerable<CatalogModifiedHymn> items)
        {
            return items
                .Where(item => item != null)
                .GroupBy(item =>
                {
                    var keys = (item.Props?.Keys ?? Enumerable.Empty<string>())
                        .OrderBy(k => k, StringComparer.OrdinalIgnoreCase);
                    return string.Join("|", keys);
                }, StringComparer.Ordinal)
                .Select(g =>
                {
                    var list = g.ToList();
                    var first = list[0];
                    var sameValues = list.All(item =>
                        JsonConvert.SerializeObject(item.Props) == JsonConvert.SerializeObject(first.Props));
                    return new CatalogModifiedGroup
                    {
                        Count = list.Count,
                        Numbers = list.Select(item => item.Number).Where(n => !string.IsNullOrWhiteSpace(n)).ToList(),
                        NumbersComplete = true,
                        ChangedProps = (first.Props?.Keys ?? Enumerable.Empty<string>())
                            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                            .ToList(),
                        ValueVaries = !sameValues,
                        Props = sameValues ? first.Props : null,
                        Examples = sameValues
                            ? new List<CatalogModifiedExample>()
                            : list.Take(3).Select(item => new CatalogModifiedExample
                            {
                                Number = item.Number,
                                Props = item.Props
                            }).ToList()
                    };
                })
                .OrderByDescending(g => g.Count);
        }

        static string FormatNumberSample(IList<string> numbers, int count)
        {
            if (numbers == null || numbers.Count == 0)
                return count > 0 ? $"{count} hymn{(count == 1 ? string.Empty : "s")}" : string.Empty;

            var shown = numbers.Take(MaxNumbersShown).ToList();
            var text = "#" + string.Join(", #", shown);
            if (count > shown.Count)
                text += $" …and {count - shown.Count} more";
            return text;
        }

        static bool IsEmptyToken(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null)
                return true;

            if (value.Type == JTokenType.String)
                return string.IsNullOrWhiteSpace(value.Value<string>());

            if (value.Type == JTokenType.Array)
                return !value.HasValues;

            if (value.Type == JTokenType.Object)
            {
                var preview = value["preview"]?.Value<string>();
                if (preview != null)
                    return string.IsNullOrWhiteSpace(preview);
                return !value.HasValues;
            }

            return false;
        }

        static string FormatValue(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null)
                return "(empty)";

            // Server may send { preview, length, truncated } for long strings.
            if (value.Type == JTokenType.Object)
            {
                var preview = value["preview"]?.Value<string>();
                if (!string.IsNullOrEmpty(preview))
                    return Truncate(preview, MaxPropPreview);
            }

            if (value.Type == JTokenType.String)
                return Truncate(value.Value<string>() ?? string.Empty, MaxPropPreview);

            if (value.Type == JTokenType.Array)
            {
                var items = value.Children().Select(t => t.Type == JTokenType.String
                    ? t.Value<string>()
                    : t.ToString(Formatting.None)).Take(8).ToList();
                var joined = string.Join(", ", items);
                if (value.Count() > items.Count)
                    joined += "…";
                return Truncate(joined, MaxPropPreview);
            }

            return Truncate(value.ToString(Formatting.None), MaxPropPreview);
        }

        static string Truncate(string text, int max)
        {
            // Server sometimes HTML-encodes quotes/apostrophes in string values
            // (e.g. &#34;Welcome...&#34;); decode so the UI shows readable text.
            text = string.IsNullOrEmpty(text) ? string.Empty : WebUtility.HtmlDecode(text);
            if (text.Length <= max)
                return text;
            return text.Substring(0, max) + "…";
        }

        void Overlay_Tapped(object sender, TappedEventArgs e) => Close(null);

        void btnSyncChanges_Clicked(object sender, EventArgs e) => Close(ResultSync);
    }
}
