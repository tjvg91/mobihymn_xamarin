using System.Threading.Tasks;
using MobiHymn4.Services;

namespace MobiHymn4.Platforms.iOS;

public sealed class GoogleSignInService : IGoogleSignInService
{
    public Task<GoogleSignInResult> SignInAsync() =>
        Task.FromResult(GoogleSignInResult.Failed("Google Sign-In on iOS will be configured in a follow-up build."));
}
