using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Path = System.IO.Path;

namespace MonoTorrent.UWP
{
    public static class PathHelpers
    {
        public static char DirectorySeparatorChar 
        {
            get 
            { 
                return '\\';
            }
        }

        public static string GetFullPath(string path)
        {
            return path; //TODO: check if this works
        }

        public static string RemovePrefixPath(string path, string pathPrefix)
        {
            if (path.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return path.Remove(0, pathPrefix.Length).TrimStart('\\');
            }

            return path;
        }

        public static string RemoveSuffixPath(string path, string pathSuffix)
        {
            if (path.EndsWith(pathSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return path.Remove(path.Length - pathSuffix.Length).TrimEnd('\\');
            }

            return path;
        }

        public static string SanitizeFileName(string fileName)
        {
            return Path.GetInvalidFileNameChars().Aggregate(fileName, (current, c) => current.Replace(c.ToString(), String.Empty));
        }
    }
}
