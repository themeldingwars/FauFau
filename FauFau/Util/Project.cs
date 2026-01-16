using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using static FauFau.Util.Solitude;

namespace FauFau.Util
{
    [DataContract]
    public partial class Project<T> : Common.JsonWrapper<T>
    {
        public static string DefaultLanguage { get => "English"; }

        public static string CurrentLanguage = "English";

        [DataMember] public string Language { get { return CurrentLanguage;  } set { CurrentLanguage = value; } }

        public delegate void RunDelegate();
        public delegate void ExitDelegate();
        public delegate void ErrorDelegate(Exception ex);
        public static RunDelegate Run;
        public static ExitDelegate Exit;
        public static CmdDelegate Cmd;
        public static ErrorDelegate Error;
        private static Solitude solitude;

        //public static Localization<T> Localization { get { return Localization<T>;  } };

       // public static Localization<T> Localization = new Localization<T>();

        public static bool SingleInstanceApplication = false;

       
        public static readonly string TMWBaseDataPath = Path.Combine(Environment.ExpandEnvironmentVariables(@"%LocalAppData%"), "TheMeldingWars");

        private static string dataPath;
        public static string DataPath
        {
            get
            {
                if(dataPath == null)
                {
                    dataPath = GetDataPath(Name);
                    Directory.CreateDirectory(dataPath);
                }
                return dataPath;
            }
        }

        private static string name;   
        public static string Name { get { return name == null ? typeof(T).Name : name; } set { name = value; } }

        private static T config;
        private static string cfgPath = Path.Combine(DataPath, "config.json");
        private static string errorPath = Path.Combine(DataPath, "sadness.txt");

        public static T Config
        {
            get
            {
                if (config == null)
                {
                    try
                    {
                        if (File.Exists(cfgPath))
                        {
                            config = Read(cfgPath);
                        }
                        else
                        {
                            config = Activator.CreateInstance<T>();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                        config = Activator.CreateInstance<T>();
                    }
                }
                return config;
            }
        }
        public static void SaveConfig()
        {
            (Config as Common.JsonWrapper<T>).Write(cfgPath);
        }
        public static string GetDataPath(string projectName)
        {
            return Path.Combine(TMWBaseDataPath, Name);
        }

        public static void Start()
        {
            #if !DEBUG
            try
            #endif
            {
                if (SingleInstanceApplication)
                {
                    solitude = new Solitude("TheMeldingWars." + Name, CommandHandler);
                    
                    if (!solitude.FirstInstance)
                    {
                        return;
                    }              
                }


                if (!Common.IsDebugging())
                {
                    string[] args = Environment.GetCommandLineArgs();
                    if (!DataPath.Equals(Path.GetDirectoryName(args[0]), StringComparison.CurrentCultureIgnoreCase))
                    {
                        string here = args[0];
                        string there = Path.Combine(DataPath, Name + ".exe");

                        if (File.Exists(there))
                        {
                            File.Delete(there);
                        }
                        File.Copy(here, there);
                        Common.Execute(there, Environment.CommandLine);
                        Environment.Exit(0);
                    }
                }
                else
                {
                    Cmd?.Invoke(Environment.GetCommandLineArgs().ToList());
                }

                AppDomain.CurrentDomain.ProcessExit += new EventHandler(ExitHandler);
                Run?.Invoke();
            }
            #if !DEBUG
            catch (Exception ex)
            {
               ErrorHandler(null, new UnhandledExceptionEventArgs(ex, true));
            }
            #endif
        }
        private static void CommandHandler(List<string> args)
        {
            Cmd?.Invoke(args);
        }
        private static void ExitHandler(object sender, EventArgs args)
        {
            SaveConfig();
            Exit?.Invoke();
        }

        // error handling stolen from meldii
        // all hail the quinn
        private static void ErrorHandler(object sender, UnhandledExceptionEventArgs args)
        {
            Exception e = (Exception)args.ExceptionObject;
            File.WriteAllText(errorPath, ParseException(e));
            Error?.Invoke(e);
            Environment.Exit(0);
        }
    
        public static string ParseException(Exception ex)
        {
            var sep = "---------------------------------------------------------";
            var ret = new System.Text.StringBuilder();
            var cur = ex;
            int num = 1;
            do
            {
                ret.AppendFormat("\r\n\r\n{0} - Exception\r\n{1}", num.ToString(), sep);
                ret.AppendFormat("\r\n.NET Runtime Version: {0}", Environment.Version.ToString());
                ret.AppendFormat("\r\nOS: {0}", Environment.OSVersion.ToString());
                ret.AppendFormat("\r\nType: {0}", cur.GetType().FullName);

                var ignore = new[] { "InnerException", "StackTrace", "Data", "HelpLink" };
                var properties = ex.GetType().GetProperties();
                foreach (var property in properties)
                {
                    foreach(string x in ignore)
                    {
                        if(property.Name.Contains(x))
                        {
                            continue;
                        }
                    }

                    try
                    {
                        var val = property.GetValue(cur, null);
                        if (val == null)
                            ret.AppendFormat("\r\n{0}: NULL", property.Name);
                        else ret.AppendFormat("\r\n{0}: {1}", property.Name, val.ToString());
                    }
                    catch (Exception ex2)
                    {

                        //MessageBox.Show(ex2);
                        // ignore
                    }

                }

                if (cur.StackTrace != null)
                {
                    ret.AppendFormat("\r\n\r\nStackTrace\r\n{0}", sep);
                    ret.AppendFormat("\r\n{0}", cur.StackTrace);
                }

                cur = cur.InnerException;
                ++num;
            }
            while (cur != null);

            return ret.ToString();
        }


    }
}
