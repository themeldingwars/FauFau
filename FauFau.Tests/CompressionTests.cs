using System;
using System.IO;
using Bitter;
using FauFau.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class CompressionTests
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
        public void Deflate_InflateUnknownTargetSize_RoundTrips()
        {
            byte[] original = MakeData(5000);
            BinaryStream deflated = new BinaryStream();
            BinaryStream inflated = new BinaryStream();

            Compression.Deflate(StreamOf(original), deflated);
            deflated.ByteOffset = 0;
            Compression.Inflate(deflated, inflated);

            ToArray(inflated).ShouldBe(original);
        }

        [TestMethod]
        public void Deflate_InflateKnownTargetSize_RoundTrips()
        {
            byte[] original = MakeData(5000);
            BinaryStream deflated = new BinaryStream();
            BinaryStream inflated = new BinaryStream();

            Compression.Deflate(StreamOf(original), deflated);
            deflated.ByteOffset = 0;
            Compression.Inflate(deflated, inflated, original.Length);

            ToArray(inflated).ShouldBe(original);
        }

        [TestMethod]
        public void Gzip_Gunzip_RoundTrips()
        {
            byte[] original = MakeData(5000);
            BinaryStream compressed = new BinaryStream();
            BinaryStream decompressed = new BinaryStream();

            Compression.Gzip(StreamOf(original), compressed);
            compressed.ByteOffset = 0;
            Compression.Gunzip(compressed, decompressed);

            ToArray(decompressed).ShouldBe(original);
        }
    }
}
