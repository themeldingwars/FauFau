using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using static FauFau.Util.Common;

namespace FauFau.Util
{
    public partial class Project<T> : Common.JsonWrapper<T>
    {
        public static class Localization
        {
            private static Dictionary<string, Language> languages = LoadLanguages();
            private static Dictionary<string, Language> LoadLanguages()
            {
                Dictionary<string, Language> _languages = new Dictionary<string, Language>();

                if (Directory.Exists(@"Localization"))
                {
                    foreach (string file in Directory.GetFiles(@"Localization", "*.json"))
                    {
                        Language lang = Language.Read(file);
                        if (_languages.ContainsKey(lang.Name))
                        {
                            Log.WriteLine(Log.LogLevel.Trace, string.Format("Duplicate language file for \"{0}\" skipping file found at \"{1}\"", lang.Name, file));
                        }
                        else
                        {
                            _languages.Add(lang.Name, lang);
                        }
                    }
                }

                Assembly assembly = Assembly.GetEntryAssembly();
                Console.WriteLine(assembly.FullName);
                foreach (string resource in EmbeddedResource.ResourceSearch(assembly, "Localization", "*.json", true))
                {

                    Language lang = Language.FromString(EmbeddedResource.GetResourceAsString(resource, assembly));
                    if (_languages.ContainsKey(lang.Name))
                    {
                        Log.WriteLine(Log.LogLevel.Trace, string.Format("Duplicate language file for \"{0}\" skipping file found at \"{1}\"", lang.Name, resource));
                    }
                    else
                    {
                        Console.WriteLine(lang.Name);
                        _languages.Add(lang.Name, lang);
                    }
                }
                return _languages;
            }

            public static string Localize(string key)
            {
                return Localize(key, null);
            }
            public static string Localize(string key, params object[] args)
            {
                if (languages.Count > 0 && languages[CurrentLanguage].Strings.ContainsKey(key))
                {

                    if (args == null)
                    {
                        return string.Format(languages[CurrentLanguage].Strings[key]);
                    }
                    else
                    {
                        return string.Format(languages[CurrentLanguage].Strings[key], args);
                    }
                }
                else if (languages.Count > 0 && languages[DefaultLanguage].Strings.ContainsKey(key))
                {

                    if (args == null)
                    {
                        return string.Format(languages[DefaultLanguage].Strings[key]);
                    }
                    else
                    {
                        return string.Format(languages[CurrentLanguage].Strings[key], args);
                    }
                }
                else
                {
                    if (args == null)
                    {
                        return string.Format(key);
                    }
                    else
                    {
                        return string.Format(key, args);
                    }
                }
            }

            [DataContract]
            public class Language : JsonWrapper<Language>
            {
                private Dictionary<string, string> _strings = new ();

                [DataMember] protected string language = "Unknown";
                [DataMember]
                protected string[][] strings
                {
                    get
                    {
                        string[][] ret = new string[_strings.Count][];
                        int x = 0;
                        foreach (KeyValuePair<string, string> str in _strings)
                        {
                            ret[x] = new string[2];
                            ret[x][0] = str.Key;
                            ret[x][1] = str.Value;
                            x++;
                        }
                        return ret;
                    }
                    set
                    {
                        _strings = new Dictionary<string, string>(value.Length);
                        for (int i = 0; i < value.Length; i++)
                        {
                            _strings.Add(value[i][0], value[i][1]);
                        }
                    }
                }


                public string Name { get => language; set => language = value; }
                public Dictionary<string, string> Strings { get => _strings; set => _strings = value; }
            }
        }
    }
}
