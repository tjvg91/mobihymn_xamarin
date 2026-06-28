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
			set => timeStamp = value;
		}

        public string BookmarkGroup { get; set; }

        public string HistoryGroup => GetHistoryGroup(TimeStamp);

        public string DateTimeText
		{
			get
			{
				var localTime = ToLocalTime(TimeStamp);
				var now = DateTime.Now;
                if (now.Date.Equals(localTime.Date))
					return localTime.ToString("HH:mm");
				if (now.Year.Equals(localTime.Year))
					return localTime.ToString("MM-dd");
				return localTime.ToString("yyyy-MM-dd");
            }
		}

        public ShortHymn()
		{
			TimeStamp = DateTime.UtcNow;
			BookmarkGroup = "General";
        }

        static DateTime ToLocalTime(DateTime value) =>
            value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;

        static string GetHistoryGroup(DateTime timestamp)
        {
            var localDate = ToLocalTime(timestamp).Date;
            var today = DateTime.Today;
            var daysAgo = (today - localDate).Days;

            if (daysAgo <= 0)
                return "Today";
            if (daysAgo == 1)
                return "Yesterday";
            if (daysAgo < 7)
                return "Within a week ago";
            if (daysAgo < 14)
                return "Within 2 weeks ago";
            if (localDate.Year == today.Year && localDate.Month == today.Month)
                return "This month";
            if (localDate.Year == today.Year)
                return localDate.ToString("MMMM");
            return localDate.ToString("yyyy");
        }
	}
}
