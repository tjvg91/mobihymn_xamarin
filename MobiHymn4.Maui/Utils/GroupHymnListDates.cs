using System;
using System.Globalization;

namespace MobiHymn4.Utils;

public static class GroupHymnListDates
{
    public const string NameFormat = "MMM d, yyyy";
    public const string KeyFormat = "yyyy-MM-dd";

    public static string FormatName(DateTime date) =>
        date.Date.ToString(NameFormat, CultureInfo.InvariantCulture);

    public static string ToDateKey(DateTime date) =>
        date.Date.ToString(KeyFormat, CultureInfo.InvariantCulture);

    public static bool TryGetScheduledDate(string listId, string name, out DateTime date)
    {
        if (DateTime.TryParseExact(
                listId,
                KeyFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date))
            return true;

        if (!string.IsNullOrWhiteSpace(name)
            && DateTime.TryParseExact(
                name.Trim(),
                NameFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date))
            return true;

        date = default;
        return false;
    }
}
