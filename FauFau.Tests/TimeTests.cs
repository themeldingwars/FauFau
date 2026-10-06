using System;
using System.Globalization;
using FauFau.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class TimeTests
    {
        private static readonly DateTime Sample = new DateTime(2016, 11, 15, 18, 0, 0, 123, DateTimeKind.Utc);

        [TestMethod]
        public void DateTimeFromUnixTimestamp_ConvertsAllUnits()
        {
            DateTime expected = new DateTime(2016, 11, 15, 18, 0, 0, DateTimeKind.Utc);
            long seconds = new DateTimeOffset(expected).ToUnixTimeSeconds();

            DateTime fromSeconds = Time.DateTimeFromUnixTimestamp(seconds);
            DateTime fromMilliseconds = Time.DateTimeFromUnixTimestampMilliseconds(seconds * 1000);
            DateTime fromMicroseconds = Time.DateTimeFromUnixTimestampMicroseconds(seconds * 1_000_000);

            fromSeconds.ShouldBe(expected);
            fromMilliseconds.ShouldBe(expected);
            fromMicroseconds.ShouldBe(expected);
        }

        [TestMethod]
        public void UnixTimestampMicroseconds_RoundTrips()
        {
            long expected = new DateTimeOffset(Sample).ToUnixTimeMilliseconds() * 1000;

            long microseconds = Time.UnixTimestampMicrosecondsFromDatetime(Sample);
            DateTime converted = Time.DateTimeFromUnixTimestampMicroseconds(microseconds);

            microseconds.ShouldBe(expected);
            converted.ShouldBe(Sample);
        }

        [TestMethod]
        public void FictionalTimeString_IgnoresCurrentCulture()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            string text;
            try
            {
                text = Time.FictionalTimeString(new DateTime(2239, 11, 15, 18, 0, 0));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }

            text.ShouldBe("Friday, November 15.750 Zulu 2239");
        }

        [TestMethod]
        [DataRow(0, 0f)]
        [DataRow(12, 0.5f)]
        [DataRow(18, 0.75f)]
        public void ClockAsFloat_IsFractionOfDay(int hour, float expected)
        {
            DateTime time = new DateTime(2016, 1, 1, hour, 0, 0);

            float clock = Time.ClockAsFloat(time);

            clock.ShouldBe(expected);
        }
    }
}
