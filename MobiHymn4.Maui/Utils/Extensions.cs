using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MvvmHelpers;
using Newtonsoft.Json.Linq;
using Microsoft.Maui.Controls;
using MobiHymn4.Models;
using System.Threading.Tasks;
using System.Threading;

namespace MobiHymn4.Utils
{
    public static class Extensions
    {
        public static ObservableCollection<T> ToObservableCollection<T>(this IEnumerable<T> col)
        {
            return new ObservableCollection<T>(col);
        }
        public static ObservableRangeCollection<T> ToObservableRangeCollection<T>(this IEnumerable<T> col)
        {
            return new ObservableRangeCollection<T>(col);
        }

        public static HymnList ToHymnList(this IEnumerable<Hymn> list)
        {
            return new HymnList(list);
        }

        public static string StripPunctuation(this string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (!char.IsPunctuation(c))
                    sb.Append(c);
            }
            return sb.ToString();
        }

        public static string ToSentenceCase(this string str)
        {
            return Regex.Replace(str, "[a-z][A-Z]", m => $"{m.Value[0]} {char.ToLower(m.Value[1])}");
        }

        public static Color? ToColor(this JObject jObject)
        {
            double ReadChannel(string upper, string lower)
            {
                if (jObject[upper] != null)
                    return double.Parse(jObject[upper] + "");
                if (jObject[lower] != null)
                    return double.Parse(jObject[lower] + "");
                return -1;
            }

            var r = ReadChannel("R", "r");
            var g = ReadChannel("G", "g");
            var b = ReadChannel("B", "b");
            var a = ReadChannel("A", "a");

            if (r >= 0 && g >= 0 && b >= 0)
            {
                return Color.FromRgba(
                    r > 1 ? r / 255d : r,
                    g > 1 ? g / 255d : g,
                    b > 1 ? b / 255d : b,
                    a >= 0 ? (a > 1 ? a / 255d : a) : 1);
            }

            return null;
        }

        public static string ToMinSec(this double seconds)
        {
            TimeSpan timeSpan = TimeSpan.FromSeconds(seconds);
            return string.Format("{0:D1}:{1:D2}", timeSpan.Minutes, timeSpan.Seconds);
        }

        public static string ToTitle(this string number)
        {
            var tune = new Regex("[fst]$").Match(number).Value;
            return tune == "f" ? number.Replace("f", " (4th tune)") :
                    tune == "s" ? number.Replace("s", " (2nd tune)") : number.Replace("t", " (3rd tune)");
        }

        public static async Task ForEachAsync<T>(this IEnumerable<T> list, int dop, Func<T, int, Task> body)
        {
            using var semaphore = new SemaphoreSlim(initialCount: dop, maxCount: dop);
            var tasks = list.Select(async (item, index) =>
            {
                await semaphore.WaitAsync();
                try
                {
                    await body(item, index);
                }
                finally
                {
                    semaphore.Release();
                }
            });
            await Task.WhenAll(tasks);
        }

        public static string Capitalize(this string str)
        {
            return char.ToUpper(str[0]) + str.Substring(1);
        }
    }
}

