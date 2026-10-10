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
    public class ZoneTests
    {
        private static BinaryStream CreateHeader(string name)
        {
            BinaryStream stream = new BinaryStream(new MemoryStream());
            stream.Write.String("ZONE");
            stream.Write.Int(8);
            stream.Write.Long(1461290437107);
            stream.Write.Int(name.Length + 1);
            stream.Write.ByteArray(Encoding.ASCII.GetBytes(name + "\0"));
            stream.Write.ULong(GtLayer.Marker);
            stream.Write.UInt(Zone.RootLayerId);
            stream.Write.UInt(0);
            stream.ByteOffset = 0;
            return stream;
        }

        [TestMethod]
        public void Read_Header_ReadsMillisecondTimestampAndName()
        {
            BinaryStream stream = CreateHeader("New Eden");

            Zone zone = new Zone();

            zone.Read(stream);

            zone.Magic.ShouldBe("ZONE");
            zone.Version.ShouldBe(8);
            zone.TimeStamp.ShouldBe(new DateTime(2016, 4, 22, 2, 0, 37, 107, DateTimeKind.Utc));
            zone.Name.ShouldBe("New Eden");
            zone.Root.Id.ShouldBe(Zone.RootLayerId);
            stream.ByteOffset.ShouldBe(stream.Length);
        }

        [TestMethod]
        public void GetChunks_RefAndRef2_TakeCubeFaceOfTheirRange()
        {
            byte[] range = WorldLayersTests.Layer(Zone.ChunkRangeLayerId, UInts(2, 10, 20, 30, 40));
            byte[] reference = WorldLayersTests.Layer(Zone.ChunkRefLayerId, UInts(12, 34, 99));
            byte[] reference2 = WorldLayersTests.Layer(Zone.ChunkRef2LayerId, UInts(13, 35));
            byte[] chunkInfo = WorldLayersTests.Layer(Zone.ChunkInfoLayerId, range.Concat(reference).Concat(reference2).ToArray());
            Zone zone = new Zone { Root = (GtContainerLayer)GtLayer.Read(new BinaryStream(new MemoryStream(WorldLayersTests.Layer(Zone.RootLayerId, chunkInfo)))) };

            var chunks = zone.GetChunks();

            chunks.Count.ShouldBe(2);
            chunks[0].FileName.ShouldBe("2_0012_0034.gtchunk");
            chunks[0].ChunkRecordId.ShouldBe(99U);
            chunks[1].FileName.ShouldBe("2_0013_0035.gtchunk");
            chunks[1].ChunkRecordId.ShouldBe(0U);
        }

        [TestMethod]
        public void Write_ReadZone_WritesSameBytes()
        {
            BinaryStream stream = CreateHeader("New Eden");
            byte[] bytes = stream.Read.ByteArray((int)stream.Length);
            stream.ByteOffset = 0;
            Zone zone = new Zone();
            zone.Read(stream);
            BinaryStream written = new BinaryStream(new MemoryStream());

            zone.Write(written);
            written.ByteOffset = 0;
            byte[] result = written.Read.ByteArray((int)written.Length);

            result.ShouldBe(bytes);
        }

        private static byte[] UInts(params uint[] values)
        {
            return values.SelectMany(BitConverter.GetBytes).ToArray();
        }
    }
}
