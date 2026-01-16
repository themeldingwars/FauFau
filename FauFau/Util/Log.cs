using System;
using System.Collections.Generic;
using System.Linq;

namespace FauFau.Util
{
    public partial class Project<T> : Common.JsonWrapper<T>
    {
        public class Log
        {
            public delegate void LogMessageDelegate(LogLevel level, string message);
            public delegate void LogClearDelegate();

            private static LogMessageDelegate onLogMessage;
            public static LogMessageDelegate OnLogMessage
            {
                get
                {
                    return onLogMessage;
                }
                set
                {
                    onLogMessage = value;
                    Dequeue();
                }
            }
            public static LogClearDelegate OnLogClear;

            private static Queue<Tuple<LogLevel, string>> queue = new ();


            private static LogLevel logLevel = Log.LogLevel.Trace;
            public static LogLevel Level { get => logLevel; set => logLevel = value; }

            private static void Clear()
            {
                queue.Clear();
                OnLogClear?.Invoke();
            }

            public static void WriteLine(LogLevel level, string text)
            {
                Write(level, text + "\n");
            }
            public static void Write(LogLevel level, string text)
            {
                queue.Enqueue(new Tuple<LogLevel, string>(level, text));
                Dequeue();
            }

            public static void Trace(string text, params object[] args)
            {
                WriteLine(LogLevel.Trace, Localization.Localize(text, args));
            }
            public static void Debug(string text, params object[] args)
            {
                WriteLine(LogLevel.Debug, Localization.Localize(text, args));
            }
            public static void Info(string text, params object[] args)
            {
                WriteLine(LogLevel.Information, Localization.Localize(text, args));
            }
            public static void Warning(string text, params object[] args)
            {
                WriteLine(LogLevel.Warning, Localization.Localize(text, args));
            }
            public static void Error(string text, params object[] args)
            {
                WriteLine(LogLevel.Error, Localization.Localize(text, args));
            }
            public static void Critical(string text, params object[] args)
            {
                WriteLine(LogLevel.Critical, Localization.Localize(text, args));
            }

            private static void Dequeue()
            {
                if (onLogMessage != null)
                {
                    while (queue.Any())
                    {
                        Tuple<LogLevel, string> data = queue.Dequeue();
                        OnLogMessage?.Invoke(data.Item1, data.Item2);
                    }
                }
            }
            public enum LogLevel
            {
                Trace = 0,
                Debug = 1,
                Information = 2,
                Warning = 3,
                Error = 4,
                Critical = 5,
                None = 6
            }
        }
    }
}
