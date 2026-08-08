using System.Text.Json;
using Microsoft.JSInterop;
using MobiHymn4.Shared;
using MobiHymn4.Shared.Models;
using MobiHymn4.Shared.Services;

namespace MobiHymn4.Web.Services;

/// <summary>Thin JS interop wrapper around Firebase Web SDK (wwwroot/js/firebase.js).</summary>
public sealed class FirebaseJs
{
    readonly IJSRuntime js;
    bool ready;

    public FirebaseJs(IJSRuntime js) => this.js = js;

    public async Task EnsureReadyAsync()
    {
        if (ready) return;
        ready = await js.InvokeAsync<bool>("mobihymnFirebase.ensureInit");
    }

    public Task<JsonElement?> GetCurrentUserAsync() =>
        InvokeJsonOrNullAsync("mobihymnFirebase.getCurrentUserAsJson");

    public Task SignInAsync(string email, string password) =>
        js.InvokeVoidAsync("mobihymnFirebase.signIn", email, password).AsTask();

    public Task SignUpAsync(string email, string password) =>
        js.InvokeVoidAsync("mobihymnFirebase.signUp", email, password).AsTask();

    public Task SignOutAsync() =>
        js.InvokeVoidAsync("mobihymnFirebase.signOut").AsTask();

    public Task SendPasswordResetAsync(string email) =>
        js.InvokeVoidAsync("mobihymnFirebase.sendPasswordReset", email).AsTask();

    public Task SendEmailVerificationAsync() =>
        js.InvokeVoidAsync("mobihymnFirebase.sendEmailVerification").AsTask();

    public Task UpdatePasswordAsync(string password) =>
        js.InvokeVoidAsync("mobihymnFirebase.updatePassword", password).AsTask();

    public Task ReloadUserAsync() =>
        js.InvokeVoidAsync("mobihymnFirebase.reloadUser").AsTask();

    public Task<JsonElement?> GetDocAsync(string path) =>
        InvokeJsonOrNullAsync("mobihymnFirebase.getDocAsJson", path);

    public Task SetDocAsync(string path, object data) =>
        js.InvokeVoidAsync("mobihymnFirebase.setDoc", path, data).AsTask();

    public Task<JsonElement[]> QueryCollectionAsync(string path, string? field = null, string? op = null, object? value = null) =>
        js.InvokeAsync<JsonElement[]>("mobihymnFirebase.queryCollection", path, field, op, value).AsTask();

    public Task<JsonElement[]> QueryCollectionGroupAsync(string collectionId, string? documentIdEquals = null) =>
        js.InvokeAsync<JsonElement[]>("mobihymnFirebase.queryCollectionGroup", collectionId, documentIdEquals).AsTask();

    public Task DeleteDocAsync(string path) =>
        js.InvokeVoidAsync("mobihymnFirebase.deleteDoc", path).AsTask();

    public Task<string?> SubscribeDocAsync(string path, object callback) =>
        js.InvokeAsync<string?>("mobihymnFirebase.subscribeDoc", path, callback).AsTask();

    public Task<string?> SubscribeQueryAsync(
        string path, string? field, string? op, object? value, int? max, object callback) =>
        js.InvokeAsync<string?>("mobihymnFirebase.subscribeQuery", path, field, op, value, max, callback).AsTask();

    public Task UnsubscribeAsync(string? subscriptionId)
    {
        if (string.IsNullOrEmpty(subscriptionId))
            return Task.CompletedTask;
        return js.InvokeVoidAsync("mobihymnFirebase.unsubscribe", subscriptionId).AsTask();
    }

    public Task<string?> GetFcmTokenAsync() =>
        js.InvokeAsync<string?>("mobihymnFirebase.getFcmToken").AsTask();

    public Task<string> GetNotificationPermissionAsync() =>
        js.InvokeAsync<string>("mobihymnFirebase.getNotificationPermission").AsTask();

    public Task<string> RequestNotificationPermissionAsync() =>
        js.InvokeAsync<string>("mobihymnFirebase.requestNotificationPermission").AsTask();

    public Task ShowLocalNotificationAsync(string title, string body, object data) =>
        js.InvokeVoidAsync("mobihymnFirebase.showLocalNotification", title, body, data).AsTask();

    public Task OnBoardNotificationClickAsync(object callback) =>
        js.InvokeVoidAsync("mobihymnFirebase.onBoardNotificationClick", callback).AsTask();

    public Task<string?> GetMidiUrlAsync(string number) =>
        js.InvokeAsync<string?>("mobihymnFirebase.getMidiUrl", number).AsTask();

    public async Task<LatestRelease?> GetLatestReleaseAsync(string platform = "Web")
    {
        await EnsureReadyAsync();
        var el = await InvokeJsonOrNullAsync("mobihymnFirebase.getLatestReleaseAsJson", platform);
        if (el == null || el.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        var o = el.Value;
        return new LatestRelease
        {
            Version = o.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "",
            DownloadUrl = o.TryGetProperty("downloadUrl", out var d) ? d.GetString() ?? "" : "",
            Mandatory = o.TryGetProperty("mandatory", out var m) && m.ValueKind == JsonValueKind.True
        };
    }

    static async Task<JsonElement?> InvokeJsonOrNullAsync(IJSRuntime js, string identifier, params object?[] args)
    {
        var json = args.Length == 0
            ? await js.InvokeAsync<string?>(identifier)
            : await js.InvokeAsync<string?>(identifier, args);
        if (string.IsNullOrWhiteSpace(json) || string.Equals(json, "null", StringComparison.Ordinal))
            return null;

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    Task<JsonElement?> InvokeJsonOrNullAsync(string identifier, params object?[] args) =>
        InvokeJsonOrNullAsync(js, identifier, args);
}

public sealed class FirebaseAuthService : IAuthService
{
    readonly FirebaseJs firebase;
    string uid = "";
    string email = "";
    bool verified;

    public FirebaseAuthService(FirebaseJs firebase) => this.firebase = firebase;

    public event EventHandler? AuthStateChanged;

    public bool IsSignedIn => !string.IsNullOrEmpty(uid);
    public string CurrentUserId => uid;
    public string CurrentEmail => email;
    public bool IsEmailVerified => verified;

    public async Task InitializeAsync()
    {
        try
        {
            await firebase.EnsureReadyAsync();
            await RefreshFromJsAsync();
        }
        catch
        {
            // Firebase config may be missing in local/dev — app still works for reader/API.
        }
    }

    async Task RefreshFromJsAsync()
    {
        var user = await firebase.GetCurrentUserAsync();
        if (user == null || user.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            uid = "";
            email = "";
            verified = false;
        }
        else
        {
            uid = user.Value.TryGetProperty("uid", out var u) ? u.GetString() ?? "" : "";
            email = user.Value.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
            verified = user.Value.TryGetProperty("emailVerified", out var v) && v.GetBoolean();
        }

        AuthStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SignInWithEmailAsync(string email, string password)
    {
        await firebase.EnsureReadyAsync();
        await firebase.SignInAsync(email.Trim(), password);
        await RefreshFromJsAsync();
    }

    public async Task SignUpWithEmailAsync(string email, string password)
    {
        await firebase.EnsureReadyAsync();
        await firebase.SignUpAsync(email.Trim(), password);
        await firebase.SendEmailVerificationAsync();
        await RefreshFromJsAsync();
    }

    public async Task SendPasswordResetEmailAsync(string email)
    {
        await firebase.EnsureReadyAsync();
        await firebase.SendPasswordResetAsync(email.Trim());
    }

    public async Task UpdatePasswordAsync(string newPassword)
    {
        await firebase.EnsureReadyAsync();
        await firebase.UpdatePasswordAsync(newPassword);
    }

    public async Task SendEmailVerificationAsync()
    {
        await firebase.EnsureReadyAsync();
        await firebase.SendEmailVerificationAsync();
    }

    public async Task RefreshEmailVerificationStatusAsync()
    {
        await firebase.EnsureReadyAsync();
        await firebase.ReloadUserAsync();
        await RefreshFromJsAsync();
    }

    public async Task SignOutAsync()
    {
        try
        {
            await firebase.EnsureReadyAsync();
            await firebase.SignOutAsync();
        }
        finally
        {
            uid = "";
            email = "";
            verified = false;
            AuthStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
