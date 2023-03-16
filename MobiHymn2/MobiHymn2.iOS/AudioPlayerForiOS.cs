using System;
using System.Runtime.InteropServices;
using AudioToolbox;
using AudioUnit;
using AVFoundation;
using CoreMidi;
using Foundation;
using GameplayKit;
using MobiHymn2.Models;
using Plugin.AudioRecorder;

[assembly: Xamarin.Forms.Dependency(typeof(MobiHymn2.iOS.AudioPlayerForiOS))]
namespace MobiHymn2.iOS
{
	public class AudioPlayerForiOS : IPlayService
    {
        MidiClient midiClient;
        MusicPlayer player;
        AUGraph graph;
        AUAudioUnit samplerUnit;

        public AudioPlayerForiOS()
		{
		}

        public void Init(string fileName)
        {
            string filePath = NSBundle.MainBundle.PathForResource("midi/" + fileName, null);

            midiClient = new MidiClient("Midi Client");

            MidiError stat;
            var midiEndpoint = midiClient.CreateVirtualDestination("VEnd", out stat);

            var s = new MusicSequence();
            s.LoadFile(NSUrl.FromFilename(filePath), MusicSequenceFileTypeID.Midi);
            s.SetMidiEndpoint(midiEndpoint);

            player = new MusicPlayer();

            player.MusicSequence = s;
            player.Start();
        }

        public double Duration => 0.0;

        public void Play()
        {
            player.Start();
        }

        public void Pause()
        {
            player.Stop();
        }
    }
}

