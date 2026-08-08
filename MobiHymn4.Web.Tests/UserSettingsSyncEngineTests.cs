using Microsoft.Extensions.DependencyInjection;
using MobiHymn4.Shared;
using MobiHymn4.Shared.Models;
using MobiHymn4.Shared.Services;
using MobiHymn4.Web.Services;
using Xunit;

namespace MobiHymn4.Web.Tests;

/// <summary>
/// Covers: sign out → guest edits → sign in / sign up outcomes for local vs cloud settings.
/// </summary>
public sealed class UserSettingsSyncEngineTests
{
    const string AccountA = "uid-account-a";
    const string AccountB = "uid-account-b";

    [Fact]
    public async Task SignOut_ClearsLocalSettings_ButKeepsCloudOwnerUid()
    {
        var fx = await CreateFixtureAsync(signedInAs: AccountA);
        fx.Prefs.Set(PrefKeys.CloudOwnerUid, AccountA);
        fx.State.Settings.ActiveAlignment = 2;
        fx.State.Settings.DarkMode = true;
        fx.State.Bookmarks.Add(new ShortHymn { Number = "10", Line = "A", BookmarkGroup = "General" });
        await fx.Store.SaveLocalSettingsAsync(fx.State.Settings);
        await fx.Store.SaveBookmarksAsync(fx.State.Bookmarks);

        await fx.Engine.HandleSignedOutAsync();

        Assert.Equal(AccountA, fx.Prefs.Get(PrefKeys.CloudOwnerUid));
        Assert.Equal(0, fx.State.Settings.ActiveAlignment);
        Assert.False(fx.State.Settings.DarkMode);
        Assert.Empty(fx.State.Bookmarks);
        Assert.Empty(fx.State.History);
        Assert.False(fx.Prefs.GetBool(PrefKeys.DarkMode, true));
    }

    [Fact]
    public async Task GuestChanges_ThenSignInSameAccount_CloudAlignmentWins_BookmarksAndHistoryMerge()
    {
        var fx = await CreateFixtureAsync(signedInAs: null);
        fx.Prefs.Set(PrefKeys.CloudOwnerUid, AccountA);

        // Cloud backup for account A (from before sign-out).
        fx.Cloud.Docs[AccountA] = new UserSettingsCloudDoc
        {
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            ActiveAlignment = 1,
            DarkMode = true,
            ActiveFontSize = 22,
            Bookmarks =
            [
                new ShortHymnCloudDoc { Number = "1", Line = "Cloud bookmark", BookmarkGroup = "General", TimeStamp = DateTimeOffset.UtcNow.AddDays(-2) }
            ],
            History =
            [
                new ShortHymnCloudDoc { Number = "2", Line = "Cloud history", TimeStamp = DateTimeOffset.UtcNow.AddDays(-2) }
            ]
        };

        // Step 2 — guest edits after sign-out wipe.
        await fx.Engine.HandleSignedOutAsync();
        fx.State.Settings.ActiveAlignment = 2; // different from cloud
        fx.State.Settings.DarkMode = false;
        fx.State.Bookmarks.Clear();
        fx.State.Bookmarks.Add(new ShortHymn { Number = "99", Line = "Guest bookmark", BookmarkGroup = "General", TimeStamp = DateTime.UtcNow });
        fx.State.History.Clear();
        fx.State.History.Add(new ShortHymn { Number = "88", Line = "Guest history", TimeStamp = DateTime.UtcNow });
        await fx.Store.SaveLocalSettingsAsync(fx.State.Settings);
        await fx.Store.SaveBookmarksAsync(fx.State.Bookmarks);
        await fx.Store.SaveHistoryAsync(fx.State.History);

        // Step 3 — sign in to same account A.
        fx.Auth.SignIn(AccountA);
        await fx.Engine.PullAndMergeAsync(preferCloud: true);

        Assert.Equal(1, fx.State.Settings.ActiveAlignment); // cloud wins
        Assert.True(fx.State.Settings.DarkMode); // cloud wins
        Assert.Contains(fx.State.Bookmarks, b => b.Number == "1");
        Assert.Contains(fx.State.Bookmarks, b => b.Number == "99"); // guest merged
        Assert.Contains(fx.State.History, h => h.Number == "2");
        Assert.Contains(fx.State.History, h => h.Number == "88");
    }

    [Fact]
    public async Task GuestChanges_ThenSignInDifferentAccount_WipesGuest_LoadsCloud()
    {
        var fx = await CreateFixtureAsync(signedInAs: null);
        fx.Prefs.Set(PrefKeys.CloudOwnerUid, AccountA);

        fx.Cloud.Docs[AccountB] = new UserSettingsCloudDoc
        {
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            ActiveAlignment = 1,
            DarkMode = true,
            ActiveFontSize = 24,
            Bookmarks =
            [
                new ShortHymnCloudDoc { Number = "7", Line = "B cloud", BookmarkGroup = "General", TimeStamp = DateTimeOffset.UtcNow }
            ],
            History =
            [
                new ShortHymnCloudDoc { Number = "8", Line = "B hist", TimeStamp = DateTimeOffset.UtcNow }
            ]
        };

        await fx.Engine.HandleSignedOutAsync();
        fx.State.Settings.ActiveAlignment = 2;
        fx.State.Bookmarks.Clear();
        fx.State.Bookmarks.Add(new ShortHymn { Number = "99", Line = "Guest", BookmarkGroup = "General" });
        fx.State.History.Clear();
        fx.State.History.Add(new ShortHymn { Number = "88", Line = "Guest hist" });
        await fx.Store.SaveLocalSettingsAsync(fx.State.Settings);
        await fx.Store.SaveBookmarksAsync(fx.State.Bookmarks);
        await fx.Store.SaveHistoryAsync(fx.State.History);

        fx.Auth.SignIn(AccountB);
        await fx.Engine.PullAndMergeAsync(preferCloud: true);

        Assert.Equal(1, fx.State.Settings.ActiveAlignment);
        Assert.True(fx.State.Settings.DarkMode);
        Assert.Equal(24, fx.State.Settings.ActiveFontSize);
        Assert.DoesNotContain(fx.State.Bookmarks, b => b.Number == "99");
        Assert.DoesNotContain(fx.State.History, h => h.Number == "88");
        Assert.Contains(fx.State.Bookmarks, b => b.Number == "7");
        Assert.Equal(AccountB, fx.Prefs.Get(PrefKeys.CloudOwnerUid));
    }

    [Fact]
    public async Task GuestChanges_ThenSignInDifferentAccount_NoCloud_UsesDefaults()
    {
        var fx = await CreateFixtureAsync(signedInAs: null);
        fx.Prefs.Set(PrefKeys.CloudOwnerUid, AccountA);

        await fx.Engine.HandleSignedOutAsync();
        fx.State.Settings.ActiveAlignment = 2;
        fx.State.Settings.DarkMode = true;
        fx.State.Bookmarks.Clear();
        fx.State.Bookmarks.Add(new ShortHymn { Number = "99", Line = "Guest", BookmarkGroup = "General" });
        await fx.Store.SaveLocalSettingsAsync(fx.State.Settings);
        await fx.Store.SaveBookmarksAsync(fx.State.Bookmarks);

        fx.Auth.SignIn(AccountB);
        await fx.Engine.PullAndMergeAsync(preferCloud: true);

        Assert.Equal(0, fx.State.Settings.ActiveAlignment);
        Assert.False(fx.State.Settings.DarkMode);
        Assert.Empty(fx.State.Bookmarks);
        Assert.True(fx.Cloud.Docs.ContainsKey(AccountB));
        Assert.Empty(fx.Cloud.Docs[AccountB].Bookmarks);
        Assert.Equal(AccountB, fx.Prefs.Get(PrefKeys.CloudOwnerUid));
    }

    [Fact]
    public async Task SignUp_UploadsPreExistingLocalSettings_ToNewAccount()
    {
        var fx = await CreateFixtureAsync(signedInAs: null);
        fx.Prefs.Set(PrefKeys.CloudOwnerUid, AccountA); // previous account on device

        await fx.Engine.HandleSignedOutAsync();
        fx.State.Settings.ActiveAlignment = 2;
        fx.State.Settings.DarkMode = true;
        fx.State.Bookmarks.Clear();
        fx.State.Bookmarks.Add(new ShortHymn { Number = "55", Line = "Pre-signup", BookmarkGroup = "General", TimeStamp = DateTime.UtcNow });
        fx.State.History.Clear();
        fx.State.History.Add(new ShortHymn { Number = "66", Line = "Pre-signup hist", TimeStamp = DateTime.UtcNow });
        await fx.Store.SaveLocalSettingsAsync(fx.State.Settings);
        await fx.Store.SaveBookmarksAsync(fx.State.Bookmarks);
        await fx.Store.SaveHistoryAsync(fx.State.History);

        // Sign-up path: seed flag + new uid.
        fx.Prefs.SetBool(PrefKeys.SeedLocalSettingsOnNextSync, true);
        fx.Auth.SignIn(AccountB);
        await fx.Engine.PullAndMergeAsync(preferCloud: false);

        Assert.Equal(2, fx.State.Settings.ActiveAlignment);
        Assert.True(fx.State.Settings.DarkMode);
        Assert.Contains(fx.State.Bookmarks, b => b.Number == "55");
        Assert.True(fx.Cloud.Docs.ContainsKey(AccountB));
        Assert.Equal(2, fx.Cloud.Docs[AccountB].ActiveAlignment);
        Assert.Contains(fx.Cloud.Docs[AccountB].Bookmarks, b => b.Number == "55");
        Assert.Equal(AccountB, fx.Prefs.Get(PrefKeys.CloudOwnerUid));
        Assert.False(fx.Prefs.GetBool(PrefKeys.SeedLocalSettingsOnNextSync, false));
    }

    static async Task<Fixture> CreateFixtureAsync(string? signedInAs)
    {
        var prefs = new MemoryAppPreferences();
        var store = new BrowserUserDataStore(prefs);
        var cloud = new MemorySettingsCloudStore();
        var auth = new FakeAuthService();
        var services = new ServiceCollection();
        services.AddSingleton<IUserSettingsSyncService, StubSyncService>();
        var sp = services.BuildServiceProvider();
        var state = new AppState(new StubLyricsSource(), store, prefs, sp);
        await state.ClearAccountLocalStorageAndResetAsync();

        var engine = new UserSettingsSyncEngine(
            prefs,
            cloud,
            () => state,
            () => auth.IsSignedIn,
            () => auth.CurrentUserId);

        if (!string.IsNullOrEmpty(signedInAs))
        {
            auth.SignIn(signedInAs);
            prefs.Set(PrefKeys.CloudOwnerUid, signedInAs);
        }

        return new Fixture(prefs, store, cloud, auth, state, engine);
    }

    sealed record Fixture(
        MemoryAppPreferences Prefs,
        BrowserUserDataStore Store,
        MemorySettingsCloudStore Cloud,
        FakeAuthService Auth,
        AppState State,
        UserSettingsSyncEngine Engine);
}
