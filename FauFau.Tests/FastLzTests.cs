using System;
using System.IO;
using System.Linq;
using System.Text;
using FauFau.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class FastLzTests
    {
        [TestMethod]
        [DataRow((byte)0x00)]
        [DataRow((byte)0x20)]
        public void Decompress_LiteralsAndOverlappingMatch_RepeatsBytes(byte level)
        {
            byte[] compressed = { (byte)(level | 2), (byte)'a', (byte)'b', (byte)'c', 0x80, 2 };

            byte[] data = FastLz.Decompress(compressed);

            Encoding.ASCII.GetString(data).ShouldBe("abcabcabc");
        }

        [TestMethod]
        public void Decompress_Level1LongMatch_ReadsExtraLength()
        {
            byte[] compressed = { 0x00, (byte)'x', 0xE0, 10, 0 };

            byte[] data = FastLz.Decompress(compressed);

            data.ShouldBe(Enumerable.Repeat((byte)'x', 20).ToArray());
        }

        [TestMethod]
        public void Decompress_Level2LongMatch_ReadsLengthUntilBelow255()
        {
            byte[] compressed = { 0x20, (byte)'x', 0xE0, 255, 1, 0 };

            byte[] data = FastLz.Decompress(compressed);

            data.Length.ShouldBe(1 + 6 + 255 + 1 + 3);
            data.ShouldAllBe(b => b == (byte)'x');
        }

        [TestMethod]
        public void Decompress_MatchBeforeStart_Throws()
        {
            byte[] compressed = { 0x00, (byte)'x', 0x80, 5 };

            Action act = () => FastLz.Decompress(compressed);

            act.ShouldThrow<InvalidDataException>();
        }
    }
}
