using System.Threading.Tasks;
using Firebase.RemoteConfig;
using MobiHymn2.Models;

namespace MobiHymn2.iOS
{
    public class MyRemoteConfigurationService : IRemoteConfigurationService
    {
        public MyRemoteConfigurationService()
        {
            RemoteConfig.SharedInstance.SetDefaults("RemoteConfigDefaults");
            RemoteConfig.SharedInstance.ConfigSettings = new RemoteConfigSettings();
        }

        public async Task FetchAndActivateAsync()
        {
            var status = await RemoteConfig.SharedInstance.FetchAsync(0);
            if (status == RemoteConfigFetchStatus.Success)
            {
                await RemoteConfig.SharedInstance.FetchAndActivateAsync();
            }
        }

        public async Task<TInput> GetAsync<TInput>(string key)
        {
            var settings = RemoteConfig.SharedInstance[key].StringValue;
            return await Task.FromResult(Newtonsoft.Json.JsonConvert.DeserializeObject<TInput>(settings));
        }
    }
}