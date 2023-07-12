using System;
using System.Threading.Tasks;

namespace MobiHymn4.Services
{
	public interface IAppCenterService
	{
        public Task InitiateAsync();
        public void LogInfo(string title);
        public void LogInfo(string title, string valueName, string value);
        public void LogInfo(string title, string valueName, string oldValue, string newValue);
        public void LogError(Exception exception);
    }
}

