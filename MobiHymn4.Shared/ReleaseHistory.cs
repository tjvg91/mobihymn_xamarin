namespace MobiHymn4.Shared;

/// <summary>App release notes — kept in sync with MAUI AboutViewModel.LoadRevisions().</summary>
public static class ReleaseHistory
{
    public sealed record Entry(string Version, IReadOnlyList<string> Details);

    public const string CurrentVersion = "0.9.2";

    public static IReadOnlyList<Entry> Entries { get; } =
    [
        new("0.9.2",
        [
            "MIDI files modification",
            "MIDI panel UI improvements",
            "MIDI soundfonts",
        ]),
        new("0.9.0",
        [
            "Optimize downloading and syncing",
            "More details on hymns",
            "AI search and agent",
            "Signing in and worship groups",
        ]),
        new("0.8.4",
        [
            "Voice command",
            "Bug fixes",
            "Letter spacing & line spacing options",
            "Shareable deep links for hymns",
        ]),
        new("0.8.3",
        [
            "MP3 player for some hymns",
            "Downloading and syncing hymns in the background",
            "Bookmark groups",
            "Clipboard icon for Android",
        ]),
        new("0.8.2",
        [
            "Disable selection of lyrics for Android temporarily",
            "New UI",
            "Disable MIDI playing temporarily",
            "Feature to sync updates from cloud",
            "New themes and fonts",
        ]),
        new("0.8.0",
        [
            "Slider intro",
            "Splash Screen",
            "Can play MIDI",
        ]),
        new("0.7.6",
        [
            "Bug fixes",
            "New fonts",
        ]),
        new("0.7.4",
        [
            "New app icon",
            "Initial MIDI player",
        ]),
    ];
}
