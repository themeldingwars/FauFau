using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace FauFau.Util
{
    public static class EmbeddedResource
    {
        public static string[] ResourceList(Assembly assembly = null)
        {
            if (assembly == null)
            {
                assembly = Assembly.GetExecutingAssembly();
            }

            string[] res = assembly.GetManifestResourceNames();
            string[] ret = new string[res.Length];

            for (int i = 0; i < ret.Length; i++)
            {

                string[] split = res[i].Split('.');
                string str = "";
                for (int x = 1; x < split.Length - 2; x++)
                {
                    str += split[x] + "\\";
                }
                str += split[split.Length - 2] + "." + split[split.Length - 1];
                ret[i] = str;
            }
            return ret;

        }
        public static string[] ResourceSearch(string path, string pattern = "*", bool recursive = false)
        {
            return ResourceSearch(Assembly.GetExecutingAssembly(), path, pattern, recursive);
        }
        public static string[] ResourceSearch(Assembly assembly, string path, string pattern = "*", bool recursive = false)
        {

            if (assembly == null)
            {
                assembly = Assembly.GetExecutingAssembly();
            }


            char[] invalidFileChars = Path.GetInvalidFileNameChars();


            string[] files = ResourceList(assembly);
            List<string> match = new List<string>();

            foreach (char c in invalidFileChars)
            {
                if (path.Contains(c.ToString()))
                {
                    throw new ArgumentException("Illegal characters in path.");
                }

                if (!c.Equals((char)'*'))
                {
                    if (pattern.Contains(c.ToString()))
                    {
                        throw new ArgumentException("Illegal characters in pattern.");
                    }
                }

            }

            path = path.Replace(@"/", @"\\");
            path = path.Replace(@"\", @"\\");
            path = Regex.Replace(path, @"\\{2,}", @"\\");
            path = path.Replace(@".", @"\.");


            if (path.EndsWith("\\"))
            {
                path = path.Substring(0, path.Length - 1);
            }

            path += recursive ? @".*\\" : @"\\";

            pattern = pattern.Replace(@"/", @"\\");
            pattern = pattern.Replace(@"\", @"\\");
            pattern = Regex.Replace(pattern, @"\\{2,}", @"\\");
            pattern = pattern.Replace(@".", @"\.");
            pattern = pattern.Replace("*", ".*");

            string regex = path + pattern;

            //Console.WriteLine(regex);
            foreach (string file in files)
            {
                if (!Regex.IsMatch(file, regex, RegexOptions.IgnoreCase))
                {
                    continue;
                }
                match.Add(file);
            }

            return match.ToArray();
        }
        private static string FormatName(Assembly assembly, string resourceName)
        {
            return assembly.GetName().Name + "." + resourceName.Replace(" ", "_")
                                                               .Replace("\\", ".")
                                                               .Replace("/", ".");
        }
        public static string GetResourceAsString(string resourceName, Assembly assembly = null)
        {
            if (assembly == null)
            {
                assembly = Assembly.GetExecutingAssembly();
            }

            resourceName = FormatName(assembly, resourceName);
            using (Stream resourceStream = assembly.GetManifestResourceStream(resourceName))
            {
                if (resourceStream == null)
                    return null;

                using (StreamReader reader = new StreamReader(resourceStream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
