using System;
using System.IO;
using System.IO.Compression;
using Bitter;
using FauFau.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class CziTests
    {
        private static Czi CreatePattern()
        {
            return new Czi
            {
                Width = 4,
                Height = 4,
                IsPattern = true,
                PatternUsage = 4,
                Mips = { new byte[] { 0x55 }, new byte[] { 0x00, 0x55, 0xAA, 0xFF } },
            };
        }

        [TestMethod]
        public void Read_WrittenPattern_ReadsSameMips()
        {
            CreatePattern().Write(out byte[] bytes);
            Czi czi = new Czi();

            czi.Read(bytes);

            czi.Version.ShouldBe(3U);
            czi.Width.ShouldBe(4U);
            czi.IsPattern.ShouldBeTrue();
            czi.PatternUsage.ShouldBe((byte)4);
            czi.Mips.Count.ShouldBe(2);
            czi.Mips[1].ShouldBe(new byte[] { 0x00, 0x55, 0xAA, 0xFF });
        }

        [TestMethod]
        public void Read_Version1_HasNoPatternFields()
        {
            BinaryStream stream = new BinaryStream(new MemoryStream());
            stream.Write.String("CZIM");
            stream.Write.UInt(1);
            stream.Write.UInt(1);
            stream.Write.UInt(1);
            stream.Write.UInt(1);
            stream.Write.UInt(0);
            stream.Write.UInt(1);
            MemoryStream compressed = new MemoryStream();
            using (ZLibStream zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
                zlib.Write(new byte[] { 7 });
            stream.Write.Int((int)compressed.Length);
            stream.Write.ByteArray(compressed.ToArray());
            stream.ByteOffset = 0;
            Czi czi = new Czi();

            czi.Read(stream);

            czi.IsPattern.ShouldBeFalse();
            czi.Mips[0].ShouldBe(new byte[] { 7 });
        }

        [TestMethod]
        public void Read_WrongMagic_Throws()
        {
            CreatePattern().Write(out byte[] bytes);
            bytes[0] = (byte)'X';

            Action act = () => new Czi().Read(bytes);

            act.ShouldThrow<InvalidDataException>();
        }
    }
}
