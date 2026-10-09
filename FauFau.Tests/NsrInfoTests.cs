using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FauFau.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class NsrInfoTests
    {
        private static NsrInfo Read(byte[] bytes, IncrementalHash hash = null)
        {
            return NsrInfo.Read(new MemoryStream(bytes), hash);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void Read_Replay_ReadsSectionsAndCountsPackets(bool compressed)
        {
            byte[] bytes = NsrTests.WriteBytes(NsrTests.CreateSample(compressed));

            NsrInfo info = Read(bytes);

            info.GzipLayers.ShouldBe(compressed ? 1 : 0);
            info.Description.ProtocolVersion.ShouldBe(19551);
            info.Meta.ZoneId.ShouldBe(448);
            info.Meta.CharacterName.ShouldBe("Tester");
            info.Packets.ShouldBe(2);
            info.FirstPacketTime.ShouldBe(3795714048U);
            info.Truncated.ShouldBeFalse();
            info.Error.ShouldBeNull();
        }

        [TestMethod]
        public void Read_Version2_CountsPacketsAfterMeta()
        {
            byte[] bytes = NsrTests.CreateVersion2();

            NsrInfo info = Read(bytes);

            info.Description.Version.ShouldBe(2);
            info.Meta.Version.ShouldBe(3);
            info.Packets.ShouldBe(2);
            info.DataSize.ShouldBe(bytes.Length);
        }

        [TestMethod]
        public void Read_GzipTwice_UnpacksAllLayers()
        {
            byte[] plain = NsrTests.WriteBytes(NsrTests.CreateSample(false));

            NsrInfo info = Read(NsrTests.Gzip(NsrTests.Gzip(plain)));

            info.GzipLayers.ShouldBe(2);
            info.DataSize.ShouldBe(plain.Length);
            info.Packets.ShouldBe(2);
        }

        [TestMethod]
        public void Read_LastPacketCutOff_CountsCompletePackets()
        {
            byte[] bytes = NsrTests.WriteBytes(NsrTests.CreateSample(false));

            NsrInfo info = Read(bytes.AsSpan(0, bytes.Length - 5).ToArray());

            info.Truncated.ShouldBeTrue();
            info.Packets.ShouldBe(1);
            info.Meta.ShouldNotBeNull();
        }

        [TestMethod]
        public void Read_SectionsCutOff_IsTruncatedWithoutSections()
        {
            byte[] bytes = NsrTests.WriteBytes(NsrTests.CreateSample(false));

            NsrInfo info = Read(bytes.AsSpan(0, 60).ToArray());

            info.Truncated.ShouldBeTrue();
            info.Meta.ShouldBeNull();
            info.DataSize.ShouldBe(60);
        }

        [TestMethod]
        public void Read_UnknownVersion_ReportsError()
        {
            byte[] bytes = NsrTests.WriteBytes(NsrTests.CreateSample(false));
            bytes[4] = 9;

            NsrInfo info = Read(bytes);

            info.Description.ShouldBeNull();
            info.Truncated.ShouldBeFalse();
            info.Error.ShouldNotBeNull();
        }

        [TestMethod]
        public void Read_NotAReplay_ReturnsNull()
        {
            NsrInfo info = Read(Encoding.ASCII.GetBytes("not a replay"));

            info.ShouldBeNull();
        }

        [TestMethod]
        public void Read_Hash_CoversUnpackedData()
        {
            byte[] plain = NsrTests.WriteBytes(NsrTests.CreateSample(false));
            using IncrementalHash packedHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            Read(NsrTests.Gzip(plain), packedHash);

            packedHash.GetHashAndReset().ShouldBe(SHA256.HashData(plain));
        }

        [TestMethod]
        public void Read_Version2Hash_CoversEveryByteOnce()
        {
            byte[] bytes = NsrTests.CreateVersion2();
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            Read(bytes, hash);

            hash.GetHashAndReset().ShouldBe(SHA256.HashData(bytes));
        }
    }
}
