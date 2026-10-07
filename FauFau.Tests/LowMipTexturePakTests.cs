using System;
using System.IO;
using System.Text;
using Bitter;
using FauFau.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class LowMipTexturePakTests
    {
        private const int Dxt1BlockSize = 8;

        private static readonly byte[] LastThreeMipsOf16x16 = CreateLastThreeMipsOf16x16();

        private static byte[] CreateLastThreeMipsOf16x16()
        {
            byte[] mips = new byte[3 * Dxt1BlockSize];
            for (int i = 0; i < mips.Length; i++)
            {
                mips[i] = (byte)i;
            }
            return mips;
        }

        private static byte[] CreatePak()
        {
            (byte[] mipProperties, byte[] mipStream) = TestLzma.Compress(LastThreeMipsOf16x16);

            BinaryStream index = new BinaryStream(new MemoryStream());
            index.Write.Int(1);
            index.Write.ByteArray(Encoding.ASCII.GetBytes("00118000\\00118090.dds\0"));
            index.Write.UInt(0xA1007);
            index.Write.UInt(16);
            index.Write.UInt(16);
            index.Write.UInt(0);
            index.Write.UInt(128);
            index.Write.UInt(5);
            index.Write.UInt(32);
            index.Write.UInt(4);
            index.Write.String("DXT1");
            index.Write.ByteArray(new byte[20]);
            index.Write.UInt(0x401008);
            index.Write.ByteArray(new byte[12]);
            index.Write.UInt(2);
            index.Write.UInt((uint)LastThreeMipsOf16x16.Length);
            index.Write.UInt(0);
            index.Write.UInt((uint)(5 + mipStream.Length));
            index.ByteOffset = 0;
            byte[] indexBytes = index.Read.ByteArray((int)index.Length);
            (byte[] indexProperties, byte[] indexStream) = TestLzma.Compress(indexBytes);

            BinaryStream stream = new BinaryStream(new MemoryStream());
            stream.Write.String("LMTD");
            stream.Write.Int(indexBytes.Length);
            stream.Write.Int(5 + indexStream.Length);
            stream.Write.ByteArray(indexProperties);
            stream.Write.ByteArray(indexStream);
            stream.Write.ByteArray(mipProperties);
            stream.Write.ByteArray(mipStream);
            stream.ByteOffset = 0;
            return stream.Read.ByteArray((int)stream.Length);
        }

        [TestMethod]
        public void Read_Pak_ReadsEntriesAndData()
        {
            byte[] bytes = CreatePak();
            LowMipTexturePak pak = new LowMipTexturePak();

            pak.Read(bytes);

            pak.Entries.Count.ShouldBe(1);
            LowMipTexturePak.Entry entry = pak.Entries[0];
            entry.Path.ShouldBe("00118000\\00118090.dds");
            entry.Width.ShouldBe(16U);
            entry.MipMapCount.ShouldBe(5U);
            entry.FourCC.ShouldBe("DXT1");
            entry.FirstMip.ShouldBe(2U);
            pak.GetData(entry).ShouldBe(LastThreeMipsOf16x16);
        }

        [TestMethod]
        public void CreateDds_Entry_HasStoredMipsOnly()
        {
            LowMipTexturePak pak = new LowMipTexturePak();
            pak.Read(CreatePak());

            byte[] dds = pak.CreateDds(pak.Entries[0]);

            Encoding.ASCII.GetString(dds, 0, 4).ShouldBe("DDS ");
            BitConverter.ToUInt32(dds, 4).ShouldBe(124U);
            BitConverter.ToUInt32(dds, 12).ShouldBe(4U);
            BitConverter.ToUInt32(dds, 16).ShouldBe(4U);
            BitConverter.ToUInt32(dds, 20).ShouldBe(8U);
            BitConverter.ToUInt32(dds, 28).ShouldBe(3U);
            Encoding.ASCII.GetString(dds, 84, 4).ShouldBe("DXT1");
            dds.AsSpan(128).ToArray().ShouldBe(LastThreeMipsOf16x16);
        }

        [TestMethod]
        public void Read_WrongMagic_Throws()
        {
            byte[] bytes = CreatePak();
            bytes[0] = (byte)'X';

            Action act = () => new LowMipTexturePak().Read(bytes);

            act.ShouldThrow<InvalidDataException>();
        }
    }
}
