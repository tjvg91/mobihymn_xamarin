using MobiHymn4.Shared.Models;

namespace MobiHymn4.Shared.Services;

/// <summary>
/// Browser tab: API on demand. Installed PWA: downloaded local catalog only.
/// </summary>
public interface IHymnLyricsSource
{
    Task<Hymn> GetHymnAsync(string number, CancellationToken cancellationToken = default);
    Task<HymnCatalogMeta> GetCatalogMetaAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Local lyrics line search across the full catalog (MAUI SearchType.Lyrics).
    /// Browser: pages from API. Installed PWA: downloaded catalog only.
    /// </summary>
    Task<IReadOnlyList<HymnSearchHit>> SearchLyricsAsync(
        string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Local metadata search. <paramref name="field"/> is author|metre|key|verse.
    /// </summary>
    Task<IReadOnlyList<HymnSearchHit>> SearchMetadataAsync(
        string query, string field, CancellationToken cancellationToken = default);

    /// <summary>
    /// Grouped metadata search (author/composer, metre, key, verse) — one accordion per distinct value.
    /// </summary>
    Task<IReadOnlyList<HymnSearchGroup>> SearchMetadataGroupsAsync(
        string query, string field, CancellationToken cancellationToken = default);

    /// <summary>Prefetch the search index in the background (no-op if already loaded).</summary>
    Task WarmSearchIndexAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch the full catalog from the network and persist it (installed PWA download / refresh).
    /// </summary>
    /// <param name="progress">Optional (completed, total) hymn counts while paging.</param>
    Task SyncCatalogFromNetworkAsync(
        IProgress<(int Completed, int Total)>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Load the persisted catalog into memory when the installed PWA already has a download.
    /// </summary>
    Task HydrateLocalCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>Drop in-memory hymn / meta / search caches (e.g. after catalog update).</summary>
    void InvalidateLocalCaches();

    /// <summary>Fetch catalog meta bypassing the in-memory cache (no-cache request).</summary>
    Task<HymnCatalogMeta> GetCatalogMetaFreshAsync(CancellationToken cancellationToken = default);
}

/// <summary>Durable storage for the installed-PWA hymn catalog.</summary>
public interface IHymnCatalogStore
{
    Task ReplaceAllJsonAsync(string hymnsJsonArray, CancellationToken cancellationToken = default);
    Task<string?> GetHymnJsonAsync(string number, CancellationToken cancellationToken = default);
    Task<string> GetAllJsonAsync(CancellationToken cancellationToken = default);
    Task<int> CountAsync(CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>Whether the client must use the downloaded catalog.</summary>
public interface IHymnAccessPolicy
{
    bool IsStandalonePwa { get; }
    bool IsLibraryDownloaded { get; }
    bool UseLocalCatalogOnly { get; }
    Task InitializeAsync();
}

public interface IAppPreferences
{
    string Get(string key, string defaultValue = "");
    void Set(string key, string value);
    void Remove(string key);
    bool GetBool(string key, bool defaultValue = false);
    void SetBool(string key, bool value);
    void ClearPrefix(string prefix);
}

public interface IAuthService
{
    event EventHandler? AuthStateChanged;

    bool IsSignedIn { get; }
    string CurrentUserId { get; }
    string CurrentEmail { get; }
    bool IsEmailVerified { get; }

    Task SignUpWithEmailAsync(string email, string password);
    Task SignInWithEmailAsync(string email, string password);
    Task SendPasswordResetEmailAsync(string email);
    Task UpdatePasswordAsync(string newPassword);
    Task SendEmailVerificationAsync();
    Task RefreshEmailVerificationStatusAsync();
    Task SignOutAsync();
    Task InitializeAsync();
}

public interface IProfileService
{
    event EventHandler? ProfileChanged;

    UserProfileDoc? CurrentProfile { get; }
    bool HasCompleteProfile { get; }
    Task RefreshCurrentProfileAsync();
    Task SaveProfileAsync(UserProfileDoc profile);
    Task SetNotificationsMutedAsync(bool muted);
}

public interface IBoardNotificationService
{
    Task StartAsync();
    Task StopAsync();
    /// <returns>True when an FCM token was obtained and saved.</returns>
    Task<bool> RegisterTokenAsync(bool force = false);
    /// <summary>Last RegisterTokenAsync failure detail (empty when last call succeeded).</summary>
    string? LastRegisterError { get; }
    Task MarkAllReadAsync();
    Task MarkListReadAsync(string groupId, string listId);
}

public interface IGroupService
{
    Task<IReadOnlyList<WorshipGroupDoc>> GetMyGroupsAsync();
    Task<WorshipGroupDoc> CreateGroupAsync(string name);
    Task<WorshipGroupDoc> JoinGroupAsync(string joinCode);
    Task LeaveGroupAsync(string groupId);
    Task<IReadOnlyList<GroupMemberDoc>> GetMembersAsync(string groupId);
    Task SetMemberAdminAsync(string groupId, string memberId, bool isAdmin);
    Task RemoveMemberAsync(string groupId, string memberId);
    Task<bool> IsGroupNotificationsMutedAsync(string groupId);
    Task SetGroupNotificationsMutedAsync(string groupId, bool muted);
}

public interface IBoardService
{
    Task<IReadOnlyList<BoardListDoc>> GetListsAsync(string groupId);
    Task<BoardListDoc?> GetListAsync(string groupId, string listId);
    Task<BoardListDoc> CreateListAsync(string groupId, string name);
    Task<BoardListDoc> UpdateListDateAsync(string groupId, string listId, DateTime date);
    Task UpdateListAsync(string groupId, BoardListDoc list);
    Task DeleteListAsync(string groupId, string listId);
    Task ClearAllHymnsAsync(string groupId, string listId);
    Task ClearAllSectionsInListAsync(string groupId, string listId);
    Task ClearAllSavedSectionsAsync(string groupId, string listId);
    Task<IReadOnlyCollection<string>> GetSavedSectionNamesAsync(string groupId);
    Task<BoardListDoc?> EnsureSavedSectionsOnListAsync(string groupId, string listId);
    Task SaveSectionToTemplateAsync(string groupId, string listId, string sectionName);
    Task RemoveSectionFromTemplateAsync(string groupId, string sectionName);
    Task RemoveSectionAsync(string groupId, string listId, string sectionId, bool deleteHymnsInSection);
    Task<BoardListDoc> ApplySectionSortAsync(string groupId, string listId, string sortMode);
    IDisposable SubscribeList(string groupId, string listId, Action<BoardListDoc?> onChange);
}

public interface IUserSettingsSyncService
{
    event EventHandler? SyncStateChanged;
    bool IsSyncing { get; }
    string LastSyncStatus { get; }
    Task PushAsync(CancellationToken cancellationToken = default);
    Task PullAndMergeAsync(CancellationToken cancellationToken = default);
    /// <param name="preferCloud">When true (e.g. after login), reader prefs come from Firestore even if local looks newer.</param>
    Task PullAndMergeAsync(bool preferCloud, CancellationToken cancellationToken = default);
    /// <summary>Fetch and apply current cloud settings when the app opens or returns to foreground.</summary>
    Task SyncOnAppOpenAsync(CancellationToken cancellationToken = default);
    void SchedulePush();
}

public interface IAgentApi
{
    Task<AgentSearchResponse> SearchAsync(string query, int limit, string? mode = null, CancellationToken ct = default);
    Task<AgentChatResponse> ChatAsync(
        IReadOnlyList<AgentChatMessageDto> messages,
        int limit,
        string? sessionId = null,
        string? mode = null,
        CancellationToken ct = default);
}

public interface ILocalUserDataStore
{
    Task<List<ShortHymn>> GetBookmarksAsync();
    Task SaveBookmarksAsync(IEnumerable<ShortHymn> bookmarks);
    Task<List<ShortHymn>> GetHistoryAsync();
    Task SaveHistoryAsync(IEnumerable<ShortHymn> history);
    Task<UserSettingsCloudDoc> GetLocalSettingsAsync();
    Task SaveLocalSettingsAsync(UserSettingsCloudDoc settings, bool touchUpdatedAt = true);
}
