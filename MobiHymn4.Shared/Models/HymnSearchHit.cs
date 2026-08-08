namespace MobiHymn4.Shared.Models;

/// <summary>One lyrics/metadata search hit (mirrors MAUI ShortHymn Line + Number).</summary>
public sealed class HymnSearchHit
{
    public string Number { get; set; } = "";
    /// <summary>Matching lyric line, or the metadata value for grouped searches.</summary>
    public string Line { get; set; } = "";
    public string? Reason { get; set; }
}
