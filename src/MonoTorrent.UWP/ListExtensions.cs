using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MonoTorrent.UWP
{
    public static class ListExtensions
    {
        public static void ForEach<T>(this IEnumerable<T> enumeration, Action<T> action)
        {
            var enumerator = enumeration.GetEnumerator();
            while (enumerator.MoveNext())
            {
                action(enumerator.Current);
            }
        }

        public static string ToCsvString<T>(this IEnumerable<T> enumeration)
        {
            return String.Join(",", new List<T>(enumeration).ToArray());
        }
        public static string ToLineBreakString<T>(this IEnumerable<T> enumeration)
        {
            return String.Join("\r\n", new List<T>(enumeration).ToArray());
        }

        public static IEnumerable<T> ConvertAll<U, T>(this IEnumerable<U> list, Func<U,T> func)
        {
            return list.Select(func);
        }

        public static IEnumerable<T> OrderByAlphaNumeric<T>(this IEnumerable<T> source, Func<T, string> selector)
        {
            int max = source
                .SelectMany(i => Regex.Matches(selector(i), @"\d+").Cast<Match>().Select(m => (int?)m.Value.Length))
                .Max() ?? 0;

            return source.OrderBy(i => Regex.Replace(selector(i), @"\d+", m => m.Value.PadLeft(max, '0')));
        }

        public static IEnumerable<string> OrderByAlphaNumeric<String>(this IEnumerable<string> source)
        {
            return OrderByAlphaNumeric(source, (str) => str);
        }
    }
}
