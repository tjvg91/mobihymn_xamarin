using System.Threading.Tasks;
using MobiHymn4.Services;

namespace MobiHymn4.Platforms;

public sealed class GoogleSignInService : IGoogleSignInService
{
    public Task<GoogleSignInResult> SignInAsync() =>
        Task.FromResult(GoogleSignInResult.Failed("Google Sign-In is not available on this platform."));
}
