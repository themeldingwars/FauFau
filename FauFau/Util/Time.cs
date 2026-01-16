using System;

namespace FauFau.Util
{
    public static class Time
    {
        public static DateTime DateTimeFromUnixTimestamp(long seconds)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds);
        }
        public static DateTime DateTimeFromUnixTimestampMilliseconds(long milliseconds)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(milliseconds);
        }
        public static DateTime DateTimeFromUnixTimestampMicroseconds(long microseconds)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddTicks((TimeSpan.TicksPerMillisecond / 1000) * microseconds);
        }

        public static long UnixTimestampMicrosecondsFromDatetime(DateTime dateTime)
        {
            return (dateTime.Ticks - new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).Ticks) / (TimeSpan.TicksPerMillisecond / 1000);
        }

        public static DateTime FictionalTimeNow()
        {
            return DateTime.UtcNow.AddYears(223);
        }

        public static float ClockAsFloat(DateTime ts)
        {
            return (float)ts.TimeOfDay.TotalMilliseconds / 86400000f;
        }
        public static string ZuluTime(DateTime ts)
        {
            string t = ClockAsFloat(ts).ToString("0.000");
            return t.Substring(t.Length - 3);
        }

        public static string FictionalTimeString(DateTime ts)
        {
            return ts.ToString("dddd, MMMM dd.") + ZuluTime(ts) + " Zulu " + ts.Year;
        }
    }
}
