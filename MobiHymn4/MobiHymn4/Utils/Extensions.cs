using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MvvmHelpers;
using Newtonsoft.Json.Linq;
using Xamarin.Forms;
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
            var keys = jObject.Properties().Select(p => p.Name);
            var hasRGBA = (from x in keys
                           where new Regex("[rgba]", RegexOptions.IgnoreCase).IsMatch(x)
                           select x).Any();
            if (hasRGBA)
            {
                return Color.FromRgba(
                    double.Parse(jObject["R"] + ""),
                    double.Parse(jObject["G"] + ""),
                    double.Parse(jObject["B"] + ""),
                    double.Parse(jObject["A"] + "")
                );
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

