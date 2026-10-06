using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PatternFinder;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class PatternTests
    {
        private static readonly byte[] Data = { 0x00, 0x11, 0x22, 0x33, 0x11, 0x22, 0x44, 0x11, 0x22, 0x33 };

        [TestMethod]
        public void Format_StripsNonPatternCharacters()
        {
            string formatted = Pattern.Format("11 2? -- zz ?3");

            formatted.ShouldBe("112??3");
        }

        [TestMethod]
        public void Find_ExactPattern_ReturnsFirstOffset()
        {
            Pattern.Byte[] pattern = Pattern.Transform("11 22 33");

            bool found = Pattern.Find(Data, pattern, out long offset);

            found.ShouldBeTrue();
            offset.ShouldBe(1);
        }

        [TestMethod]
        public void Find_PartialMatchBeforeRealMatch_ReturnsRealMatch()
        {
            Pattern.Byte[] pattern = Pattern.Transform("11 22 44");

            bool found = Pattern.Find(Data, pattern, out long offset);

            found.ShouldBeTrue();
            offset.ShouldBe(4);
        }

        [TestMethod]
        [DataRow("22 ?? 11", 2)]
        [DataRow("22 4?", 5)]
        public void Find_Wildcards_MatchAnyNibble(string text, long expected)
        {
            Pattern.Byte[] pattern = Pattern.Transform(text);

            bool found = Pattern.Find(Data, pattern, out long offset);

            found.ShouldBeTrue();
            offset.ShouldBe(expected);
        }

        [TestMethod]
        public void Find_Missing_ReturnsFalse()
        {
            Pattern.Byte[] pattern = Pattern.Transform("55 66");

            bool found = Pattern.Find(Data, pattern, out long offset);

            found.ShouldBeFalse();
            offset.ShouldBe(-1);
        }

        [TestMethod]
        public void FindAll_ReturnsEveryOffset()
        {
            Pattern.Byte[] pattern = Pattern.Transform("11 22");

            bool found = Pattern.FindAll(Data, pattern, out List<long> offsets);

            found.ShouldBeTrue();
            offsets.ShouldBe(new long[] { 1, 4, 7 });
        }

        [TestMethod]
        public void Scan_ReportsFoundSignatures()
        {
            Signature[] signatures =
            {
                new Signature("present", "33 11"),
                new Signature("missing", "AB CD"),
            };

            Signature[] found = Pattern.Scan(Data, signatures);

            found.Length.ShouldBe(1);
            found[0].Name.ShouldBe("present");
            found[0].FoundOffset.ShouldBe(3);
        }
    }
}
