using System;
using System.Collections.Generic;

namespace MobiHymn2.Models
{
	public class HymnList: List<Hymn>
	{
        public HymnList() : base() { }
        public HymnList(IEnumerable	<Hymn> hymns) : base()
		{
			this.AddRange(hymns);
		}

		public Hymn this[string number]
		{
			get => Find(x => x.Number == number);
		}
	}
}

