using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Bitter;
using FauFau.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class WorldLayersTests
    {
        internal static byte[] Bytes(params object[] values)
        {
            using MemoryStream stream = new MemoryStream();
            using (System.IO.BinaryWriter writer = new System.IO.BinaryWriter(stream, Encoding.UTF8, true))
            {
                foreach (object value in values)
                {
                    switch (value)
                    {
                        case uint u:
                            writer.Write(u);
                            break;
                        case ushort u:
                            writer.Write(u);
                            break;
                        case byte b:
                            writer.Write(b);
                            break;
                        case float f:
                            writer.Write(f);
                            break;
                        case byte[] bytes:
                            writer.Write(bytes);
                            break;
                        case object[] nested:
                            writer.Write(Bytes(nested));
                            break;
                        case string s:
                            writer.Write((uint)Encoding.UTF8.GetByteCount(s));
                            writer.Write(Encoding.UTF8.GetBytes(s));
                            break;
                    }
                }
            }
            return stream.ToArray();
        }

        internal static byte[] Layer(uint id, byte[] data, bool marked = true)
        {
            BinaryStream stream = new BinaryStream(new MemoryStream());
            if (marked)
                stream.Write.ULong(GtLayer.Marker);

            stream.Write.UInt(id);
            stream.Write.UInt((uint)data.Length);
            stream.Write.ByteArray(data);
            stream.ByteOffset = 0;
            return stream.Read.ByteArray((int)stream.Length);
        }

        private static T Read<T>(uint parentId, uint id, byte[] data) where T : GtLayer
        {
            return GtLayer.ReadList(Layer(id, data), parentId).Single().ShouldBeOfType<T>();
        }

        [TestMethod]
        public void ZoneBounds_ReadsMinAndMax()
        {
            byte[] data = Bytes(1f, 2f, 3f, 4f, 5f, 6f);

            ZoneBoundsLayer bounds = Read<ZoneBoundsLayer>(WorldLayerIds.ZoneRoot, WorldLayerIds.Bounds, data);
            byte[] written = bounds.GetData();

            bounds.Min.ShouldBe(new Vector3(1, 2, 3));
            bounds.Max.ShouldBe(new Vector3(4, 5, 6));
            written.ShouldBe(data);
        }

        [TestMethod]
        public void ZoneChunkRange_ReadsXBeforeY()
        {
            byte[] data = Bytes(2u, 10u, 20u, 30u, 40u);

            ZoneChunkRangeLayer range = Read<ZoneChunkRangeLayer>(WorldLayerIds.ChunkInfo, WorldLayerIds.ChunkRange, data);
            byte[] written = range.GetData();

            range.CubeFace.ShouldBe(2U);
            range.MinX.ShouldBe(10U);
            range.MaxX.ShouldBe(20U);
            range.MinY.ShouldBe(30U);
            range.MaxY.ShouldBe(40U);
            range.Contains(15, 35).ShouldBeTrue();
            range.Contains(35, 15).ShouldBeFalse();
            written.ShouldBe(data);
        }

        [TestMethod]
        public void ZoneChunkRef2_HasNoRecordId()
        {
            byte[] data = Bytes(7u, 8u);

            ZoneChunkRefLayer reference = Read<ZoneChunkRefLayer>(WorldLayerIds.ChunkInfo, WorldLayerIds.ChunkRef2, data);
            byte[] written = reference.GetData();

            reference.X.ShouldBe(7U);
            reference.Y.ShouldBe(8U);
            reference.HasChunkRecordId.ShouldBeFalse();
            written.ShouldBe(data);
        }

        [TestMethod]
        public void ZonePath_ReadsSteps()
        {
            byte[] data = Bytes(11u, 12u, 2u, 1f, 2f, 3f, 0f, 0f, 0f, 1f, 2u, new byte[] { 9, 8 }, 4f, 5f, 6f, 0f, 0f, 1f, 0f, 0u);

            ZonePathLayer path = Read<ZonePathLayer>(WorldLayerIds.ZoneRoot, WorldLayerIds.Path, data);
            byte[] written = path.GetData();

            path.CceId.ShouldBe(11U);
            path.Steps.Count.ShouldBe(2);
            path.Steps[0].Position.ShouldBe(new Vector3(1, 2, 3));
            path.Steps[0].Action.ShouldBe(new byte[] { 9, 8 });
            path.Steps[1].Orientation.ShouldBe(new Vector4(0, 0, 1, 0));
            path.Steps[1].Action.ShouldBeEmpty();
            written.ShouldBe(data);
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(9)]
        public void MeldingPerimeter_ReadsOptionalEnd(int end)
        {
            byte[] data = Bytes("Perimeter", 3u, 10u, new byte[] { 0xFF, 0x03 }, 4u, 2u, "a", "bc", Bytes((byte)7, 0f, 120f).Take(end).ToArray());

            MeldingPerimeterLayer perimeter = Read<MeldingPerimeterLayer>(WorldLayerIds.Melding, WorldLayerIds.MeldingPerimeter, data);
            byte[] written = perimeter.GetData();

            perimeter.Name.ShouldBe("Perimeter");
            perimeter.Bitfield.ShouldBe(new byte[] { 0xFF, 0x03 });
            perimeter.Perimeters.ShouldBe(new[] { "a", "bc" });
            perimeter.Unk2.ShouldBe(end > 0 ? (byte?)7 : null);
            perimeter.Unk4.ShouldBe(end > 1 ? 120f : null);
            written.ShouldBe(data);
        }

        [TestMethod]
        public void SubZoneRegion_ReadsCellSizeBeforeBitmap()
        {
            byte[] data = Bytes(10540u, 640f, -448f, 4u, 3u, 8f, 2u, new byte[] { 0xFF, 0x0F });

            SubZoneRegionLayer region = Read<SubZoneRegionLayer>(WorldLayerIds.ZoneRoot, WorldLayerIds.SubZoneRegion, data);
            byte[] written = region.GetData();

            region.Origin.ShouldBe(new Vector2(640, -448));
            region.Width.ShouldBe(4U);
            region.CellSize.ShouldBe(8f);
            region.Bitmap.ShouldBe(new byte[] { 0xFF, 0x0F });
            written.ShouldBe(data);
        }

        [TestMethod]
        public void SubZoneRegion_WrongBitmapSize_StaysData()
        {
            byte[] data = Bytes(1u, 0f, 0f, 4u, 4u, 8f, 3u, new byte[3]);

            GtDataLayer layer = Read<GtDataLayer>(WorldLayerIds.ZoneRoot, WorldLayerIds.SubZoneRegion, data);

            layer.Data.ShouldBe(data);
        }

        [TestMethod]
        public void SubZoneGrid_ReadsGridCells()
        {
            byte[] data = Bytes(0u, 2u, 2u, 100u, 200u, 1u, new byte[] { 0, 1, 1, 0 });

            SubZoneGridLayer grid = Read<SubZoneGridLayer>(WorldLayerIds.SubChunk, WorldLayerIds.SubZoneGrid, data);
            byte[] written = grid.GetData();

            grid.SubZoneIds.ShouldBe(new[] { 100U, 200U });
            grid.Grid.ShouldBe(new byte[] { 0, 1, 1, 0 });
            written.ShouldBe(data);
        }

        [TestMethod]
        [DataRow(WorldLayerIds.ZoneRoot, WorldLayerIds.PropEncounterNameRegistry)]
        [DataRow(WorldLayerIds.Lod, WorldLayerIds.ChunkEncounterNameRegistry)]
        [DataRow(WorldLayerIds.SubChunk, WorldLayerIds.ChunkPropEncounterNameRegistry)]
        public void EncounterNameRegistry_ReadsNames(uint parentId, uint id)
        {
            byte[] data = Bytes(2u, "chosen", "thumper");

            EncounterNameRegistryLayer registry = Read<EncounterNameRegistryLayer>(parentId, id, data);
            byte[] written = registry.GetData();

            registry.Names.ShouldBe(new[] { "chosen", "thumper" });
            written.ShouldBe(data);
        }

        [TestMethod]
        [DataRow(12)]
        [DataRow(24)]
        public void Environment10000_ReadsOptionalSecondVector(int length)
        {
            byte[] data = Bytes(1f, 2f, 3f, 4f, 5f, 6f).Take(length).ToArray();

            Environment10000Layer layer = Read<Environment10000Layer>(WorldLayerIds.DefaultEnvironment, WorldLayerIds.Environment10000, data);
            byte[] written = layer.GetData();

            layer.Data1.ShouldBe(new Vector3(1, 2, 3));
            layer.Data2.ShouldBe(length > 12 ? new Vector3(4, 5, 6) : null);
            written.ShouldBe(data);
        }

        [TestMethod]
        public void ChunkRange_CutOff_StaysData()
        {
            byte[] data = Bytes(2u, 10u);

            GtDataLayer layer = Read<GtDataLayer>(WorldLayerIds.ChunkInfo, WorldLayerIds.ChunkRange, data);

            layer.Data.ShouldBe(data);
        }

        [TestMethod]
        public void Containers_BelowChunkBlocks_ReadTheirChildren()
        {
            byte[] data = Layer(WorldLayerIds.PropEnvironment, Layer(0x3E8, Bytes(1u)));

            GtContainerLayer props = Read<GtContainerLayer>(WorldLayerIds.SubChunk, WorldLayerIds.Props, data);
            byte[] written = props.GetData();

            props.Find(WorldLayerIds.PropEnvironment).ShouldBeOfType<GtContainerLayer>().Find(0x3E8).ShouldBeOfType<GtDataLayer>();
            written.ShouldBe(data);
        }
    }
}
