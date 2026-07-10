#if ANDROID
using Android.App;
using Android.Content;
using Android.Gms.Auth.Api.SignIn;
using Android.Gms.Common.Apis;
using Firebase.Auth;
using Java.Lang;
using MobiHymn4.Services;
using Microsoft.Maui.ApplicationModel;
using SignInResult = MobiHymn4.Services.GoogleSignInResult;

namespace MobiHymn4.Platforms.Android;

public sealed class GoogleSignInService : IGoogleSignInService
{
    const int SignInRequestCode = 9101;
    const string WebClientId = "525477034225-6kjt4atrp816c7c9dbe5gs2fklka4fen.apps.googleusercontent.com";

    static TaskCompletionSource<SignInResult> pendingSignIn;

    public static void Initialize()
    {
        if (MainActivity.Instance == null)
            return;

        MainActivity.Instance.ActivityResult += OnActivityResult;
    }

    public async Task<SignInResult> SignInAsync()
    {
        var activity = Platform.CurrentActivity ?? MainActivity.Instance;
        if (activity == null)
            return SignInResult.Failed("Unable to start Google Sign-In.");

        pendingSignIn?.TrySetCanceled();
        pendingSignIn = new TaskCompletionSource<SignInResult>();

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var options = new GoogleSignInOptions.Builder(GoogleSignInOptions.DefaultSignIn)
                .RequestIdToken(WebClientId)
                .RequestEmail()
                .Build();

            var client = GoogleSignIn.GetClient(activity, options);
            activity.StartActivityForResult(client.SignInIntent, SignInRequestCode);
        });

        return await pendingSignIn.Task;
    }

    static async void OnActivityResult(int requestCode, Result resultCode, Intent data)
    {
        if (requestCode != SignInRequestCode || pendingSignIn == null)
            return;

        try
        {
            var task = GoogleSignIn.GetSignedInAccountFromIntent(data);
            var account = task.GetResult(Class.FromType(typeof(ApiException))) as GoogleSignInAccount;
            if (account == null || string.IsNullOrWhiteSpace(account.IdToken))
            {
                pendingSignIn.TrySetResult(SignInResult.Failed("Google Sign-In was cancelled."));
                return;
            }

            var credential = GoogleAuthProvider.GetCredential(account.IdToken, null);
            var authResult = await FirebaseAuth.Instance.SignInWithCredentialAsync(credential);
            var user = authResult.User;
            pendingSignIn.TrySetResult(SignInResult.Ok(
                account.IdToken,
                account.IdToken,
                user?.Email ?? account.Email,
                user?.DisplayName ?? account.DisplayName));
        }
        catch (System.Exception ex)
        {
            pendingSignIn.TrySetResult(SignInResult.Failed(FriendlyError(ex.Message)));
        }
    }

    // Google Sign-In ApiException messages arrive as "STATUS_CODE:" or "STATUS_CODE: detail".
    // Map the well-known codes to readable strings.
    static string FriendlyError(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Google Sign-In failed.";

        var code = raw.Split(':')[0].Trim();
        return code switch
        {
            "10"    => "Google Sign-In is not configured for this build. The app's signing certificate must be registered in Firebase Console.",
            "7"     => "Network error during sign-in. Check your connection and try again.",
            "12500" => "Google Sign-In required but not available.",
            "12501" => "Google Sign-In was cancelled.",
            "12502" => "Google Sign-In is already in progress.",
            _       => raw
        };
    }
}
#endif
