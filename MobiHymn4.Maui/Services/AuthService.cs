using System;
using System.Threading.Tasks;
using Plugin.Firebase.Auth;
using MobiHymn4.Services;

namespace MobiHymn4.Services;

public sealed class AuthService : IAuthService
{
    readonly IFirebaseFirestoreAccessor firebase;
    readonly IGoogleSignInService googleSignIn;
    bool signedInWithGoogle;

    public AuthService(IFirebaseFirestoreAccessor firebase, IGoogleSignInService googleSignIn)
    {
        this.firebase = firebase;
        this.googleSignIn = googleSignIn;
        firebase.Auth.AddAuthStateListener(_ => AuthStateChanged?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler AuthStateChanged;

    IFirebaseUser User => firebase.Auth.CurrentUser;

    public bool IsSignedIn => User != null;
    public string CurrentUserId => User?.Uid ?? string.Empty;
    public string CurrentEmail => User?.Email ?? string.Empty;
    public bool IsEmailVerified => signedInWithGoogle || (User?.IsEmailVerified ?? false);
    public bool SignedInWithGoogle => signedInWithGoogle;

    public async Task SignUpWithEmailAsync(string email, string password)
    {
        signedInWithGoogle = false;
        await firebase.Auth.CreateUserAsync(email, password);
        await SendEmailVerificationAsync();
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SignInWithEmailAsync(string email, string password)
    {
        signedInWithGoogle = false;
        await firebase.Auth.SignInWithEmailAndPasswordAsync(email, password, false);
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SignInWithGoogleAsync()
    {
        var result = await googleSignIn.SignInAsync();
        if (!result.Success)
            throw new InvalidOperationException(result.ErrorMessage ?? "Google sign-in failed.");

        signedInWithGoogle = true;
        // Platform service signs into Firebase directly when possible.
        if (!IsSignedIn)
            throw new InvalidOperationException("Google sign-in did not produce a Firebase session.");

        AuthStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SendEmailVerificationAsync()
    {
        if (User == null || signedInWithGoogle)
            return;

        await User.SendEmailVerificationAsync(null);
    }

    public async Task RefreshEmailVerificationStatusAsync()
    {
        if (User == null || signedInWithGoogle)
            return;

        await User.GetIdTokenResultAsync(true);
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SignOutAsync()
    {
        signedInWithGoogle = false;
        await firebase.Auth.SignOutAsync();
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
    }
}
