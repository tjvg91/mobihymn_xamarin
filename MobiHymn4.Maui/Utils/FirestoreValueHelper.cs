using System;
using System.Collections.Generic;

namespace MobiHymn4.Utils;

public static class FirestoreValueHelper
{
    public static DateTime ToDateTime(object value)
    {
        if (value == null)
            return default;

        if (value is DateTime dt)
            return dt;

        if (value is DateTimeOffset dto)
            return dto.UtcDateTime;

        var type = value.GetType();
        var secondsProp = type.GetProperty("Seconds");
        var nanosProp = type.GetProperty("Nanoseconds");
        if (secondsProp?.GetValue(value) is long seconds)
        {
            var nanos = nanosProp?.GetValue(value) is int n ? n : 0;
            return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.AddTicks(nanos / 100);
        }

        if (DateTime.TryParse(value.ToString(), out var parsed))
            return parsed;

        return default;
    }

    public static List<object> ToObjectList(object value)
    {
        if (value is IEnumerable<object> objects)
            return new List<object>(objects);

        return new List<object>();
    }

    public static Dictionary<string, object> ToDictionary(object value) =>
        value as Dictionary<string, object> ?? new Dictionary<string, object>();
}
