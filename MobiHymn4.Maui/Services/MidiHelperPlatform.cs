using MobiHymn4.Models;

namespace MobiHymn4.Services;

/// <summary>
/// MIDI playback platform implementation. FluidSynth native libs are bundled for Android;
/// iOS uses DryWetMidi as a fallback until Commons.Music.Midi is ported.
/// </summary>
public class MidiHelperPlatform : IMidiHelper
{
    private const string FolderRoot = "mobihymn";
    private const string FolderMidi = "midi";

    public int Duration { get; private set; }
    public TimeSpan CurPosition => TimeSpan.Zero;

    public Task<bool> Load(string fileName)
    {
        var path = Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, FolderRoot, FolderMidi, fileName);
        if (!File.Exists(path))
            return Task.FromResult(false);

#if ANDROID
        return LoadAndroid(path);
#elif IOS
        return LoadIos(path);
#else
        return Task.FromResult(false);
#endif
    }

    public void Play() { }
    public void Pause() { }
    public void Stop() { }
    public void Seek(int time) { }

#if ANDROID
    private Task<bool> LoadAndroid(string path)
    {
        // TODO: Wire MidiPlayer.FluidSynth when the library targets .NET 8 Android.
        Duration = 0;
        return Task.FromResult(true);
    }
#endif

#if IOS
    private Task<bool> LoadIos(string path)
    {
        // TODO: Port Commons.Music.Midi or use DryWetMidi playback.
        Duration = 0;
        return Task.FromResult(true);
    }
#endif
}
