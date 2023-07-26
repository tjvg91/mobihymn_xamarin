using System;
namespace MobiHymn4.Models
{
	public class ShortHymn
	{
        public string Line { get; set; }
        public string Number { get; set; }
        public string NumberText { get => "Hymn #" + Number; }

		private DateTime timeStamp;
		public DateTime TimeStamp
        {
			get => timeStamp;
			set
			{
				timeStamp = value;

                var offset = DateTime.Now - DateTime.UtcNow;
                var offsetedTime = value.AddHours(offset.Hours + 1);
                var offsetedToday = DateTime.Today;

                HistoryGroup = offsetedTime.Date.Equals(offsetedToday.Date) ? "Today" :
                            offsetedTime.Date - offsetedToday < TimeSpan.FromDays(7) ? "Within a week ago" :
                            offsetedTime.Date - offsetedToday < TimeSpan.FromDays(14) ? "Within 2 weeks ago" :
                            offsetedTime.Year - offsetedToday.Year > 0 ? offsetedTime.ToString("yyyy") :
                            offsetedTime.Month - offsetedToday.Month > 0 ? offsetedTime.ToString("MMMM") :
                            offsetedTime.Month - offsetedToday.Month == 0 ? "This month" :
                            "";
            }
		}
        public string BookmarkGroup { get; set; }
        public string HistoryGroup { get; set; }
        public string DateTimeText
		{
			get
			{
				var offset = DateTime.Now - DateTime.UtcNow;
				var offsetedTime = TimeStamp.AddHours(offset.Hours + 1);
				var offsetedNow = DateTime.Now;
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

