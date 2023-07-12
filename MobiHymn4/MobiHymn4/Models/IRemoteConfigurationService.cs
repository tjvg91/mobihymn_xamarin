using System.Threading.Tasks;

namespace MobiHymn4.Models
{
    public interface IRemoteConfigurationService
    {
        Task FetchAndActivateAsync();
        Task<TInput> GetAsync<TInput>(string key);
    }
}
