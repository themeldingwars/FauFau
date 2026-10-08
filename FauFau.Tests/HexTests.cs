using System;
using FauFau.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class HexTests
    {
        [TestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void Encode_MatchesConvert(bool upperCase)
        {
            byte[] data = new byte[37];
            new Random(37).NextBytes(data);
            string expected = upperCase ? Convert.ToHexString(data) : Convert.ToHexString(data).ToLowerInvariant();
            char[] written = new char[data.Length * 2];

            string encoded = Hex.Encode(data, upperCase);
            bool success = Hex.TryEncode(data, written, upperCase);

            encoded.ShouldBe(expected);
            success.ShouldBeTrue();
            new string(written).ShouldBe(expected);
        }

        [TestMethod]
        public void TryEncode_OutputTooShort_ReturnsFalse()
        {
            char[] output = new char[7];

            bool success = Hex.TryEncode(new byte[4], output);

            success.ShouldBeFalse();
        }
    }
}
