using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MobiHymn4.Shared;
using MobiHymn4.Shared.Services;
using MobiHymn4.Web;
using MobiHymn4.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});

builder.Services.AddScoped<IAppPreferences, BrowserPreferences>();
builder.Services.AddScoped<ILocalUserDataStore, BrowserUserDataStore>();
builder.Services.AddScoped<IHymnCatalogStore, BrowserHymnCatalogStore>();
builder.Services.AddScoped<IHymnAccessPolicy, HymnAccessPolicy>();
builder.Services.AddScoped<IHymnLyricsSource, ApiHymnLyricsSource>();
builder.Services.AddScoped<IAgentApi, AgentApiClient>();
builder.Services.AddScoped<AppState>();
builder.Services.AddScoped<IntroUiState>();
builder.Services.AddScoped<BookmarksUiState>();
builder.Services.AddScoped<AgentUiState>();
builder.Services.AddScoped<IAuthService, FirebaseAuthService>();
builder.Services.AddScoped<IProfileService, FirebaseProfileService>();
builder.Services.AddScoped<IGroupService, FirebaseGroupService>();
builder.Services.AddScoped<IBoardService, FirebaseBoardService>();
builder.Services.AddScoped<IUserSettingsCloudStore, FirestoreUserSettingsCloudStore>();
builder.Services.AddScoped<IUserSettingsSyncService, FirebaseSettingsSyncService>();
builder.Services.AddScoped<BoardUiState>();
builder.Services.AddScoped<IBoardNotificationService, FirebaseBoardNotificationService>();
builder.Services.AddScoped<FirebaseJs>();
builder.Services.AddScoped<AuthNavigation>();
builder.Services.AddScoped<CatalogUpdateService>();
builder.Services.AddScoped<AppUpdateService>();

var host = builder.Build();

// Keep startup light so the splash/UI can paint on iOS PWA before Firebase / IndexedDB finish.
await RunWithTimeout(
    async () =>
    {
        var auth = host.Services.GetRequiredService<IAuthService>();
        await auth.InitializeAsync();
    },
    TimeSpan.FromSeconds(8));

await RunWithTimeout(
    async () =>
    {
        var appState = host.Services.GetRequiredService<AppState>();
        await appState.InitializeAsync();
    },
    TimeSpan.FromSeconds(5));

_ = WarmUpAfterUiAsync(host);

await host.RunAsync();

static async Task WarmUpAfterUiAsync(WebAssemblyHost host)
{
    // Yield so RunAsync can start rendering first.
    await Task.Yield();
    try
    {
        var settingsSync = host.Services.GetRequiredService<IUserSettingsSyncService>();
        if (settingsSync is FirebaseSettingsSyncService firebaseSettingsSync)
            await firebaseSettingsSync.BootstrapAsync();
    }
    catch { /* offline / slow Firebase */ }

    try
    {
        _ = host.Services.GetRequiredService<IBoardNotificationService>().StartAsync();
    }
    catch { /* ignore */ }

    try
    {
        var access = host.Services.GetRequiredService<IHymnAccessPolicy>();
        await access.InitializeAsync();
        var catalog = host.Services.GetRequiredService<CatalogUpdateService>();
        await catalog.InitializeAsync();
        if (catalog.IsStandalonePwa && catalog.IsLibraryDownloaded)
        {
            var lyrics = host.Services.GetRequiredService<IHymnLyricsSource>();
            await lyrics.HydrateLocalCatalogAsync();
        }
        if (catalog.IsUpdateTrackingEnabled)
            _ = catalog.RefreshAsync();
    }
    catch { /* download / hydrate later from Settings */ }
}

static async Task RunWithTimeout(Func<Task> work, TimeSpan timeout)
{
    try
    {
        var task = work();
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (completed == task)
            await task;
    }
    catch
    {
        /* continue launching UI */
    }
}
