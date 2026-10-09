using System;
using System.Buffers.Binary;
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
    public class NsrTests
    {
        private static readonly DateTime Recorded = new DateTime(2016, 11, 15, 18, 30, 0, DateTimeKind.Utc);

        internal static Nsr CreateSample(bool compressed)
        {
            Nsr nsr = Nsr.GenerateDummyFile(448);
            nsr.Compressed = compressed;
            nsr.Description.TimeStamp = Recorded;
            nsr.Meta.TimeStamp = Recorded;
            nsr.Meta.CharacterName = "Tester";
            return nsr;
        }

        private static Nsr RoundTrip(Nsr nsr)
        {
            nsr.Write(out byte[] bytes);

            Nsr read = new Nsr();
            read.Read(bytes);
            return read;
        }

        internal static byte[] WriteBytes(Nsr nsr)
        {
            nsr.Write(out byte[] bytes);
            return bytes;
        }

        private static Nsr Read(byte[] bytes)
        {
            Nsr nsr = new Nsr();
            nsr.Read(bytes);
            return nsr;
        }

        internal static byte[] Gzip(byte[] data)
        {
            using MemoryStream compressed = new MemoryStream();
            using (GZipStream gzip = new GZipStream(compressed, CompressionLevel.Fastest, true))
                gzip.Write(data);
            return compressed.ToArray();
        }

        internal static byte[] CreateVersion2()
        {
            Nsr nsr = CreateSample(false);
            nsr.Meta.Version = 3;
            nsr.Meta.Unk2 = new byte[10];
            BinaryStream stream = new BinaryStream(new MemoryStream());
            stream.Write.String("NSRD");
            stream.Write.Int(2);
            stream.Write.Int(7932);
            stream.Write.Int(0);
            stream.Write.Long(0);
            stream.Write.Type(nsr.Meta);
            stream.Write.TypeList(nsr.Packets);
            stream.ByteOffset = 0;
            return stream.Read.ByteArray((int)stream.Length);
        }

        private static BinaryStream StreamOf(string text)
        {
            return new BinaryStream(new MemoryStream(Encoding.ASCII.GetBytes(text)));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void WriteRead_KeepsContent(bool compressed)
        {
            Nsr nsr = CreateSample(compressed);

            Nsr read = RoundTrip(nsr);

            read.Compressed.ShouldBe(compressed);
            read.Description.ProtocolVersion.ShouldBe(19551);
            read.Description.TimeStamp.ShouldBe(Recorded);
            read.Meta.ZoneId.ShouldBe(448);
            read.Meta.CharacterName.ShouldBe("Tester");
            read.Meta.CharacterGUID.ShouldBe(5068907169408127230UL);
            read.Meta.LocalDateString.ShouldBe(nsr.Meta.LocalDateString);
            read.Meta.FirefallVersionString.ShouldBe("Firefall (v1.5.1962)");
            read.Meta.TimeStamp.ShouldBe(Recorded);
            read.Index.Offsets.ShouldBe(new uint[] { 329 });
            read.Description.IndexInterval.ShouldBe(5000);
            read.Packets.Count.ShouldBe(2);
            read.Packets[0].MessageId.ShouldBe((ushort)3);
            read.Packets[0].TimeStamp.ShouldBe(3795714048U);
            read.Packets[1].Data.ShouldBe(nsr.Packets[1].Data);
        }

        [TestMethod]
        public void Read_Version2_ReadsMetaAndPackets()
        {
            byte[] bytes = CreateVersion2();

            Nsr read = Read(bytes);

            read.Description.Version.ShouldBe(2);
            read.Description.ProtocolVersion.ShouldBe(7932);
            read.Description.TimeStamp.ShouldBe(Recorded);
            read.Meta.Version.ShouldBe(3);
            read.Meta.CharacterName.ShouldBe("Tester");
            read.Meta.FirefallVersionString.ShouldBe("Firefall (v1.5.1962)");
            read.Packets.Count.ShouldBe(2);
            read.Packets[0].TimeStamp.ShouldBe(3795714048U);
            read.Truncated.ShouldBeFalse();
        }

        [TestMethod]
        public void Read_GzipTwice_ReadsAllLayers()
        {
            byte[] bytes = Gzip(Gzip(WriteBytes(CreateSample(false))));

            Nsr read = Read(bytes);

            read.GzipLayers.ShouldBe(2);
            read.Compressed.ShouldBeTrue();
            read.Packets.Count.ShouldBe(2);
        }

        [TestMethod]
        public void Read_LastPacketCutOff_KeepsCompletePackets()
        {
            byte[] bytes = WriteBytes(CreateSample(false));

            Nsr read = Read(bytes.AsSpan(0, bytes.Length - 5).ToArray());

            read.Truncated.ShouldBeTrue();
            read.Packets.Count.ShouldBe(1);
        }

        [TestMethod]
        public void Read_GzipCutOff_KeepsCompletePackets()
        {
            Nsr nsr = CreateSample(true);
            for (int i = 0; i < 2000; i++)
                nsr.Packets.Add(new Nsr.Packet { TimeStamp = (uint)i, MessageId = 3, Data = BitConverter.GetBytes(i * 7919) });
            byte[] bytes = WriteBytes(nsr);

            Nsr read = Read(bytes.AsSpan(0, bytes.Length / 2).ToArray());

            read.Truncated.ShouldBeTrue();
            read.Packets.Count.ShouldBeGreaterThan(2);
            read.Packets.Count.ShouldBeLessThan(nsr.Packets.Count);
        }

        [TestMethod]
        public void Read_GarbageDescriptionTime_UsesMetaTime()
        {
            byte[] bytes = WriteBytes(CreateSample(false));
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(32), -1);

            Nsr read = Read(bytes);

            read.Description.TimeStamp.ShouldBe(Recorded);
        }

        [TestMethod]
        public void Read_DataOffsetPastEnd_Throws()
        {
            byte[] bytes = WriteBytes(CreateSample(false));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), bytes.Length + 1);

            Action act = () => Read(bytes);

            act.ShouldThrow<InvalidDataException>();
        }

        [TestMethod]
        public void Read_UnknownVersion_Throws()
        {
            byte[] bytes = WriteBytes(CreateSample(false));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 3);

            Action act = () => Read(bytes);

            act.ShouldThrow<NotSupportedException>();
        }

        [TestMethod]
        public void Meta_ZoneInstanceGuidAndClock_ReadFromUnknownBytes()
        {
            Nsr nsr = CreateSample(false);
            BinaryPrimitives.WriteUInt64LittleEndian(nsr.Meta.Unk2.AsSpan(2), 0x1122334455667788);
            BinaryPrimitives.WriteUInt64LittleEndian(nsr.Meta.Unk2.AsSpan(10), 42);

            Nsr read = RoundTrip(nsr);

            read.Meta.ZoneInstanceGuid.ShouldBe(0x1122334455667788UL);
            read.Meta.ClockSync.ShouldBe(42UL);
        }

        [TestMethod]
        public void GenerateDummyFile_TimeOfDay_IsKept()
        {
            Nsr nsr = Nsr.GenerateDummyFile(448, 0.75);

            Nsr read = RoundTrip(nsr);

            read.Meta.TimeOfDay.ShouldBe(0.75);
            BinaryPrimitives.ReadDoubleLittleEndian(read.Meta.Unk3.AsSpan(18)).ShouldBe(0.75);
        }

        [TestMethod]
        public void Read_LeavesCallerStreamOpen()
        {
            CreateSample(false).Write(out byte[] bytes);
            BinaryStream stream = new BinaryStream(new MemoryStream(bytes));

            new Nsr().Read(stream);
            stream.ByteOffset = 0;
            string magic = stream.Read.String(4);

            magic.ShouldBe("NSRD");
        }

        [TestMethod]
        public void Read_NotAReplay_Throws()
        {
            byte[] bytes = Encoding.ASCII.GetBytes("NOPE and some more bytes for the header");

            Action act = () => new Nsr().Read(bytes);

            act.ShouldThrow<InvalidDataException>();
        }

        [TestMethod]
        public void ReadNullTerminatedString_ReadsUpToTerminator()
        {
            BinaryStream stream = StreamOf("abc\0def");

            string text = Nsr.ReadNullTerminatedString(stream);

            text.ShouldBe("abc");
            stream.ByteOffset.ShouldBe(4);
        }

        [TestMethod]
        public void ReadNullTerminatedString_Empty_ReturnsEmpty()
        {
            BinaryStream stream = StreamOf("\0x");

            string text = Nsr.ReadNullTerminatedString(stream);

            text.ShouldBe("");
            stream.ByteOffset.ShouldBe(1);
        }

        [TestMethod]
        public void ReadNullTerminatedString_NoTerminator_ReadsToEnd()
        {
            BinaryStream stream = StreamOf("abc");

            string text = Nsr.ReadNullTerminatedString(stream);

            text.ShouldBe("abc");
            stream.ByteOffset.ShouldBe(3);
        }
    }
}
