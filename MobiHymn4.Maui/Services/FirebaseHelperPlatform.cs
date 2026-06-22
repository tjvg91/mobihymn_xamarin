using MobiHymn4.Models;

namespace MobiHymn4.Services;

public class FirebaseHelperPlatform : IFirebaseHelper
{
    public Task LoginWithEmailPassword(string email, string password)
    {
        // TODO: Wire Plugin.Firebase.Auth after google-services.json is configured.
        return Task.CompletedTask;
    }
}
