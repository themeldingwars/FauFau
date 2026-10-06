using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Bitter;
using FauFau.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class WorldMapTests
    {
        private static readonly byte[] Payload = CreatePayload();

        private static byte[] CreatePayload()
        {
            BinaryStream stream = new BinaryStream(new MemoryStream());
            stream.Write.UInt(10);
            stream.Write.UInt(3);
            stream.Write.ByteArray(new byte[] { 1, 2, 3 });
            stream.Write.UInt(60);
            stream.Write.UInt(0);
            stream.ByteOffset = 0;
            return stream.Read.ByteArray((int)stream.Length);
        }

        private static byte[] CreateWorldMap(bool compressed)
        {
            BinaryStream stream = new BinaryStream(new MemoryStream());
            stream.Write.String("GTNO");
            stream.Write.UInt(2);
            stream.Write.UInt(0);
            stream.Write.UInt(45);
            stream.Write.UInt(1074);
            stream.Write.UInt(7);
            stream.Write.Float(-512);
            stream.Write.Float(256);
            stream.Write.Float(1024);
            stream.Write.FloatArray(new float[] { -1, -2, -3, 1, 2, 3 });
            stream.Write.Byte((byte)(compressed ? 1 : 0));
            if (compressed)
            {
                MemoryStream deflated = new MemoryStream();
                using (ZLibStream zlib = new ZLibStream(deflated, CompressionLevel.Optimal, true))
                    zlib.Write(Payload);
                stream.Write.UInt(100);
                stream.Write.UInt((uint)(8 + deflated.Length));
                stream.Write.UInt((uint)Payload.Length);
                stream.Write.UInt((uint)deflated.Length);
                stream.Write.ByteArray(deflated.ToArray());
            }
            else
            {
                stream.Write.ByteArray(Payload);
            }
            stream.ByteOffset = 0;
            return stream.Read.ByteArray((int)stream.Length);
        }

        private static void WriteString(BinaryStream stream, string text)
        {
            stream.Write.Int(text.Length + 1);
            stream.Write.ByteArray(Encoding.ASCII.GetBytes(text + "\0"));
        }

        private static byte[] CreateWorldDir()
        {
            BinaryStream stream = new BinaryStream(new MemoryStream());
            stream.Write.UInt(0);
            stream.Write.UInt(4 + 4 + 4 + 1 + 4 + 6 + 4);
            stream.Write.String("WMAP");
            stream.Write.UInt(3);
            stream.Write.UInt(1);
            stream.Write.Byte(0);
            WriteString(stream, "world");
            stream.Write.UInt(123);
            stream.Write.UInt(100);
            stream.Write.UInt(4 + 29 + 5 * 4 + 6 * 4 + 3 * 4);
            WriteString(stream, "0_07_0000001074_opt.worldMap");
            stream.Write.UInt(0);
            stream.Write.UInt(578026);
            stream.Write.UInt(0);
            stream.Write.UInt(7);
            stream.Write.UInt(1074);
            stream.Write.FloatArray(new float[] { -1, -2, -3, 1, 2, 3, -512, 256, 1024 });
            stream.ByteOffset = 0;
            return stream.Read.ByteArray((int)stream.Length);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void Read_WorldMap_ReadsHeaderAndLayers(bool compressed)
        {
            byte[] bytes = CreateWorldMap(compressed);
            WorldMap map = new WorldMap();

            map.Read(bytes);

            map.Compressed.ShouldBe(compressed);
            map.EntryId.ShouldBe(1074U);
            map.ZoomLevel.ShouldBe(7U);
            map.Size.ShouldBe(1024f);
            map.BoundsMax.z.ShouldBe(3f);
            map.Data.ShouldBe(Payload);
            map.Layers.Count.ShouldBe(2);
            map.Layers[0].Id.ShouldBe(10U);
            map.Layers[0].Data.ShouldBe(new byte[] { 1, 2, 3 });
            map.Layers[1].Data.ShouldBeEmpty();
        }

        [TestMethod]
        public void Read_WrongMagic_Throws()
        {
            byte[] bytes = CreateWorldMap(false);
            bytes[0] = (byte)'X';

            Action act = () => new WorldMap().Read(bytes);

            act.ShouldThrow<InvalidDataException>();
        }

        [TestMethod]
        public void Read_WorldDir_ReadsTiles()
        {
            byte[] bytes = CreateWorldDir();
            WorldDir dir = new WorldDir();

            dir.Read(bytes);

            dir.Version.ShouldBe(3U);
            dir.Path.ShouldBe("world");
            dir.DirectorySize.ShouldBe(123U);
            dir.Tiles.Count.ShouldBe(1);
            dir.Tiles[0].Name.ShouldBe("0_07_0000001074_opt.worldMap");
            dir.Tiles[0].DataLength.ShouldBe(578026U);
            dir.Tiles[0].EntryId.ShouldBe(1074U);
            dir.Tiles[0].Size.ShouldBe(1024f);
        }
    }
}
