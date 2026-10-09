using System;
using System.Text;
using FauFau.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class ChecksumTests
    {
        [TestMethod]
        [DataRow("", 0x5902879EU)]
        [DataRow("a", 0xD94AA0CFU)]
        [DataRow("id", 0x05F38D1EU)]
        [DataRow("name", 0x2108BA1BU)]
        [DataRow("dbzonemetadata::ZoneRecord", 0x69CE585DU)]
        public void FFnv32_String_MatchesKnownHash(string input, uint expected)
        {
            uint hash = Checksum.FFnv32(input);

            hash.ShouldBe(expected);
        }

        [TestMethod]
        public void FFnv32_String_MatchesAsciiBytes()
        {
            const string input = "dbitems::RootItem";

            uint stringHash = Checksum.FFnv32(input);
            uint bytesHash = Checksum.FFnv32(Encoding.ASCII.GetBytes(input));

            stringHash.ShouldBe(bytesHash);
        }

        [TestMethod]
        public void FFnv32_Span_MatchesString()
        {
            byte[] padded = Encoding.ASCII.GetBytes("xxdbitems::RootItemxx");

            uint spanHash = Checksum.FFnv32(padded.AsSpan(2, padded.Length - 4));

            spanHash.ShouldBe(Checksum.FFnv32("dbitems::RootItem"));
        }

        [TestMethod]
        [DataRow("", 0x00000000U)]
        [DataRow("123456789", 0xCBF43926U)]
        public void Crc32_MatchesKnownChecksum(string input, uint expected)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(input);

            uint crc = Checksum.Crc32(bytes);

            crc.ShouldBe(expected);
        }
    }
}
