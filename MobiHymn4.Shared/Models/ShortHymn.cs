namespace MobiHymn4.Shared.Models;

public class ShortHymn
{
    public string Line { get; set; } = "";
    public string Number { get; set; } = "";
    public string NumberText => "Hymn #" + Number;
    public string NumberBadge => "#" + Number;
    public string? Reason { get; set; }
    public bool HasReason => !string.IsNullOrWhiteSpace(Reason);
    public DateTime TimeStamp { get; set; } = DateTime.UtcNow;
    public string BookmarkGroup { get; set; } = "General";

    public string HistoryGroup
    {
        get
        {
            var localDate = ToLocalTime(TimeStamp).Date;
            var daysAgo = (DateTime.Today - localDate).Days;
            if (daysAgo <= 0) return "Today";
            if (daysAgo == 1) return "Yesterday";
            if (daysAgo < 7) return "Within a week ago";
            if (daysAgo < 14) return "Within 2 weeks ago";
            if (localDate.Year == DateTime.Today.Year && localDate.Month == DateTime.Today.Month)
                return "This month";
            if (localDate.Year == DateTime.Today.Year)
                return localDate.ToString("MMMM");
            return localDate.ToString("yyyy");
        }
    }

    /// <summary>MAUI DateTimeText — today 12-hr time, this year MM-dd, else yyyy-MM-dd.</summary>
    public string DateTimeText
    {
        get
        {
            var local = ToLocalTime(TimeStamp);
            var now = DateTime.Now;
            if (now.Date.Equals(local.Date))
                return local.ToString("h:mm tt");
            if (now.Year.Equals(local.Year))
                return local.ToString("MM-dd");
            return local.ToString("yyyy-MM-dd");
        }
    }

    static DateTime ToLocalTime(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
}
