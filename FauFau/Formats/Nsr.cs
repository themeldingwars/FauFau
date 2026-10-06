using Bitter;
using FauFau.Util.CommmonDataTypes;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using BinaryReader = Bitter.BinaryReader;
using BinaryWriter = Bitter.BinaryWriter;

namespace FauFau.Formats
{
    public class Nsr : BinaryWrapper
    {
        private const int MaxGzipLayers = 4;

        public bool Compressed = false;

        // Some replays were packed more than once, 0 for an unpacked one
        public int GzipLayers;

        // Set when the gzip stream or the last packet is cut off, Packets then holds everything before the cut
        public bool Truncated;

        public DescriptionSection Description = new ();
        public IndexSection Index = new ();
        public MetaSection Meta = new ();

        public List<Packet> Packets = new ();


        public override void Read(BinaryStream bs)
        {
            bs.ByteOffset = 0;
            byte[] data = bs.Read.ByteArray((int)bs.Length);

            GzipLayers = 0;
            Truncated = false;
            while (data.Length >= 2 && data[0] == 0x1F && data[1] == 0x8B && GzipLayers < MaxGzipLayers)
            {
                data = Gunzip(data, out bool cutOff);
                Truncated |= cutOff;
                GzipLayers++;
            }
            Compressed = GzipLayers > 0;

            if (data.Length == 0)
            {
                return;
            }

            using (BinaryStream payload = new BinaryStream(new MemoryStream(data)))
            {
                ReadPayload(payload, data);
            }
        }

        private static byte[] Gunzip(byte[] data, out bool cutOff)
        {
            cutOff = false;
            // The trailer has the unpacked size, unless the file is cut off
            uint expectedSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(data.Length - 4));
            using MemoryStream inflated = new MemoryStream(expectedSize < 512 * 1024 * 1024 ? (int)expectedSize : 0);
            using (GZipStream gzip = new GZipStream(new MemoryStream(data), CompressionMode.Decompress))
            {
                // Small reads, since GZipStream throws at the cut and the data of that read is lost
                byte[] buffer = new byte[4096];
                try
                {
                    int read;
                    while ((read = gzip.Read(buffer)) > 0)
                    {
                        inflated.Write(buffer, 0, read);
                    }
                }
                catch (InvalidDataException)
                {
                    cutOff = true;
                }
            }

            // A cut off stream doesn't always throw, so check the size in the trailer as well
            cutOff |= expectedSize != (uint)inflated.Length;
            return inflated.ToArray();
        }

        private void ReadPayload(BinaryStream bs, byte[] data)
        {
            Description = bs.Read.Type<DescriptionSection>();
            Index = new IndexSection();

            if (Description.Version == 2)
            {
                // The meta section follows the shorter description and the packets follow the meta section
                Meta = bs.Read.Type<MetaSection>();
                Description.TimeStamp = Meta.TimeStamp;
            }
            else
            {
                if (Description._metaOffset < DescriptionSection.Length || Description._metaLength < 0 ||
                    Description._metaOffset + Description._metaLength > Description._dataOffset || Description._dataOffset > bs.Length)
                {
                    throw new InvalidDataException($"Unexpected section offsets: meta {Description._metaOffset}+{Description._metaLength}, data {Description._dataOffset}");
                }

                // Older builds have no index section and an index offset of 0
                if (Description._indexOffset != 0)
                {
                    bs.ByteOffset = Description._indexOffset;
                    Index = bs.Read.Type<IndexSection>();
                }

                bs.ByteOffset = Description._metaOffset;
                Meta = bs.Read.Type<MetaSection>();
                bs.ByteOffset = Description._dataOffset;

                // A few clients wrote garbage into the description time
                if (Description.TimeStamp == DateTime.MinValue)
                {
                    Description.TimeStamp = Meta.TimeStamp;
                }
            }

            ReadPackets(data, (int)bs.ByteOffset);
        }

        private void ReadPackets(byte[] data, int position)
        {
            // Roughly the average packet size of 1962 replays
            Packets = new List<Packet>((data.Length - position) / 24);
            while (data.Length - position >= Packet.HeaderLength)
            {
                ReadOnlySpan<byte> header = data.AsSpan(position, Packet.HeaderLength);
                Packet packet = new Packet
                {
                    TimeStamp = BinaryPrimitives.ReadUInt32LittleEndian(header),
                    Length = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(4)),
                    MessageId = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(6)),
                };
                position += Packet.HeaderLength;
                if (data.Length - position < packet.Length)
                {
                    Truncated = true;
                    return;
                }

                packet.Data = data.AsSpan(position, packet.Length).ToArray();
                position += packet.Length;
                Packets.Add(packet);
            }

            if (position != data.Length)
            {
                Truncated = true;
            }
        }

        public static string ReadNullTerminatedString(BinaryStream bs)
        {
            long start = bs.ByteOffset;
            int len = 0;
            bool terminated = false;
            while (bs.ByteOffset < bs.Length)
            {
                if (bs.Read.Byte() == 0)
                {
                    terminated = true;
                    break;
                }
                len++;
            }

            bs.ByteOffset = start;
            string ret = bs.Read.String(len);
            if (terminated)
            {
                bs.ByteOffset++;
            }
            return ret;
        }

        // Returns DateTime.MinValue for times that can't be right
        private static DateTime ReadTime(BinaryReader read)
        {
            long microseconds = read.Long();
            if (microseconds <= 0 || microseconds > Util.Time.UnixTimestampMicrosecondsFromDatetime(DateTime.MaxValue.AddYears(-1)))
            {
                return DateTime.MinValue;
            }
            return Util.Time.DateTimeFromUnixTimestampMicroseconds(microseconds);
        }

        public override void Write(BinaryStream bs)
        {
            using (BinaryStream payload = new BinaryStream())
            {
                WritePayload(payload);
                payload.ByteOffset = 0;

                if (Compressed)
                {
                    // compress with gzip
                    Util.Common.Gzip(payload, bs);
                }
                else
                {
                    // write as plain data
                    bs.Write.ByteArray(payload.Read.ByteArray((int)payload.Length));
                }
            }
        }
        private void WritePayload(BinaryStream bs)
        {
            BinaryWriter Write = bs.Write;

            // skip the header until we know the offsets
            bs.ByteOffset = DescriptionSection.Length;
            Description._indexOffset = (int)bs.ByteOffset;
            Write.Type(Index);

            Description._metaOffset = (int)bs.ByteOffset;
            Write.Type(Meta);
            Description._metaLength = (int)bs.ByteOffset - Description._metaOffset;

            Description._dataOffset = (int)bs.ByteOffset;
            bs.ByteOffset = 0;
            Write.Type(Description);

            bs.ByteOffset = Description._dataOffset;
            bs.Write.TypeList(Packets);
        }

        // NSRD | Network Stream Replay Description
        public class DescriptionSection : ReadWrite
        {
            public const int Length = 48;

            // Version 2 (2012 builds) has a 24 byte description without section offsets, version 5 is always written
            public int Version = 5;
            public int ProtocolVersion = 19551;
            public DateTime TimeStamp = DateTime.UtcNow;

            // Milliseconds between index entries, cvar replay.indexInterval
            public int IndexInterval = 5000;

            public int _metaOffset;
            public int _metaLength;
            public int _indexOffset;
            public int _dataOffset;

            public void Read(BinaryStream bs)
            {
                BinaryReader Read = bs.Read;
                if (!Read.String(4).Equals("NSRD"))
                {
                    throw new InvalidDataException("Not a replay file, the NSRD section is missing");
                }

                Version = Read.Int();
                if (Version == 2)
                {
                    ProtocolVersion = Read.Int();
                    Read.Int(); // unknown
                    Read.Long(); // unknown, not a unix time
                    return;
                }
                if (Version != 5)
                {
                    throw new NotSupportedException($"NSR version {Version} isn't supported, only versions 2 and 5");
                }

                _metaOffset = Read.Int();
                _metaLength = Read.Int();
                _indexOffset = Read.Int();
                _dataOffset = Read.Int();

                Read.Int(); // unknown, 0 in 1962

                ProtocolVersion = Read.Int();
                TimeStamp = ReadTime(Read);

                IndexInterval = Read.Int();
                Read.Int(); // unknown
            }

            public void Write(BinaryStream bs)
            {
                BinaryWriter Write = bs.Write;
                Write.String("NSRD");
                Write.Int(5);

                Write.Int(_metaOffset);
                Write.Int(_metaLength);
                Write.Int(_indexOffset);
                Write.Int(_dataOffset);

                Write.Int(0); // first unk

                Write.Int(ProtocolVersion);
                Write.Long(Util.Time.UnixTimestampMicrosecondsFromDatetime(TimeStamp));

                Write.Int(IndexInterval);
                Write.Int(0); // second unk

            }
        }

        // NSRI | Network Stream Replay Index
        public class IndexSection : ReadWrite
        {
            public int Version = 5;

            // Stream offsets of the keyframes, one every IndexInterval
            public List<uint> Offsets = new ();

            public void Read(BinaryStream bs)
            {
                BinaryReader Read = bs.Read;
                if (!Read.String(4).Equals("NSRI"))
                {
                    throw new InvalidDataException("Not a replay file, the NSRI section is missing");
                }

                Version = Read.Int();
                Read.Long(); // unknown
                int count = Read.Int();
                Read.UInt(); // Index Offset
                Offsets = Read.UIntList(count);
            }

            public void Write(BinaryStream bs)
            {
                BinaryWriter Write = bs.Write;
                Write.String("NSRI");
                Write.Int(Version);
                Write.Long(0); // unk
                Write.Int(Offsets.Count);
                Write.Int((int)bs.ByteOffset + 4); // Index Offset
                Write.UIntList(Offsets);
            }
        }

        // Metadata
        public class MetaSection : ReadWrite
        {
            public int Version;
            public int ZoneId;
            public string Description;
            public string LocalDateString;
            public Vector3 Position;
            public Vector4 Rotation;
            public ulong CharacterGUID;
            public string CharacterName;

            // Two unknown bytes, the zone instance GUID and in version 4 a clock value the playback restores
            public byte[] Unk2;
            public string FirefallVersionString;
            public DateTime TimeStamp;

            public int Month;
            public int Day;
            public int RealYear;
            public int FictionalYear;
            float FictionalTime;

            public string FictionalDateString;
            public byte[] Unk3;

            public ulong ZoneInstanceGuid => Unk2 != null && Unk2.Length >= 10 ? BinaryPrimitives.ReadUInt64LittleEndian(Unk2.AsSpan(2)) : 0;
            public ulong ClockSync => Unk2 != null && Unk2.Length >= 18 ? BinaryPrimitives.ReadUInt64LittleEndian(Unk2.AsSpan(10)) : 0;

            public MetaSection()
            {
                Version = 4;
                ZoneId = 12;
                Description = "(generated by faufau)";
                Position = new Vector3();
                Rotation = new Vector4 { x = 1f, y = 0f, z = 0f, w = 0f };
                CharacterGUID = 0;
                CharacterName = "TheMeldingWars";
                Unk2 = new byte[18];
                FirefallVersionString = "Firefall (v1.5.1962)";
                TimeStamp = DateTime.UtcNow;

                // The client writes it with C's ctime
                LocalDateString = TimeStamp.ToLocalTime().ToString("ddd MMM dd HH:mm:ss yyyy\n", CultureInfo.InvariantCulture);

                DateTime fictionalTime = Util.Time.FictionalTimeNow();

                Month = TimeStamp.Month;
                Day = TimeStamp.Day;
                RealYear = TimeStamp.Year;
                FictionalYear = fictionalTime.Year;
                FictionalTime = Util.Time.ClockAsFloat(fictionalTime);
                FictionalDateString = Util.Time.FictionalTimeString(fictionalTime);
                Unk3 = new byte[31];

            }

            public void Read(BinaryStream bs)
            {
                BinaryReader Read = bs.Read;

                Version = Read.Int();
                ZoneId = Read.Int();
                Description = ReadNullTerminatedString(bs);
                LocalDateString = ReadNullTerminatedString(bs);

                Position = Read.Type<Vector3>();
                Rotation = Read.Type<Vector4>();

                CharacterGUID = Read.ULong();
                CharacterName = ReadNullTerminatedString(bs);

                // Version 3 has no clock value
                Unk2 = Read.ByteArray(Version >= 4 ? 18 : 10);

                FirefallVersionString = ReadNullTerminatedString(bs);
                TimeStamp = ReadTime(Read);

                Month = Read.Int();
                Day = Read.Int();
                RealYear = Read.Int();
                FictionalYear = Read.Int();

                FictionalTime = Read.Float();

                long start = bs.ByteOffset;
                FictionalDateString = ReadNullTerminatedString(bs);

                Read.ByteArray((int)(128 - (bs.ByteOffset - start)));

                Unk3 = Read.ByteArray(31);

            }

            public void Write(BinaryStream bs)
            {
                BinaryWriter Write = bs.Write;
                Write.Int(Version);
                Write.Int(ZoneId);

                Write.String(Description);      Write.Byte(0);
                Write.String(LocalDateString);  Write.Byte(0);

                Write.Type(Position);
                Write.Type(Rotation);

                Write.ULong(CharacterGUID);
                Write.String(CharacterName); Write.Byte(0);

                Write.ByteArray(Unk2);

                Write.String(FirefallVersionString); Write.Byte(0);
                Write.Long(Util.Time.UnixTimestampMicrosecondsFromDatetime(TimeStamp));

                Write.Int(Month);
                Write.Int(Day);
                Write.Int(RealYear);
                Write.Int(FictionalYear);

                Write.Float(FictionalTime);
                Write.String(FictionalDateString);
                Write.ByteArray(new byte[128 - FictionalDateString.Length]);

                Write.ByteArray(Unk3);
            }
        }

        // Packet
        public class Packet : ReadWrite
        {
            public const int HeaderLength = 8;

            public uint TimeStamp;
            public ushort Length;
            public ushort MessageId;
            public byte[] Data;

            public void Read(BinaryStream bs)
            {
                ReadHeader(bs);
                Data = bs.Read.ByteArray(Length);
            }

            internal void ReadHeader(BinaryStream bs)
            {
                BinaryReader Read = bs.Read;
                TimeStamp = Read.UInt();
                Length = Read.UShort();
                MessageId = Read.UShort();
            }

            public void Write(BinaryStream bs)
            {
                BinaryWriter Write = bs.Write;
                Write.UInt(TimeStamp);
                Write.UShort((ushort)Data.Length);
                Write.UShort(MessageId);
                Write.ByteArray(Data);
            }
        }


        public static Nsr GenerateDummyFile(int zoneId)
        {
            Nsr n = new Nsr();
            n.Meta.ZoneId = zoneId;
            n.Meta.CharacterGUID = 5068907169408127230;
            n.Index.Offsets.Add(329);

            Packet packet1 = new Nsr.Packet();
            packet1.MessageId = 3;
            packet1.TimeStamp = 3795714048;

            packet1.Data = new byte[] { 0x0B, 0x90, 0x00, 0xE0, 0x36, 0x60, 0x58, 0x46, 0x03, 0x00, 0x00, 0x00, 0x00 };
            n.Packets.Add(packet1);

            Packet packet2 = new Nsr.Packet();
            packet2.Data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            n.Packets.Add(packet2);

            return n;
        }
    }
}
