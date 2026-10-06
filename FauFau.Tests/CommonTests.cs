using System;
using System.IO;
using Bitter;
using FauFau.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class CommonTests
    {
        private static byte[] MakeData(int length)
        {
            byte[] data = new byte[length];
            new Random(length).NextBytes(data);
            return data;
        }

        private static BinaryStream StreamOf(byte[] data)
        {
            return new BinaryStream(new MemoryStream(data));
        }

        private static byte[] ToArray(BinaryStream stream)
        {
            stream.ByteOffset = 0;
            return stream.Read.ByteArray((int)stream.Length);
        }

        [TestMethod]
        public void MTXor_ChangesData()
        {
            byte[] original = MakeData(1027);
            byte[] data = (byte[])original.Clone();

            Common.MTXor(42, ref data);

            data.ShouldNotBe(original);
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(3)]
        [DataRow(4)]
        [DataRow(1027)]
        public void MTXor_Twice_RestoresData(int length)
        {
            byte[] original = MakeData(length);
            byte[] data = (byte[])original.Clone();

            Common.MTXor(42, ref data);
            Common.MTXor(42, ref data);

            data.ShouldBe(original);
        }

        [TestMethod]
        public void MTXor_ArrayAndStreamOverloads_Agree()
        {
            byte[] original = MakeData(1027);
            byte[] array = (byte[])original.Clone();
            BinaryStream destination = new BinaryStream();

            Common.MTXor(42, ref array);
            Common.MTXor(42, StreamOf(original), destination);
            byte[] streamed = ToArray(destination);

            streamed.ShouldBe(array);
        }

        [TestMethod]
        public void MTXorOldest_MatchesMTXor()
        {
            byte[] original = MakeData(1027);
            BinaryStream current = new BinaryStream();
            BinaryStream oldest = new BinaryStream();

            Common.MTXor(42, StreamOf(original), current);
            Common.MTXorOldest(42, StreamOf(original), oldest);

            ToArray(oldest).ShouldBe(ToArray(current));
        }

        [TestMethod]
        public void Deflate_InflateUnknownTargetSize_RoundTrips()
        {
            byte[] original = MakeData(5000);
            BinaryStream deflated = new BinaryStream();
            BinaryStream inflated = new BinaryStream();

            Common.Deflate(StreamOf(original), deflated);
            deflated.ByteOffset = 0;
            Common.InflateUnknownTargetSize(deflated, inflated);

            ToArray(inflated).ShouldBe(original);
        }

        [TestMethod]
        public void Deflate_InflateKnownTargetSize_RoundTrips()
        {
            byte[] original = MakeData(5000);
            BinaryStream deflated = new BinaryStream();
            BinaryStream inflated = new BinaryStream();

            Common.Deflate(StreamOf(original), deflated);
            deflated.ByteOffset = 0;
            Common.Inflate(deflated, inflated, original.Length);

            ToArray(inflated).ShouldBe(original);
        }

        [TestMethod]
        public void Gzip_UnGzipUnknownTargetSize_RoundTrips()
        {
            byte[] original = MakeData(5000);
            BinaryStream compressed = new BinaryStream();
            BinaryStream decompressed = new BinaryStream();

            Common.Gzip(StreamOf(original), compressed);
            compressed.ByteOffset = 0;
            Common.UnGzipUnknownTargetSize(compressed, decompressed);

            ToArray(decompressed).ShouldBe(original);
        }

        [TestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void HexHelpers_MatchConvert(bool upperCase)
        {
            byte[] data = MakeData(37);
            string expected = upperCase ? Convert.ToHexString(data) : Convert.ToHexString(data).ToLowerInvariant();
            char[] written = new char[data.Length * 2];

            string hexString = Common.BytesToHexString(data, upperCase).ToString();
            string hexChars = new string(Common.BytesToHexChars(data, upperCase));
            bool success = Common.TryWriteBytesAsHex(data, written, upperCase);

            hexString.ShouldBe(expected);
            hexChars.ShouldBe(expected);
            success.ShouldBeTrue();
            new string(written).ShouldBe(expected);
        }

        [TestMethod]
        public void TryWriteBytesAsHex_OutputTooShort_ReturnsFalse()
        {
            char[] output = new char[7];

            bool success = Common.TryWriteBytesAsHex(new byte[4], output);

            success.ShouldBeFalse();
        }

        [TestMethod]
        [DataRow(0, 4, 0)]
        [DataRow(8, 4, 8)]
        [DataRow(9, 4, 12)]
        [DataRow(11, 4, 12)]
        public void FindClosestLargerNumber_RoundsUpToMultiple(int n, int m, int expected)
        {
            int result = Common.FindClosestLargerNumber(n, m);

            result.ShouldBe(expected);
        }
    }
}
