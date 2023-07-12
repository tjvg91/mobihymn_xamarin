using System;
using MobiHymn4.Utils;

namespace MobiHymn4.Models
{
	public class ResyncDetail
	{
		public CRUD Mode { get; set; }
		public string Number { get; set; }
		public ResyncType Type { get; set; }
		public ResyncDetail()
		{
		}
	}
}

