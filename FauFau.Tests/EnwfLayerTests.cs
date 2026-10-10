using System.Linq;
using System.Numerics;
using FauFau.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class EnwfLayerTests
    {
        private static readonly byte[] Havok = { 0x57, 0xE0, 0xE0, 0x57 };

        private static byte[] Header(ushort revision)
        {
            return WorldLayersTests.Bytes(EnwfLayer.MagicId, (ushort)1, revision, 5u, 1u, 7u);
        }

        private static EnwfLayer Read(uint id, byte[] data)
        {
            return GtLayer.ReadList(WorldLayersTests.Layer(id, data), WorldLayerIds.SubChunk).Single().ShouldBeOfType<EnwfLayer>();
        }

        [TestMethod]
        public void Read_Revision2_ReadsMeshBeforeHavokData()
        {
            byte[] data = Header(2)
                .Concat(WorldLayersTests.Bytes(1u, 3u, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f))
                .Concat(WorldLayersTests.Bytes(2u, 1u, 0x60002u, (ushort)0, (ushort)1, (ushort)2, 1u, 0x30001u, (byte)2, (byte)1, (byte)0))
                .Concat(WorldLayersTests.Bytes(1u, 2u, 9u, new byte[] { 3, 4 }))
                .Concat(WorldLayersTests.Bytes(1u, 1f, 2f, 3f, 4f, 1u, new byte[] { 5 }, (byte)6, (ushort)7, 1u, (ushort)8))
                .Concat(Havok).ToArray();

            EnwfLayer enwf = Read(WorldLayerIds.StaticGeometryCollision, data);
            byte[] written = enwf.GetData();

            enwf.HasMesh.ShouldBeTrue();
            enwf.PhysicsMaterialIds.ShouldBe(new[] { 7U });
            enwf.VertBlocks.Single().ShouldBe(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY });
            enwf.IndiceBlocks[0].Indices.ShouldBe(new ushort[] { 0, 1, 2 });
            enwf.IndiceBlocks[1].IndiceType.ShouldBe(EnwfLayer.IndiceBlock.IndiceTypes.Bytes);
            enwf.IndiceBlocks[1].Indices.ShouldBe(new ushort[] { 2, 1, 0 });
            enwf.MatItems.Single().Data.ShouldBe(new byte[] { 3, 4 });
            enwf.MoppBlocks.Single().Shorts.ShouldBe(new ushort[] { 8 });
            enwf.HavokData.ShouldBe(Havok);
            written.ShouldBe(data);
        }

        [TestMethod]
        public void Read_OtherRevision_HasOnlyHavokData()
        {
            byte[] data = Header(1).Concat(Havok).ToArray();

            EnwfLayer enwf = Read(WorldLayerIds.WaterCollision, data);
            byte[] written = enwf.GetData();

            enwf.HasMesh.ShouldBeFalse();
            enwf.VertBlocks.ShouldBeEmpty();
            enwf.HavokData.ShouldBe(Havok);
            written.ShouldBe(data);
        }

        [TestMethod]
        public void Read_WrongMagic_StaysData()
        {
            byte[] data = WorldLayersTests.Bytes(1u, 2u, 3u, 0u);

            GtLayer layer = GtLayer.ReadList(WorldLayersTests.Layer(WorldLayerIds.StaticGeometryCollision, data), WorldLayerIds.SubChunk).Single();

            layer.ShouldBeOfType<GtDataLayer>().Data.ShouldBe(data);
        }

        [TestMethod]
        public void Read_ZoneWorldChunkImport_IsEnwf()
        {
            byte[] data = Header(1).Concat(Havok).ToArray();

            GtLayer layer = GtLayer.ReadList(WorldLayersTests.Layer(WorldLayerIds.WorldChunkImport, data), WorldLayerIds.ZoneRoot).Single();

            layer.ShouldBeOfType<EnwfLayer>().HavokData.ShouldBe(Havok);
        }
    }
}
