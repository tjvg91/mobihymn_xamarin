namespace MobiHymn4.Shared;

public static class PrefKeys
{
    public const string LastHymnNumber = "lastHymnNumber";
    public const string HymnInputType = "hymnInputType";
    /// <summary>First-run intro slider (MAUI PreferencesVar.IS_NEW).</summary>
    public const string IsNew = "isNew";
    /// <summary>Dismissed community sign-up prompt (MAUI COMMUNITY_PROMPT_DISMISSED).</summary>
    public const string CommunityPromptDismissed = "communityPromptDismissedV2";
    public const string DarkMode = "darkMode";
    public const string ActiveFont = "activeFont";
    public const string ActiveFontSize = "activeFontSize";
    public const string ActiveTheme = "activeReadTheme";
    public const string ActiveAlignment = "activeAlignment";
    public const string LetterSpacing = "activeLetterSpacing";
    public const string LineSpacing = "activeLineSpacing";
    public const string AgentMode = "agentMode";
    public const string AgentChatLimit = "agentChatLimit";
    /// <summary>Device-local Selah chat transcript + session id (not synced / not wiped on sign-out).</summary>
    public const string SelahSessionJson = "selahSessionJson";
    public const string CloudOwnerUid = "cloudSettingsOwnerUid";
    public const string CloudPending = "cloudSettingsPending";
    public const string CloudUpdatedAt = "cloudSettingsUpdatedAt";
    /// <summary>
    /// Set just before email sign-up so the first sync uploads pre-existing local
    /// settings into the new account instead of wiping them as an account switch.
    /// </summary>
    public const string SeedLocalSettingsOnNextSync = "cloudSeedLocalSettingsOnNextSync";
    public const string BookmarksJson = "bookmarksJson";
    public const string HistoryJson = "historyJson";
    public const string SettingsJson = "settingsJson";
    public const string GroupMutePrefix = "groupNotificationsMuted_";
    public const string FcmDeviceId = "fcm_device_id";
    /// <summary>Persisted board setlist prev/next context (MAUI boardNavState).</summary>
    public const string BoardNavState = "boardNavState";
    /// <summary>Device-local MIDI panel layout: expanded sheet vs compact radial.</summary>
    public const string MidiPanelExpanded = "midiPanelExpanded";
    /// <summary>MP3 play/pause fade length in seconds (0.5–2).</summary>
    public const string Mp3FadeSeconds = "mp3FadeSeconds";

    /// <summary>
    /// Account-scoped keys wiped on sign-out / account switch.
    /// Keeps <see cref="CloudOwnerUid"/> so the next login can detect a switch.
    /// Device catalog keys are not included.
    /// </summary>
    public static readonly string[] AccountScopedKeys =
    {
        SettingsJson,
        BookmarksJson,
        HistoryJson,
        LastHymnNumber,
        HymnInputType,
        DarkMode,
        ActiveFont,
        ActiveFontSize,
        ActiveTheme,
        ActiveAlignment,
        LetterSpacing,
        LineSpacing,
        AgentMode,
        AgentChatLimit,
        CloudPending,
        CloudUpdatedAt,
    };

    /// <summary>Last server catalogHash after a successful sync / baseline (MAUI: hymnCatalogHash).</summary>
    public const string HymnCatalogHash = "hymnCatalogHash";

    /// <summary>Last known hymn total from meta (MAUI: hymnTotal).</summary>
    public const string HymnTotal = "hymnTotal";

    /// <summary>
    /// Set after the installed PWA finishes downloading / caching the hymn library.
    /// Catalog update badges only run when this is set.
    /// </summary>
    public const string HymnLibraryDownloaded = "hymnLibraryDownloaded";

    /// <summary>Server catalogHash the update popup was last shown for (once per catalog version).</summary>
    public const string CatalogUpdatePromptedHash = "catalogUpdatePromptedHash";

    /// <summary>Last catalog-policy.json id (tools/push-catalog-update.ps1) this device has synced past.</summary>
    public const string CatalogPolicyAppliedId = "catalogPolicyAppliedId";
}
