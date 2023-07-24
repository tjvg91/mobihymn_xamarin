using System;
using System.IO;
using System.Threading.Tasks;

namespace MobiHymn4.Models
{
	public interface IMidiHelper
    {
        public int Duration { get; }
		public TimeSpan CurPosition { get; } 

        public Task<bool> Load(string fileName);
		public void Play();
		public void Pause();
		public void Stop();
		public void Seek(int time);
    }
}

