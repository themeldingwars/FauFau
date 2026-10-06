using System;
using System.Collections.Generic;
using FauFau.Hax;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class BytePatternTests
    {
        private static readonly byte[] Data = { 0x00, 0x11, 0x22, 0x33, 0x11, 0x22, 0x44, 0x11, 0x22, 0x33 };

        [TestMethod]
        [DataRow("11 22 33", 1)]
        [DataRow("11 22 44", 4)]
        [DataRow("22 ?? 11", 2)]
        [DataRow("22 ? 11", 2)]
        [DataRow("22 4?", 5)]
        [DataRow("?2 3?", 2)]
        [DataRow("?? ??", 0)]
        public void Find_Pattern_ReturnsFirstOffset(string text, int expected)
        {
            BytePattern pattern = BytePattern.Parse(text);

            int offset = pattern.Find(Data);

            offset.ShouldBe(expected);
        }

        [TestMethod]
        public void Find_Bytes_ReturnsFirstOffset()
        {
            BytePattern pattern = new BytePattern(new byte[] { 0x22, 0x33 });

            int offset = pattern.Find(Data);

            offset.ShouldBe(2);
        }

        [TestMethod]
        public void Find_FromStart_SkipsEarlierMatches()
        {
            BytePattern pattern = BytePattern.Parse("11 22");

            int offset = pattern.Find(Data, 2);

            offset.ShouldBe(4);
        }

        [TestMethod]
        [DataRow("55 66")]
        [DataRow("?? 55")]
        [DataRow("4? 99")]
        public void Find_Missing_ReturnsMinusOne(string text)
        {
            BytePattern pattern = BytePattern.Parse(text);

            int offset = pattern.Find(Data);

            offset.ShouldBe(-1);
        }

        [TestMethod]
        public void Find_MatchWouldRunPastEnd_ReturnsMinusOne()
        {
            BytePattern pattern = BytePattern.Parse("22 33 ??");

            int offset = pattern.Find(new byte[] { 0x00, 0x22, 0x33 });

            offset.ShouldBe(-1);
        }

        [TestMethod]
        public void FindAll_ReturnsEveryMatchWithoutOverlap()
        {
            BytePattern pattern = BytePattern.Parse("11 22");

            List<int> offsets = pattern.FindAll(new byte[] { 0x11, 0x22, 0x11, 0x22, 0x11, 0x11, 0x22 });

            offsets.ShouldBe(new[] { 0, 2, 5 });
        }

        [TestMethod]
        public void IsMatch_ShortData_ReturnsFalse()
        {
            BytePattern pattern = BytePattern.Parse("11 22 33");

            bool match = pattern.IsMatch(new byte[] { 0x11, 0x22 });

            match.ShouldBeFalse();
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("1")]
        [DataRow("123")]
        [DataRow("1G")]
        public void Parse_InvalidPattern_Throws(string text)
        {
            Action act = () => BytePattern.Parse(text);

            act.ShouldThrow<FormatException>();
        }
    }
}
