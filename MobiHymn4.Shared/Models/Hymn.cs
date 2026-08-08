namespace MobiHymn4.Shared.Models;

public class Hymn
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Name { get; set; }
    public string Lyrics { get; set; } = "";
    public string FirstLine { get; set; } = "";
    public string? MidiFileName { get; set; }
    public string? Author { get; set; }
    public string? Metre { get; set; }
    public string? Tune { get; set; }
    public string? TuneComposer { get; set; }
    public string? TuneKey { get; set; }
    public string[] Verses { get; set; } = Array.Empty<string>();
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string? VerseRef { get; set; }
    public string? Year { get; set; }
    public string? Remark { get; set; }

    public IEnumerable<string> GetVerseReferences()
    {
        if (Verses?.Length > 0)
            return Verses.Where(v => !string.IsNullOrWhiteSpace(v));

        return string.IsNullOrWhiteSpace(VerseRef)
            ? Enumerable.Empty<string>()
            : new[] { VerseRef };
    }
}
