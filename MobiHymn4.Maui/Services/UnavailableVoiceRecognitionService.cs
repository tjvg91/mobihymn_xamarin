using MobiHymn4.Models;

namespace MobiHymn4.Services;

public class UnavailableVoiceRecognitionService : IVoiceRecognitionService
{
    public Task<string> ListenOnceAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<string>(new NotSupportedException("Voice recognition is not available on this platform yet."));
}
