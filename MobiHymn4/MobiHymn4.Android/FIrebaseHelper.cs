using System;
using MobiHymn4.Models;

using Firebase.Auth;
using Xamarin.Forms;

[assembly: Dependency(typeof(MobiHymn4.Droid.FirebaseHelper))]
namespace MobiHymn4.Droid
{
    public class FirebaseHelper : IFirebaseHelper
	{
        string token;

        public FirebaseHelper()
		{
		}

        public async System.Threading.Tasks.Task LoginWithEmailPassword(string email, string password)
        {
            try
            {
                var user = await FirebaseAuth.Instance.SignInWithEmailAndPasswordAsync(email, password);
                token = user.User.GetIdToken(false).ToString();
            }
            catch (Exception ex)
            {
                token =  "";
            }
        }
    }
}

