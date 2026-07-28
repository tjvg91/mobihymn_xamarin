using System.Threading.Tasks;

namespace MobiHymn4.Services;

/// <summary>Stub used while Google Sign-In is temporarily disabled.</summary>
public sealed class UnavailableGoogleSignInService : IGoogleSignInService
{
    public Task<GoogleSignInResult> SignInAsync() =>
        Task.FromResult(GoogleSignInResult.Failed("Google Sign-In is not available right now."));
}
