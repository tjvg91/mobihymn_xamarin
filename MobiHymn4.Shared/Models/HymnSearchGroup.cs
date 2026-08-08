namespace MobiHymn4.Shared.Models;

/// <summary>Grouped metadata search result (author/composer, metre, key, verse).</summary>
public sealed class HymnSearchGroup
{
    public string Title { get; set; } = "";
    /// <summary>Author, Composer, Metre, Key, or Verse.</summary>
    public string Category { get; set; } = "";
    public IReadOnlyList<HymnSearchHit> Hymns { get; set; } = Array.Empty<HymnSearchHit>();

    public string SubtitleText
    {
        get
        {
            var count = $"{Hymns.Count} hymn{(Hymns.Count == 1 ? "" : "s")}";
            // Title already is the metre/key/verse value — don't repeat the category label.
            var showCategory =
                !string.Equals(Category, "Metre", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Category, "Key", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Category, "Verse", StringComparison.OrdinalIgnoreCase);
            return showCategory ? $"{Category} · {count}" : count;
        }
    }
}
