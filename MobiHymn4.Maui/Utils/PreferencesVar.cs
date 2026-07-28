using System;
namespace MobiHymn4.Utils
{
	public static class PreferencesVar
	{
        public const string IS_NEW = "isNew";
        public const string DARK_MODE = "darkMode";
        public const string KEEP_AWAKE = "keepAwake";
        public const string RESYNC_VERSION = "ResyncVersion";
        public const string HYMN_INPUT_TYPE = "hymnInputType";
        public const string ACTIVE_GROUP_ID = "activeGroupId";
        public const string ACTIVE_BOARD_DATE = "activeBoardDate";
        public const string ACTIVE_LIST_ID = "activeListId";
        public const string BOARD_NAV_STATE = "boardNavState";
        public const string LAST_HYMN_NUMBER = "lastHymnNumber";
        public const string ACTIVE_READ_THEME = "activeReadTheme";
        public const string ACTIVE_FONT = "activeFont";
        public const string ACTIVE_FONT_SIZE = "activeFontSize";
        public const string ACTIVE_LETTER_SPACING = "activeLetterSpacing";
        public const string ACTIVE_LINE_SPACING = "activeLineSpacing";
        public const string ACTIVE_ALIGNMENT = "activeAlignment";
        public const string READER_SETTINGS = "readerSettings";
        public const string GROUP_WELCOME_SEEN_IDS = "groupWelcomeSeenIds";
        // Bumped key: earlier builds could set the flag without the user seeing the popup.
        public const string COMMUNITY_PROMPT_DISMISSED = "communityPromptDismissedV2";
        public const string HYMN_TOTAL = "hymnTotal";
        public const string HYMN_CATALOG_HASH = "hymnCatalogHash";
        public const string AGENT_MODE = "agentMode";
        public const string AGENT_CHAT_LIMIT = "agentChatLimit";
        public const string PENDING_INVITES_CHECKED_AT = "pendingInvitesCheckedAt";
        public const string GROUP_NOTIFICATIONS_MUTED_PREFIX = "groupNotificationsMuted:";
        public const string CLOUD_SETTINGS_UPDATED_AT = "cloudSettingsUpdatedAt";
        public const string CLOUD_SETTINGS_PENDING = "cloudSettingsPending";
        /// <summary>Firebase uid whose settings were last applied/synced on this device.</summary>
        public const string CLOUD_SETTINGS_OWNER_UID = "cloudSettingsOwnerUid";
    }
}
