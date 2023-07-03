using System;
using System.Collections.Generic;

namespace MobiHymn2.Models
{
	public class ResyncHeader
    {
        public string Version { get; set; }
		public List<ResyncDetail> Details { get; set; }
		public ResyncHeader()
		{

		}
	}
}

