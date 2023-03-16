using System;
namespace MobiHymn2.Models
{
	public interface IPlayService
	{
        public void Init(string fileName);
        public double Duration { get; }
        public void Play();
        public void Pause();

    }
}

