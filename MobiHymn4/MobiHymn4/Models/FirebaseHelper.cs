using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Database;
using MobiHymn4.Utils;

namespace MobiHymn4.Models
{
	public class FirebaseHelper
    {
		public static string FirebaseClient = "https://mobihymn.firebaseio.com";
		public static string FirebaseSecret = "resiwCezcusOmAtXQggJWbbvQfqG9DMS6sYDNkYJ";

		private const string ACTIVE_SYNC_VERSION_KEY = "ActiveSyncVersion";

        private FirebaseClient fc;

        private static FirebaseHelper instance = null;
        public static FirebaseHelper Instance
        {
            get
            {
                instance ??= new FirebaseHelper();
                return instance;
            }
        }

        public FirebaseHelper()
		{
			fc = new FirebaseClient(FirebaseClient, new FirebaseOptions
			{
				AuthTokenAsyncFactory = () => Task.FromResult(FirebaseSecret),
			});
		}

		public async Task<int> RetrieveActiveSyncVersion()
		{
			try
			{
				var ret = (int)(await fc.Child(ACTIVE_SYNC_VERSION_KEY).OnceSingleAsync<int>());
				return ret;
			}
			catch (Exception ex)
			{
				return -1;
			}
		}

		public async Task<List<ResyncDetail>> RetrieveSyncChangesFrom(int version)
		{
			var ret = new List<ResyncDetail>();
			var currentVersion = await RetrieveActiveSyncVersion();
			try
			{

				for (var i = version + 1; i <= currentVersion; i++)
				{
					var changes = (await fc.Child($"v{i}").OnceAsListAsync<ResyncDetail>())
									.Select(item => new ResyncDetail
									{
										Mode = item.Object.Mode,
										Number = item.Object.Number,
										Type = item.Object.Type
									}).ToList();
					ret.AddRange(changes);
				}
			}
			catch (Exception ex)
			{

			}
			return ret;
		}
	}
}

