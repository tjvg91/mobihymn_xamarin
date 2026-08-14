using System;
using System.Threading.Tasks;
using MobiHymn4.Models;

namespace MobiHymn4.Services;

public interface IAuthService
{
    event EventHandler AuthStateChanged;

    bool IsSignedIn { get; }
    string CurrentUserId { get; }
    string CurrentEmail { get; }
    bool IsEmailVerified { get; }
    bool SignedInWithGoogle { get; }

    Task SignUpWithEmailAsync(string email, string password);
    Task SignInWithEmailAsync(string email, string password);
    Task SignInWithGoogleAsync();
    Task SendPasswordResetEmailAsync(string email);
    Task UpdatePasswordAsync(string newPassword);
    Task SendEmailVerificationAsync();
    Task RefreshEmailVerificationStatusAsync();
    Task SignOutAsync();

    /// <summary>Raw Firebase Auth ID token JWT for authenticating HTTPS Callable
    /// Cloud Function requests. Returns null when signed out.</summary>
    Task<string> GetIdTokenAsync(bool forceRefresh = false);
}
