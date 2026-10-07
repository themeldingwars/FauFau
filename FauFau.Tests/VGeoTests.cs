using System;
using System.IO;
using System.Linq;
using Bitter;
using FauFau.Formats;
using FauFau.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class VGeoTests
    {
        private static byte[] CreateVertices(int count, int stride)
        {
            BinaryStream stream = new BinaryStream(new MemoryStream());
            for (int i = 0; i < count; i++)
            {
                stream.Write.FloatArray(new float[] { i, i * 2, i * 3 });
                stream.Write.ByteArray(new byte[stride - 12]);
            }
            stream.ByteOffset = 0;
            return stream.Read.ByteArray((int)stream.Length);
        }

        private static byte[] CreatePage(byte[] vertices, bool fullStride)
        {
            (byte[] properties, byte[] stream) = TestLzma.Compress(vertices);
            return new[] { (byte)(fullStride ? 1 : 0) }.Concat(properties).Concat(stream).ToArray();
        }

        private static (byte[] Index, byte[] Data) CreateFiles(byte[] vertices, byte[] page)
        {
            byte[] usedSlots = new byte[VGeoIndex.VerticesPerPage / 8];
            usedSlots[0] = 0b101;
            usedSlots[1] = 0b1;

            BinaryStream stream = new BinaryStream(new MemoryStream());
            stream.Write.Int(3);
            stream.Write.Byte(1);
            stream.Write.Int(2);
            stream.Write.ULong(0);
            stream.Write.UInt(0);
            stream.Write.UShort(0);
            stream.Write.ULong(10);
            stream.Write.UInt(Checksum.Crc32(vertices));
            stream.Write.UShort(3);
            stream.Write.UShort((ushort)page.Length);
            stream.Write.ByteArray(usedSlots);
            stream.Write.Int(1);
            stream.Write.UIntArray(new uint[] { 4, 5, 6 });
            stream.ByteOffset = 0;

            byte[] data = new byte[10].Concat(page).ToArray();
            return (stream.Read.ByteArray((int)stream.Length), data);
        }

        [TestMethod]
        public void Read_Index_ReadsPagesAndTextures()
        {
            byte[] vertices = CreateVertices(3, VGeoPage.FullStride);
            (byte[] indexBytes, _) = CreateFiles(vertices, CreatePage(vertices, true));
            VGeoIndex index = new VGeoIndex();

            index.Read(indexBytes);

            index.Version.ShouldBe(3);
            index.Pages.Count.ShouldBe(2);
            index.Pages[0].VertexCount.ShouldBe((ushort)0);
            index.Pages[1].Offset.ShouldBe(10UL);
            index.Pages[1].VertexCount.ShouldBe((ushort)3);
            index.Textures[0].ShouldBe(new uint[] { 4, 5, 6 });
        }

        [TestMethod]
        [DataRow(true, VGeoPage.FullStride)]
        [DataRow(false, VGeoPage.ShortStride)]
        public void ReadPage_Compressed_DecodesVertices(bool fullStride, int stride)
        {
            byte[] vertices = CreateVertices(3, stride);
            (byte[] indexBytes, byte[] data) = CreateFiles(vertices, CreatePage(vertices, fullStride));
            VGeoIndex index = new VGeoIndex();
            index.Read(indexBytes);

            VGeoPage page = index.ReadPage(new MemoryStream(data), index.Pages[1]);

            page.Stride.ShouldBe(stride);
            page.VertexCount.ShouldBe(3);
            page.Crc.ShouldBe(index.Pages[1].Crc);
            page.GetPosition(2).z.ShouldBe(6f);
            page.GetSlots().ShouldBe(new[] { 0, 2, 8 });
        }

        [TestMethod]
        public void Read_UnsupportedVersion_Throws()
        {
            byte[] bytes = BitConverter.GetBytes(4);

            Action act = () => new VGeoIndex().Read(bytes);

            act.ShouldThrow<NotSupportedException>();
        }
    }
}
