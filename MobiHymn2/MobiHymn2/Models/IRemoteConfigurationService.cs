using System.Threading.Tasks;

namespace MobiHymn2.Models
{
    public interface IRemoteConfigurationService
    {
        Task FetchAndActivateAsync();
        Task<TInput> GetAsync<TInput>(string key);
    }
}
