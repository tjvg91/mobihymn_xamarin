using System;
using System.Threading.Tasks;
using MobiHymn4.Utils;
using Plugin.Firebase.Auth;
#if ANDROID
using AndroidFirebaseAuth = Firebase.Auth.FirebaseAuth;
using AndroidActionCodeSettings = Firebase.Auth.ActionCodeSettings;
#endif

namespace MobiHymn4.Services;

public sealed class AuthService : IAuthService
{
    readonly IFirebaseFirestoreAccessor firebase;
    bool signedInWithGoogle;
    bool? emailVerifiedFromToken;

    public AuthService(IFirebaseFirestoreAccessor firebase)
    {
        this.firebase = firebase;
        firebase.Auth.AddAuthStateListener(_ =>
        {
            if (firebase.Auth.CurrentUser == null)
                emailVerifiedFromToken = null;
            AuthStateChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public event EventHandler AuthStateChanged;

    IFirebaseUser User => firebase.Auth.CurrentUser;

    public bool IsSignedIn => User != null;
    public string CurrentUserId => User?.Uid ?? string.Empty;
    public string CurrentEmail => User?.Email ?? string.Empty;
    public bool IsEmailVerified =>
        signedInWithGoogle
        || emailVerifiedFromToken == true
        || (User?.IsEmailVerified ?? false);
    public bool SignedInWithGoogle => signedInWithGoogle;

    public async Task SignUpWithEmailAsync(string email, string password)
    {
        signedInWithGoogle = false;
        emailVerifiedFromToken = null;
        await firebase.Auth.CreateUserAsync(email, password);
        await SendEmailVerificationAsync();
        // AuthStateListener already raises AuthStateChanged — avoid a duplicate profile fetch.
    }

    public async Task SignInWithEmailAsync(string email, string password)
    {
        signedInWithGoogle = false;
        emailVerifiedFromToken = null;
        await firebase.Auth.SignInWithEmailAndPasswordAsync(email, password, false);
    }

    public async Task SignInWithGoogleAsync()
    {
        // Temporarily disabled — email/password only.
        throw new NotSupportedException("Google sign-in is not available right now. Use email and password.");
    }

    public async Task SendPasswordResetEmailAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("Enter your email address.");

        email = email.Trim();

#if ANDROID
        // Plugin overload has no ActionCodeSettings — use native so Continue opens the app.
        var native = ToAndroidNative(AuthEmailActionSettings.Create());
        await AndroidFirebaseAuth.Instance.SendPasswordResetEmailAsync(email, native);
#else
        await firebase.Auth.SendPasswordResetEmailAsync(email);
#endif
    }

    public async Task UpdatePasswordAsync(string newPassword)
    {
        if (User == null)
            throw new InvalidOperationException("Sign in to change your password.");
        if (signedInWithGoogle)
            throw new InvalidOperationException("Google accounts manage passwords in Google Account settings.");
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            throw new InvalidOperationException("Password must be at least 6 characters.");

        await User.UpdatePasswordAsync(newPassword);
    }

    public async Task SendEmailVerificationAsync()
    {
        if (User == null || signedInWithGoogle)
            return;

        await User.SendEmailVerificationAsync(AuthEmailActionSettings.Create());
    }

    public async Task<string> GetIdTokenAsync(bool forceRefresh = false)
    {
        if (User == null)
            return null;

        var tokenResult = await User.GetIdTokenResultAsync(forceRefresh);
        return tokenResult?.Token;
    }

    public async Task RefreshEmailVerificationStatusAsync()
    {
        if (User == null || signedInWithGoogle)
            return;

        // Plugin.Firebase has no ReloadAsync; force a fresh ID token and read email_verified.
        var tokenResult = await User.GetIdTokenResultAsync(true);
        emailVerifiedFromToken = ReadEmailVerifiedClaim(tokenResult) || User.IsEmailVerified;
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
    }

    static bool ReadEmailVerifiedClaim(IAuthTokenResult tokenResult)
    {
        if (tokenResult == null)
            return false;

        try
        {
            var claim = tokenResult.GetClaim<bool>("email_verified");
            return claim;
        }
        catch
        {
        }

        try
        {
            if (tokenResult.Claims != null
                && tokenResult.Claims.TryGetValue("email_verified", out var raw)
                && raw != null)
            {
                if (raw is bool b)
                    return b;
                if (bool.TryParse(raw.ToString(), out var parsed))
                    return parsed;
            }
        }
        catch
        {
        }

        return false;
    }

    public async Task SignOutAsync()
    {
        signedInWithGoogle = false;
        emailVerifiedFromToken = null;
        await firebase.Auth.SignOutAsync();
    }

#if ANDROID
    static AndroidActionCodeSettings ToAndroidNative(ActionCodeSettings settings)
    {
        // Url + HandleCodeInApp only. AndroidPackageName / iOSBundleId would
        // request Firebase Dynamic Links and fail with "FDL domain is not configured".
        return AndroidActionCodeSettings.NewBuilder()
            .SetUrl(settings.Url ?? AuthEmailActionSettings.ContinueUrl)
            .SetHandleCodeInApp(settings.HandleCodeInApp)
            .Build();
    }
#endif
}
