using Plugin.Firebase.Auth;

namespace MobiHymn4.Utils;

/// <summary>
/// Firebase Auth email-action settings (verification / password reset).
/// Continue URL must be an authorized domain (Authentication → Settings).
/// Do not set AndroidPackageName / iOSBundleId — that requires Firebase Dynamic
/// Links, which shut down in August 2025 ("FDL domain is not configured").
/// </summary>
public static class AuthEmailActionSettings
{
    public const string AndroidPackageName = "com.tjapps.mobihymn";
    public const string IosBundleId = "com.tjapps.mobihymn";

    /// <summary>Authorized HTTPS continue URL after email verification / reset.</summary>
    public const string ContinueUrl = "https://mobihymn.firebaseapp.com/auth/continue";

    public static ActionCodeSettings Create(bool handleCodeInApp = false)
    {
        return new ActionCodeSettings
        {
            Url = ContinueUrl,
            // Browser completes the action; Continue opens this URL (App Link / intent).
            HandleCodeInApp = handleCodeInApp,
        };
    }

    public static bool IsAuthContinueUri(System.Uri uri)
    {
        if (uri == null)
            return false;

        if (string.Equals(uri.Scheme, "mobihymn", System.StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, "auth", System.StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.Equals(uri.Scheme, "https", System.StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, "http", System.StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.Equals(uri.Host, "mobihymn.firebaseapp.com", System.StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Host, "mobihymn.web.app", System.StringComparison.OrdinalIgnoreCase))
            return false;

        // Continue URL only (e.g. /auth/continue). Email links use /__/auth/action — leave those in the browser.
        var path = uri.AbsolutePath ?? string.Empty;
        return path.StartsWith("/auth", System.StringComparison.OrdinalIgnoreCase);
    }
}
