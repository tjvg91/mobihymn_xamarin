using System.Threading.Tasks;
using Firebase.RemoteConfig;
using MobiHymn2.Models;

namespace MobiHymn2.Droid
{
    public class MyRemoteConfigurationService : IRemoteConfigurationService
    {
        public MyRemoteConfigurationService()
        {
            FirebaseRemoteConfigSettings configSettings = new FirebaseRemoteConfigSettings.Builder()
                .Build();
            FirebaseRemoteConfig.Instance.SetConfigSettingsAsync(configSettings);
            FirebaseRemoteConfig.Instance.SetDefaultsAsync(Resource.Xml.RemoteConfigDefaults);
        }

        public async Task FetchAndActivateAsync()
        {
            await FirebaseRemoteConfig.Instance.FetchAsync(0);
            FirebaseRemoteConfig.Instance.Activate();
        }

        public async Task<TInput> GetAsync<TInput>(string key)
        {
            var settings = FirebaseRemoteConfig.Instance.GetString(key);
            return await Task.FromResult(Newtonsoft.Json.JsonConvert.DeserializeObject<TInput>(settings));
        }
    }
}