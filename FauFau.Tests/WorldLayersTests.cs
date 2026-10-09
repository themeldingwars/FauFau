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
        private static byte[] Bytes(params object[] values)
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
                        case float f:
                            writer.Write(f);
                            break;
                        case byte[] bytes:
                            writer.Write(bytes);
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

        [TestMethod]
        public void ZoneBounds_ReadsMinAndMax()
        {
            ZoneBounds bounds = ZoneBounds.Read(Bytes(1f, 2f, 3f, 4f, 5f, 6f));

            bounds.Min.ShouldBe(new Vector3(1, 2, 3));
            bounds.Max.ShouldBe(new Vector3(4, 5, 6));
        }

        [TestMethod]
        public void ZoneChunkRange_ReadsXBeforeY()
        {
            ZoneChunkRange range = ZoneChunkRange.Read(Bytes(2u, 10u, 20u, 30u, 40u));

            range.CubeFace.ShouldBe(2U);
            range.MinX.ShouldBe(10U);
            range.MaxX.ShouldBe(20U);
            range.MinY.ShouldBe(30U);
            range.MaxY.ShouldBe(40U);
            range.Contains(15, 35).ShouldBeTrue();
            range.Contains(35, 15).ShouldBeFalse();
        }

        [TestMethod]
        public void ZoneChunkRef_WithoutRecordId_IsZero()
        {
            ZoneChunkRef reference = ZoneChunkRef.Read(Bytes(7u, 8u));

            reference.X.ShouldBe(7U);
            reference.Y.ShouldBe(8U);
            reference.ChunkRecordId.ShouldBe(0U);
        }

        [TestMethod]
        public void ZonePath_ReadsSteps()
        {
            byte[] data = Bytes(11u, 12u, 2u, 1f, 2f, 3f, 0f, 0f, 0f, 1f, 2u, new byte[] { 9, 8 }, 4f, 5f, 6f, 0f, 0f, 1f, 0f, 0u);

            ZonePath path = ZonePath.Read(data);

            path.CceId.ShouldBe(11U);
            path.Steps.Count.ShouldBe(2);
            path.Steps[0].Position.ShouldBe(new Vector3(1, 2, 3));
            path.Steps[0].Action.ShouldBe(new byte[] { 9, 8 });
            path.Steps[1].Orientation.ShouldBe(new Vector4(0, 0, 1, 0));
            path.Steps[1].Action.ShouldBeEmpty();
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void MeldingPerimeter_ReadsOptionalLastByte(bool withByte)
        {
            byte[] data = Bytes("Perimeter", 3u, 10u, new byte[] { 0xFF, 0x03 }, 4u, 2u, "a", "bc");
            if (withByte)
                data = data.Append((byte)7).ToArray();

            MeldingPerimeter perimeter = MeldingPerimeter.Read(data);

            perimeter.Name.ShouldBe("Perimeter");
            perimeter.Bitfield.ShouldBe(new byte[] { 0xFF, 0x03 });
            perimeter.Perimeters.ShouldBe(new[] { "a", "bc" });
            perimeter.Unk2.ShouldBe(withByte ? (byte?)7 : null);
        }

        [TestMethod]
        public void SubZoneRegion_ReadsCellSizeBeforeBitmap()
        {
            byte[] data = Bytes(10540u, 640f, -448f, 4u, 3u, 8f, 2u, new byte[] { 0xFF, 0x0F });

            SubZoneRegion region = SubZoneRegion.Read(data);

            region.Origin.ShouldBe(new Vector2(640, -448));
            region.Width.ShouldBe(4U);
            region.CellSize.ShouldBe(8f);
            region.Bitmap.ShouldBe(new byte[] { 0xFF, 0x0F });
        }

        [TestMethod]
        public void SubZoneRegion_WrongBitmapSize_Throws()
        {
            byte[] data = Bytes(1u, 0f, 0f, 4u, 4u, 8f, 3u, new byte[3]);

            Should.Throw<InvalidDataException>(() => SubZoneRegion.Read(data));
        }

        [TestMethod]
        public void SubZoneGrid_ReadsGridCells()
        {
            byte[] data = Bytes(0u, 2u, 2u, 100u, 200u, 1u, new byte[] { 0, 1, 1, 0 });

            SubZoneGrid grid = SubZoneGrid.Read(data);

            grid.SubZoneIds.ShouldBe(new[] { 100U, 200U });
            grid.Grid.ShouldBe(new byte[] { 0, 1, 1, 0 });
        }

        [TestMethod]
        public void EncounterNameRegistry_ReadsNames()
        {
            EncounterNameRegistry registry = EncounterNameRegistry.Read(Bytes(2u, "chosen", "thumper"));

            registry.Names.ShouldBe(new[] { "chosen", "thumper" });
        }

        [TestMethod]
        public void Read_CutOff_Throws()
        {
            byte[] data = Bytes(2u, 10u);

            Should.Throw<InvalidDataException>(() => ZoneChunkRange.Read(data));
        }

        [TestMethod]
        public void GtLayerReadList_MarkedAndUnmarked_ReadsAll()
        {
            byte[] data = Layer(0x40101, new byte[] { 1, 2 }).Concat(Layer(0x40102, new byte[] { 3 }, false)).ToArray();

            var layers = GtLayer.ReadList(data);

            layers.Select(l => l.Id).ShouldBe(new[] { 0x40101U, 0x40102U });
            layers[1].Data.ShouldBe(new byte[] { 3 });
        }

        [TestMethod]
        public void GtLayerReadList_CutOff_Throws()
        {
            byte[] data = Layer(0x40101, new byte[] { 1, 2 });

            Should.Throw<InvalidDataException>(() => GtLayer.ReadList(data.AsSpan(0, data.Length - 1).ToArray()));
        }
    }
}
