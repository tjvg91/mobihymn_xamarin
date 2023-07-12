using System;
namespace MobiHymn4.Models
{
	public class FontSettings
	{
        public string Name { get; set; }
        public double CharacterSpacing { get; set; }

        public FontSettings()
        {
            CharacterSpacing = 0;
        }
    }
}

