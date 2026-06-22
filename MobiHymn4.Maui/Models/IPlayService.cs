using System;
using System.Threading.Tasks;

namespace MobiHymn4.Models
{
    public interface IPlayService
    {
        double Duration { get; }
        double Position { get; }
        bool IsPlaying { get; }
        bool IsBuffering { get; }

        event EventHandler PlaybackEnded;
        event EventHandler PositionChanged;
        event EventHandler BufferingChanged;

        Task<bool> LoadAsync(string url);
        void Play();
        void Pause();
        void Stop();
        void Seek(double seconds);
    }
}
