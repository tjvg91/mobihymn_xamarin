using System;
using System.Collections.Generic;

namespace MobiHymn4.Models
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
			set
			{
				var index = FindIndex(x => x.Number == number);
				if(index > -1) this[index] = value;
			}

        }
	}
}

