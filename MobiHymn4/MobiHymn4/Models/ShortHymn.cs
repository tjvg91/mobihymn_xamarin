using System;
namespace MobiHymn4.Models
{
	public class ShortHymn
	{
        public string Line { get; set; }
        public string Number { get; set; }
        public string NumberText { get => "Hymn #" + Number; }
		public DateTime TimeStamp { get; set; }
        public string BookmarkGroup { get; set; }
        public string DateTimeText
		{
			get
			{
				var offset = DateTime.Now - DateTime.UtcNow;
				var offsetedTime = TimeStamp.AddHours(offset.Hours + 1);
				var offsetedNow = DateTime.Now;//.AddHours(offset.Hours + 1);
                if (offsetedNow.Date.Equals(offsetedTime.Date))
					return offsetedTime.ToString("HH:mm");
				else if (offsetedNow.Year.Equals(offsetedTime.Date.Year))
					return offsetedTime.ToString("MM-dd");
				return offsetedTime.ToString("yyyy-MM-dd");
            }
		}
        public ShortHymn()
		{
			TimeStamp = DateTime.UtcNow;
			BookmarkGroup = "General";
			
		}
	}
}

