namespace MobiHymn4.Models
{
    /// <summary>
    /// Controls the OS-level foreground-service notification shown during hymn downloads.
    /// Implementations subscribe to Globals events and drive the native notification channel.
    /// </summary>
    public interface IDownloadNotificationService
    {
        void Start(string title = "Downloading hymns…");
        void UpdateProgress(string message, int current = -1, int total = -1);
        void Stop();
    }
}
