using System;
using System.IO;
using System.Threading.Tasks;
using Commons.Music.Midi;
using MobiHymn4.Models;
using PCLStorage;
using Xamarin.Forms;
using Commons.Music.Midi.CoreMidiApi;
using System.Diagnostics;

[assembly: Dependency(typeof(MobiHymn4.iOS.MidiHelper))]
namespace MobiHymn4.iOS
{
	public class MidiHelper : IMidiHelper
	{
        private string folderRootName = "mobihymn";
        private string folderMidiName = "midi";

        private MidiPlayer player;

        public int Duration => player.GetTotalPlayTimeMilliseconds();

        public TimeSpan CurPosition => player.PositionInTime;

        public MidiHelper()
		{
		}

        public async Task<bool> Load(string fileName)
        {
            try
            {

                player = new MidiPlayer(MidiMusic.Read(await GetMidiStream(fileName)));
                player.EventReceived += Player_EventReceived;

                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        private void Player_EventReceived(MidiEvent m)
        {
            Debug.WriteLine($"MIDI Channel: {m.Channel}");
            Debug.WriteLine($"MIDI Event Type: {m.EventType}");
            Debug.WriteLine($"MIDI Value: {m.Value}");
        }

        public void Pause()
        {
            player.Pause();
        }

        public void Play()
        {
            player.Play();
        }

        public void Stop()
        {
            player.Stop();
        }

        public void Seek(int ticks)
        {
            player.Seek(ticks);
        }


        public async Task<string> GetMidiFile(string fileName)
        {
            string fullPath = $"{folderRootName}/{folderMidiName}/{fileName}";
            IFolder rootFolder = FileSystem.Current.LocalStorage;
            if (await rootFolder.CheckExistsAsync(fullPath) == ExistenceCheckResult.FileExists)
                return fullPath;
            return "";
        }
        public async Task<Stream> GetMidiStream(string fileName)
        {
            try
            {
                string fullPath = $"{folderRootName}/{folderMidiName}/{fileName}";
                IFolder rootFolder = FileSystem.Current.LocalStorage;
                if (await rootFolder.CheckExistsAsync(fullPath) == ExistenceCheckResult.FileExists)
                {
                    Stream fileStream = File.OpenRead($"{rootFolder.Path}/{fullPath}");
                    return fileStream;
                }
            }
            catch (Exception ex)
            {

            }

            return null;
        }
    }
}

