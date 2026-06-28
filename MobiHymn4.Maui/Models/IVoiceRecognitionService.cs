namespace MobiHymn4.Models;

public interface IVoiceRecognitionService
{
    Task<string> ListenOnceAsync(CancellationToken cancellationToken = default);
}
