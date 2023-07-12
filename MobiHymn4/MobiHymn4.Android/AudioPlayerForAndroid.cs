using System;
using Android.Content.Res;
using Android.Media;
using MobiHymn4.Models;

[assembly: Xamarin.Forms.Dependency(typeof(MobiHymn4.Droid.AudioPlayerForAndroid))]
namespace MobiHymn4.Droid
{
	public class AudioPlayerForAndroid : IPlayService
    {
        private MediaPlayer mediaPlayer;
        public AudioPlayerForAndroid()
        {

        }

        public double Duration => mediaPlayer.Duration / 1000.0;

        public void Init(string fileName)
        {
            mediaPlayer = new MediaPlayer();

            // Make sure this file is placed in the Android project's Assets folder with build action AndroidAsset.
            AssetFileDescriptor descriptor = Android.App.Application.Context.Assets.OpenFd(fileName);
            mediaPlayer.SetDataSource(descriptor.FileDescriptor, descriptor.StartOffset, descriptor.Length);
            mediaPlayer.Prepare();
        }

        public void Pause()
        {
            mediaPlayer.Pause();
        }

        public void Play()
        {
            mediaPlayer.Start();
        }
    }
}

