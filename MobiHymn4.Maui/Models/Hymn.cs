using System;
using System.Collections.Generic;
using System.Linq;

namespace MobiHymn4.Models
{
    public class Hymn
    {
        public string Number { get; set; }
        /// <summary>Display label from the hymn number (e.g. "805", "3 (2nd tune)").</summary>
        public string Title { get; set; }
        /// <summary>Song title from the catalog when available (e.g. "O perfect Love").</summary>
        public string Name { get; set; }
        public string Lyrics { get; set; }
        public string FirstLine { get; set; }
        public string MidiFileName { get; set; }
        public string Author { get; set; }
        public string Metre { get; set; }
        public string Tune { get; set; }
        public string TuneComposer { get; set; }
        public string TuneKey { get; set; }
        public string[] Verses { get; set; } = Array.Empty<string>();
        public string[] Tags { get; set; } = Array.Empty<string>();
        /// <summary>Legacy single-reference field retained for older saved hymn libraries.</summary>
        public string VerseRef { get; set; }
        public string Year { get; set; }
        public string Remark { get; set; }

        public Hymn() { }
        public Hymn(Hymn hymn)
        {
            Number = hymn.Number;
            Title = hymn.Title;
            Name = hymn.Name;
            Lyrics = hymn.Lyrics;
            FirstLine = hymn.FirstLine;
            MidiFileName = hymn.MidiFileName;
            Author = hymn.Author;
            Metre = hymn.Metre;
            Tune = hymn.Tune;
            TuneComposer = hymn.TuneComposer;
            TuneKey = hymn.TuneKey;
            Verses = hymn.Verses?.ToArray() ?? Array.Empty<string>();
            Tags = hymn.Tags?.ToArray() ?? Array.Empty<string>();
            VerseRef = hymn.VerseRef;
            Year = hymn.Year;
            Remark = hymn.Remark;
        }

        public IEnumerable<string> GetVerseReferences()
        {
            if (Verses?.Length > 0)
                return Verses.Where(value => !string.IsNullOrWhiteSpace(value));

            return string.IsNullOrWhiteSpace(VerseRef)
                ? Enumerable.Empty<string>()
                : new[] { VerseRef };
        }
    }
}
