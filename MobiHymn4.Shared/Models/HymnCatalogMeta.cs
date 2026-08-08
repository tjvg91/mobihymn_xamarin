namespace MobiHymn4.Shared.Models;

public sealed class HymnCatalogMeta
{
    public int Total { get; set; }
    public string? CatalogHash { get; set; }
    public List<HymnCatalogEntry> Hymns { get; set; } = new();
}

public sealed class HymnCatalogEntry
{
    public string Number { get; set; } = "";
    public string? Title { get; set; }
    public string? FirstLine { get; set; }
}
