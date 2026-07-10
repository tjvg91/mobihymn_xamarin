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
    Task SendEmailVerificationAsync();
    Task RefreshEmailVerificationStatusAsync();
    Task SignOutAsync();
}
