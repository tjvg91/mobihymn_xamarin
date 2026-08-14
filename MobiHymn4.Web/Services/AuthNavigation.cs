using Microsoft.AspNetCore.Components;
using MobiHymn4.Shared.Services;

namespace MobiHymn4.Web.Services;

/// <summary>
/// Web equivalent of MAUI <c>AuthNavigationHelper</c> — routes by auth / verify / profile state
/// after email-action continue links and sign-in.
/// </summary>
public sealed class AuthNavigation
{
    readonly IAuthService auth;
    readonly IProfileService profiles;
    readonly NavigationManager nav;

    public AuthNavigation(IAuthService auth, IProfileService profiles, NavigationManager nav)
    {
        this.auth = auth;
        this.profiles = profiles;
        this.nav = nav;
    }

    /// <summary>Login URL that returns to the current page (or <paramref name="returnUrl"/>) after sign-in.</summary>
    public string LoginUrl(string? returnUrl = null)
    {
        var target = SanitizeReturnUrl(returnUrl) ?? CurrentRelativePath();
        if (string.IsNullOrEmpty(target)
            || target.StartsWith("login", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("verify", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("auth/", StringComparison.OrdinalIgnoreCase))
            return "login";

        return $"login?returnUrl={Uri.EscapeDataString(target)}";
    }

    /// <summary>Verify-email page, optionally returning to <paramref name="returnUrl"/> after verification.</summary>
    public string VerifyUrl(string? returnUrl = null)
    {
        var target = SanitizeReturnUrl(returnUrl) ?? CurrentRelativePath();
        if (string.IsNullOrEmpty(target)
            || target.StartsWith("login", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("verify", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("auth/", StringComparison.OrdinalIgnoreCase))
            return "verify";

        return $"verify?returnUrl={Uri.EscapeDataString(target)}";
    }

    /// <summary>Signed in with a verified email — required for groups / boards.</summary>
    public bool CanAccessGroups => auth.IsSignedIn && auth.IsEmailVerified;

    /// <summary>
    /// After a successful password sign-in: verify → profile if needed, otherwise restore
    /// <paramref name="returnUrl"/> (seamless return to the page the user came from).
    /// </summary>
    public async Task NavigateAfterSignInAsync(string? returnUrl = null, bool replace = true)
    {
        if (!auth.IsSignedIn)
        {
            nav.NavigateTo("login", replace);
            return;
        }

        await profiles.RefreshCurrentProfileAsync();
        var safeReturn = SanitizeReturnUrl(returnUrl);

        if (!auth.IsEmailVerified)
        {
            nav.NavigateTo(WithReturnQuery("verify", safeReturn), forceLoad: false, replace: replace);
            return;
        }

        if (!profiles.HasCompleteProfile)
        {
            nav.NavigateTo(WithReturnQuery("profile", safeReturn), forceLoad: false, replace: replace);
            return;
        }

        // Prefer the page they left — never dump onto a blank Account screen after login.
        nav.NavigateTo(safeReturn ?? "", forceLoad: false, replace: replace);
    }

    public async Task NavigateForAuthStateAsync(string? returnUrl = null, bool replace = true)
    {
        if (!auth.IsSignedIn)
        {
            nav.NavigateTo("login", replace);
            return;
        }

        await profiles.RefreshCurrentProfileAsync();
        var safeReturn = SanitizeReturnUrl(returnUrl);

        if (!auth.IsEmailVerified)
        {
            nav.NavigateTo(WithReturnQuery("verify", safeReturn), forceLoad: false, replace: replace);
            return;
        }

        if (!profiles.HasCompleteProfile)
        {
            nav.NavigateTo(WithReturnQuery("profile", safeReturn), forceLoad: false, replace: replace);
            return;
        }

        // Email-continue / verify flows: returnUrl if any, else Account.
        nav.NavigateTo(safeReturn ?? "account", forceLoad: false, replace: replace);
    }

    /// <summary>
    /// Continue URL after Firebase email verification / password reset (MAUI:
    /// <c>https://mobihymn.firebaseapp.com/auth/continue</c>). On web we use the
    /// current origin so the PWA receives the return link.
    /// </summary>
    public string AuthContinueUrl =>
        nav.ToAbsoluteUri("auth/continue").ToString();

    public string? SanitizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return null;

        var path = returnUrl.Trim();
        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("//", StringComparison.Ordinal)
            || path.Contains("://", StringComparison.Ordinal))
            return null;

        path = path.TrimStart('/');
        if (path.Length == 0)
            return null;

        // Never bounce back into the auth funnel.
        if (path.StartsWith("login", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("verify", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("auth/", StringComparison.OrdinalIgnoreCase))
            return null;

        return path;
    }

    string CurrentRelativePath()
    {
        var abs = nav.Uri;
        var bas = nav.BaseUri;
        string rel;
        if (abs.StartsWith(bas, StringComparison.OrdinalIgnoreCase))
            rel = abs[bas.Length..];
        else
            rel = new Uri(abs).PathAndQuery.TrimStart('/');

        var hash = rel.IndexOf('#');
        if (hash >= 0)
            rel = rel[..hash];

        return rel.TrimStart('/');
    }

    static string WithReturnQuery(string path, string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl))
            return path;
        return $"{path}?returnUrl={Uri.EscapeDataString(returnUrl)}";
    }
}
