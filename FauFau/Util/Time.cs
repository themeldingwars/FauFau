using System;
using System.Collections.Generic;
using System.Text;

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
    }
}
