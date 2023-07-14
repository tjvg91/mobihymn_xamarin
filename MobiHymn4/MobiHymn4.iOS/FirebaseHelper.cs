using System;
using Xamarin.Forms;
using MobiHymn4.Models;
using System.Threading.Tasks;
using Firebase.Auth;

[assembly: Dependency(typeof(MobiHymn4.iOS.FirebaseHelper))]
namespace MobiHymn4.iOS
{
    public class FirebaseHelper : IFirebaseHelper
    {
        string token;

        public async Task LoginWithEmailPassword(string email, string password)
        {
            try
            {
                var user = await Auth.DefaultInstance.SignInWithPasswordAsync(email, password);
                //User = user.User;
                token = await user.User.GetIdTokenAsync();
            }
            catch (Exception)
            {
                token = "";
            }
        }
    }
}

