using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MonoTorrent.UWP
{
    public static class RegexExtensions
    {
        public static Dictionary<string, string> MatchNamedCaptures(this Regex regex, string input)
        {
            var namedCaptureDictionary = new Dictionary<string, string>();
            GroupCollection groups = regex.Match(input).Groups;
            string[] groupNames = regex.GetGroupNames();
            foreach (string groupName in groupNames)
                if (groups[groupName].Captures.Count > 0)
                    namedCaptureDictionary.Add(groupName, groups[groupName].Value);
            return namedCaptureDictionary;
        }

        public static string GetNamedCaptureValue(this Regex regex, string input, string namedGroup)
        {
            var namedCaptureDictionary = MatchNamedCaptures(regex, input);
            if (namedCaptureDictionary.ContainsKey(namedGroup))
            {
                return namedCaptureDictionary[namedGroup];
            }
            return null;
        }
    }
}
