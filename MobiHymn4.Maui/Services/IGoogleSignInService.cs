using System;
using System.Threading.Tasks;

namespace MobiHymn4.Services;

public interface IGoogleSignInService
{
    Task<GoogleSignInResult> SignInAsync();
}

public sealed class GoogleSignInResult
{
    public bool Success { get; init; }
    public string IdToken { get; init; }
    public string AccessToken { get; init; }
    public string Email { get; init; }
    public string DisplayName { get; init; }
    public string ErrorMessage { get; init; }

    public static GoogleSignInResult Failed(string message) =>
        new() { Success = false, ErrorMessage = message };

    public static GoogleSignInResult Ok(string idToken, string accessToken, string email, string displayName) =>
        new()
        {
            Success = true,
            IdToken = idToken,
            AccessToken = accessToken,
            Email = email,
            DisplayName = displayName,
        };
}
