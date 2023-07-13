using System;

namespace MobiHymn4.Models
{
    public class Hymn
    {
        public string Number { get; set; }
        public string Title { get; set; }
        public string Lyrics { get; set; }
        public string FirstLine { get; set; }
        public string MidiFileName { get; set; }

        public Hymn() { }
        public Hymn(Hymn hymn)
        {
            Number = hymn.Number;
            Title = hymn.Title;
            Lyrics = hymn.Lyrics;
            FirstLine = hymn.FirstLine;
            MidiFileName = hymn.MidiFileName;
        }
    }
}
