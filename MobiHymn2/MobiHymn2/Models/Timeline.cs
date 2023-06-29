using System;
using MvvmHelpers;

namespace MobiHymn2.Models
{
	public class Timeline
	{
		public string Header
		{
			get; set;
		}
		public ObservableRangeCollection<string> Details
		{
			get; set;
		}
        public double Height
        {
            get; set;
        }
        public Timeline()
		{
			Details = new ObservableRangeCollection<string>();
		}
	}
}

