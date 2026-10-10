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
            return WorldLayersTests.Layer(id, content.SelectMany(c => c).ToArray());
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
        public void ReadHeader_Marked_SkipsMarker()
        {
            BinaryStream stream = StreamOf(Layer(0x21000, UInts(1, 2, 3)));

            (uint id, uint length) = GtLayer.ReadHeader(stream);

            id.ShouldBe(0x21000U);
            length.ShouldBe(12U);
        }

        [TestMethod]
        public void Read_ZoneRoot_ReadsChildrenByParent()
        {
            byte[] bytes = Layer(0x30000, Layer(0x20000, UInts(7)), Layer(0x20400, Layer(0x10101, UInts(4, 5, 6))));

            GtLayer root = GtLayer.Read(StreamOf(bytes));

            GtContainerLayer container = root.ShouldBeOfType<GtContainerLayer>();
            container.Children.Select(c => c.Id).ShouldBe(new uint[] { 0x20000, 0x20400 });
            container.Find<ZoneSkyboxLayer>().SkyboxRecordId.ShouldBe(7U);
            container.Find(0x20400).ShouldBeOfType<GtContainerLayer>().Find<ZoneChunkRefLayer>().ChunkRecordId.ShouldBe(6U);
        }

        [TestMethod]
        public void Read_UnknownLayerHoldingLayers_StaysData()
        {
            byte[] content = Layer(0x50011, UInts(1));
            byte[] bytes = Layer(0x21300, content);

            GtLayer layer = GtLayer.Read(StreamOf(bytes), WorldLayerIds.ZoneRoot);

            layer.ShouldBeOfType<GtDataLayer>().Data.ShouldBe(content);
        }

        [TestMethod]
        public void Read_SameIdBelowOtherParent_StaysData()
        {
            byte[] perimeter = WorldLayersTests.Bytes("Perimeter", 3u, 8u, new byte[] { 0xFF }, 0u, 0u);

            GtLayer below = GtLayer.ReadList(Layer(WorldLayerIds.MeldingPerimeter, perimeter), WorldLayerIds.Melding).Single();
            GtLayer top = GtLayer.ReadList(Layer(WorldLayerIds.MeldingPerimeter, perimeter)).Single();

            below.ShouldBeOfType<MeldingPerimeterLayer>();
            top.ShouldBeOfType<GtDataLayer>();
        }

        [TestMethod]
        public void Read_TypedLayerWithBytesLeft_StaysData()
        {
            byte[] bytes = Layer(0x21000, UInts(1, 2, 3, 4, 5, 6, 7));

            GtLayer layer = GtLayer.Read(StreamOf(bytes), WorldLayerIds.ZoneRoot);

            layer.ShouldBeOfType<GtDataLayer>().Data.ShouldBe(UInts(1, 2, 3, 4, 5, 6, 7));
        }

        [TestMethod]
        public void Read_LengthPastEnd_Throws()
        {
            byte[] bytes = Layer(0x21000, UInts(1, 2, 3));

            Action act = () => GtLayer.Read(StreamOf(bytes.AsSpan(0, bytes.Length - 1).ToArray()));

            act.ShouldThrow<InvalidDataException>();
        }

        [TestMethod]
        public void ToArray_ReadLayers_WritesSameBytes()
        {
            byte[] bytes = Layer(0x30000,
                Layer(0x21000, UInts(1, 2, 3, 4, 5, 6)),
                Layer(0x20200, Layer(5, WorldLayersTests.Bytes("Perimeter", 3u, 8u, new byte[] { 0xFF }, 0u, 1u, "a"))),
                Layer(0x21300, UInts(9, 9)));
            byte[] unmarked = WorldLayersTests.Layer(0x21000, UInts(1, 2, 3, 4, 5, 6), false);

            byte[] written = GtLayer.Read(StreamOf(bytes)).ToArray();
            byte[] writtenUnmarked = GtLayer.ReadList(unmarked, WorldLayerIds.ZoneRoot).Single().ToArray();

            written.ShouldBe(bytes);
            writtenUnmarked.ShouldBe(unmarked);
        }

        [TestMethod]
        public void ReadList_MarkedAndUnmarked_ReadsAll()
        {
            byte[] data = Layer(0x40203, new byte[] { 1, 2 }).Concat(WorldLayersTests.Layer(0x40208, new byte[] { 3 }, false)).ToArray();

            var layers = GtLayer.ReadList(data, WorldLayerIds.SubChunk);

            layers.Select(l => l.Id).ShouldBe(new[] { 0x40203U, 0x40208U });
            layers[0].HasMarker.ShouldBeTrue();
            layers[1].HasMarker.ShouldBeFalse();
            layers[1].ShouldBeOfType<GtDataLayer>().Data.ShouldBe(new byte[] { 3 });
        }

        [TestMethod]
        public void ReadList_CutOff_Throws()
        {
            byte[] data = Layer(0x40203, new byte[] { 1, 2 });

            Action act = () => GtLayer.ReadList(data.AsSpan(0, data.Length - 1));

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
