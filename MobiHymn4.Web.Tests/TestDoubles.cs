using MobiHymn4.Shared;
using MobiHymn4.Shared.Models;
using MobiHymn4.Shared.Services;
using MobiHymn4.Web.Services;

namespace MobiHymn4.Web.Tests;

sealed class MemoryAppPreferences : IAppPreferences
{
    readonly Dictionary<string, string> data = new(StringComparer.Ordinal);

    public string Get(string key, string defaultValue = "") =>
        data.TryGetValue(key, out var v) ? v : defaultValue;

    public void Set(string key, string value) => data[key] = value ?? "";

    public void Remove(string key) => data.Remove(key);

    public bool GetBool(string key, bool defaultValue = false)
    {
        var text = Get(key, defaultValue ? "true" : "false");
        return bool.TryParse(text, out var b) ? b : defaultValue;
    }

    public void SetBool(string key, bool value) => Set(key, value ? "true" : "false");

    public void ClearPrefix(string prefix)
    {
        foreach (var key in data.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            data.Remove(key);
    }

    public bool ContainsKey(string key) => data.ContainsKey(key);
}

sealed class MemorySettingsCloudStore : IUserSettingsCloudStore
{
    public Dictionary<string, UserSettingsCloudDoc> Docs { get; } = new(StringComparer.Ordinal);

    public Task EnsureReadyAsync() => Task.CompletedTask;

    public Task<UserSettingsCloudDoc?> GetSettingsAsync(string uid) =>
        Task.FromResult(Docs.TryGetValue(uid, out var d) ? Clone(d) : null);

    public Task SetSettingsAsync(string uid, UserSettingsCloudDoc doc)
    {
        Docs[uid] = Clone(doc);
        return Task.CompletedTask;
    }

    static UserSettingsCloudDoc Clone(UserSettingsCloudDoc d) => new()
    {
        UpdatedAt = d.UpdatedAt,
        LastHymnNumber = d.LastHymnNumber,
        ActiveReadTheme = d.ActiveReadTheme,
        ActiveAlignment = d.ActiveAlignment,
        ActiveFontSize = d.ActiveFontSize,
        ActiveFont = d.ActiveFont,
        ActiveLetterSpacing = d.ActiveLetterSpacing,
        ActiveLineSpacing = d.ActiveLineSpacing,
        DarkMode = d.DarkMode,
        KeepAwake = d.KeepAwake,
        HymnInputType = d.HymnInputType,
        AgentMode = d.AgentMode,
        AgentChatLimit = d.AgentChatLimit,
        History = d.History?.Select(h => new ShortHymnCloudDoc
        {
            Number = h.Number,
            Line = h.Line,
            TimeStamp = h.TimeStamp,
            BookmarkGroup = h.BookmarkGroup
        }).ToList() ?? new(),
        Bookmarks = d.Bookmarks?.Select(h => new ShortHymnCloudDoc
        {
            Number = h.Number,
            Line = h.Line,
            TimeStamp = h.TimeStamp,
            BookmarkGroup = h.BookmarkGroup
        }).ToList() ?? new(),
        Searches = d.Searches?.ToList() ?? new()
    };
}

sealed class FakeAuthService : IAuthService
{
    public event EventHandler? AuthStateChanged;
    public bool IsSignedIn => !string.IsNullOrEmpty(CurrentUserId);
    public string CurrentUserId { get; private set; } = "";
    public string CurrentEmail { get; private set; } = "";
    public bool IsEmailVerified { get; private set; } = true;

    public void SignIn(string uid, string email = "user@test.com")
    {
        CurrentUserId = uid;
        CurrentEmail = email;
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SignOut()
    {
        CurrentUserId = "";
        CurrentEmail = "";
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task SignUpWithEmailAsync(string email, string password) => throw new NotSupportedException();
    public Task SignInWithEmailAsync(string email, string password) => throw new NotSupportedException();
    public Task SendPasswordResetEmailAsync(string email) => throw new NotSupportedException();
    public Task SignOutAsync()
    {
        SignOut();
        return Task.CompletedTask;
    }
    public Task SendEmailVerificationAsync() => Task.CompletedTask;
    public Task RefreshEmailVerificationStatusAsync() => Task.CompletedTask;
    public Task UpdatePasswordAsync(string newPassword) => Task.CompletedTask;
    public Task InitializeAsync() => Task.CompletedTask;
}

sealed class StubLyricsSource : IHymnLyricsSource
{
    public Task<Hymn> GetHymnAsync(string number, CancellationToken cancellationToken = default) =>
        Task.FromResult(new Hymn { Number = number, FirstLine = $"Hymn {number}" });

    public Task<HymnCatalogMeta> GetCatalogMetaAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new HymnCatalogMeta());

    public Task<IReadOnlyList<HymnSearchHit>> SearchLyricsAsync(string query, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<HymnSearchHit>>(Array.Empty<HymnSearchHit>());

    public Task<IReadOnlyList<HymnSearchHit>> SearchMetadataAsync(string query, string field, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<HymnSearchHit>>(Array.Empty<HymnSearchHit>());

    public Task<IReadOnlyList<HymnSearchGroup>> SearchMetadataGroupsAsync(string query, string field, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<HymnSearchGroup>>(Array.Empty<HymnSearchGroup>());

    public Task WarmSearchIndexAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void InvalidateLocalCaches() { }
    public Task<HymnCatalogMeta> GetCatalogMetaFreshAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new HymnCatalogMeta());
}

sealed class StubSyncService : IUserSettingsSyncService
{
    public event EventHandler? SyncStateChanged;
    public bool IsSyncing => false;
    public string LastSyncStatus => "";
    public Task PushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task PullAndMergeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task PullAndMergeAsync(bool preferCloud, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void SchedulePush() { }
}
