namespace MobiHymn4.Web.Services;

/// <summary>Maps Firebase Auth exception text to short user-facing messages.</summary>
public static class AuthErrorMessages
{
    public static string From(Exception ex)
    {
        var raw = (ex.GetBaseException().Message ?? ex.Message ?? "").ToLowerInvariant();

        if (raw.Contains("auth/wrong-password")
            || raw.Contains("auth/invalid-password")
            || raw.Contains("auth/user-not-found")
            || raw.Contains("auth/invalid-credential")
            || raw.Contains("auth/invalid-login-credentials"))
            return "Incorrect email or password.";

        if (raw.Contains("auth/invalid-email"))
            return "Enter a valid email address.";

        if (raw.Contains("auth/email-already-in-use"))
            return "An account with this email already exists.";

        if (raw.Contains("auth/weak-password"))
            return "Password should be at least 6 characters.";

        if (raw.Contains("auth/unauthorized-continue-uri")
            || raw.Contains("auth/invalid-continue-uri")
            || raw.Contains("auth/missing-continue-uri")
            || raw.Contains("not whitelisted"))
            return "Couldn’t send the email link. Try again in a moment.";

        if (raw.Contains("auth/too-many-requests") || raw.Contains("too-many-requests"))
            return "Too many emails sent. Wait a few minutes, check spam, then try Resend.";

        if (raw.Contains("auth/network-request-failed") || raw.Contains("failed to fetch"))
            return "Network error. Check your connection and try again.";

        if (raw.Contains("auth/user-disabled"))
            return "This account has been disabled.";

        if (raw.Contains("firebase is not configured"))
            return "Sign-in isn’t configured in this build.";

        if (raw.Contains("auth/missing-email") || raw.Contains("auth/missing-password"))
            return "Enter your email and password.";

        if (raw.Contains("auth/", StringComparison.Ordinal))
            return "Couldn’t complete that request. Please try again.";

        var firstLine = (ex.GetBaseException().Message ?? ex.Message ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "Something went wrong. Please try again.";
        if (firstLine.Length > 120)
            firstLine = firstLine[..117] + "…";
        return firstLine;
    }
}
