using System;
using System.IO;
using System.Linq;
using System.Text;
using Bitter;
using FauFau.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class GtLayerTests
    {
        private static byte[] Layer(uint id, params byte[][] content)
        {
            byte[] data = content.SelectMany(c => c).ToArray();
            BinaryStream stream = new BinaryStream(new MemoryStream());
            stream.Write.ULong(GtLayer.Marker);
            stream.Write.UInt(id);
            stream.Write.UInt((uint)data.Length);
            stream.Write.ByteArray(data);
            stream.ByteOffset = 0;
            return stream.Read.ByteArray((int)stream.Length);
        }

        private static byte[] UInts(params uint[] values)
        {
            return values.SelectMany(BitConverter.GetBytes).ToArray();
        }

        private static BinaryStream StreamOf(byte[] bytes)
        {
            return new BinaryStream(new MemoryStream(bytes));
        }

        [TestMethod]
        public void ReadHeader_Unmarked_ReadsIdAndLength()
        {
            BinaryStream stream = StreamOf(UInts(100, 45));

            (uint id, uint length) = GtLayer.ReadHeader(stream);

            id.ShouldBe(100U);
            length.ShouldBe(45U);
        }

        [TestMethod]
        public void Read_NestedLayers_BuildsTree()
        {
            byte[] bytes = Layer(0x30000, Layer(0x21000, UInts(1, 2, 3)), Layer(0x20400, Layer(0x10101, UInts(4, 5, 6))));

            GtLayer root = GtLayer.Read(StreamOf(bytes));

            root.IsContainer.ShouldBeTrue();
            root.Children.Select(c => c.Id).ShouldBe(new uint[] { 0x21000, 0x20400 });
            root.Find(0x21000).Data.ShouldBe(UInts(1, 2, 3));
            root.Find(0x20400).Find(0x10101).Data.ShouldBe(UInts(4, 5, 6));
        }

        [TestMethod]
        public void Read_DataStartingWithMarkerButNotOnlyLayers_StaysLeaf()
        {
            byte[] content = Layer(0x50011, UInts(1)).Concat(new byte[] { 9, 9, 9 }).ToArray();
            byte[] bytes = Layer(0x21500, content);

            GtLayer layer = GtLayer.Read(StreamOf(bytes));

            layer.IsContainer.ShouldBeFalse();
            layer.Data.ShouldBe(content);
        }

        [TestMethod]
        public void Read_LengthPastEnd_Throws()
        {
            byte[] bytes = Layer(0x21000, UInts(1, 2, 3));

            Action act = () => GtLayer.Read(StreamOf(bytes.AsSpan(0, bytes.Length - 1).ToArray()));

            act.ShouldThrow<InvalidDataException>();
        }

        [TestMethod]
        public void Zone_GetChunks_UsesCubeFaceOfRange()
        {
            byte[] chunkInfo = Layer(Zone.ChunkInfoLayerId,
                Layer(Zone.ChunkRangeLayerId, UInts(5, 1130, 1135, 1495, 1500)),
                Layer(Zone.ChunkRefLayerId, UInts(1133, 1497, 42)),
                Layer(Zone.ChunkRefLayerId, UInts(7, 8, 43)));
            BinaryStream stream = new BinaryStream(new MemoryStream());
            stream.Write.String("ZONE");
            stream.Write.Int(8);
            stream.Write.Long(0);
            stream.Write.Int(4);
            stream.Write.ByteArray(Encoding.ASCII.GetBytes("Abc\0"));
            stream.Write.ByteArray(Layer(Zone.RootLayerId, chunkInfo));
            stream.ByteOffset = 0;
            Zone zone = new Zone();

            zone.Read(stream);
            var chunks = zone.GetChunks();

            chunks.Select(c => c.FileName).ShouldBe(new[] { "5_1133_1497.gtchunk", "0_0007_0008.gtchunk" });
            chunks[0].ChunkRecordId.ShouldBe(42U);
        }
    }
}
