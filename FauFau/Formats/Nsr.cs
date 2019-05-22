using Bitter;
using FauFau.Util.CommmonDataTypes;
using System;
using System.Collections.Generic;
using System.Text;

namespace FauFau.Formats
{
    public class Nsr : BinaryWrapper
    {
        public bool Compressed = false;

        public DescriptionSection Description = new DescriptionSection();
        public IndexSection Index = new IndexSection();
        public MetaSection Meta = new MetaSection();

        public List<Packet> Packets = new List<Packet>();
        

        public override void Read(BinaryStream bs)
        {
            using (BinaryStream payload = new BinaryStream())
            {
                // check if compressed

                uint magic = bs.Read.UInt();
                bs.ByteOffset = 0;

                if (magic == 559903)
                {                
                    try
                    {
                        Util.Util.UnGzipUnknownTargetSize(bs, payload);
                        Compressed = true;
                    }
                    catch
                    {
                        payload.ByteOffset = 0;
                        payload.Write.ByteArray(bs.Read.ByteArray((int)bs.Length));
                    }
                }
                else
                {
                    payload.Write.ByteArray(bs.Read.ByteArray((int)bs.Length));
                    Console.WriteLine(payload.Length);
                }

                bs.Dispose();

                payload.ByteOffset = 0;

                if(payload.Length == 0)
                {
                    Compressed = false;
                    return;
                }

                ReadPayload(payload);                
            } 
        }
        private void ReadPayload(BinaryStream bs)
        {
            Description = bs.Read.Type<DescriptionSection>();
            Index = bs.Read.Type<IndexSection>();
            Meta = bs.Read.Type<MetaSection>();
            Packets = new List<Packet>();
            while(!bs.EndOfStream)
            {
                Packets.Add(bs.Read.Type<Packet>());
            }
        }

        public static string ReadNullTerminatedString(BinaryStream bs)
        {
            int len = 0;
            while (bs.ByteOffset != bs.Length && bs.Read.Byte() != 0)
            {
                len++;
            }
            bs.ByteOffset -= len + 1;
            string ret = bs.Read.String(len);
            bs.ByteOffset++;
            return ret;
        }

        public override void Write(BinaryStream bs)
        {
            using (BinaryStream payload = new BinaryStream())
            {
                WritePayload(payload);
                payload.ByteOffset = 0;

                Console.WriteLine(payload.Length + "!");

                if (Compressed)
                {
                    // compress with gzip
                    Util.Util.Gzip(payload, bs);
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
            bs.ByteOffset = 48;
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
            public int Version = 5;
            public int ProtocolVersion = 19551;
            public DateTime TimeStamp = DateTime.UtcNow;

            public int _metaOffset;
            public int _metaLength;
            public int _indexOffset;
            public int _dataOffset;

            public void Read(BinaryStream bs)
            {
                BinaryReader Read = bs.Read;
                if (!Read.String(4).Equals("NSRD")) { Console.WriteLine("This is not a valid replay file >,>"); return; }

                Version = Read.Int();

                _metaOffset = Read.Int();
                _metaLength = Read.Int();
                _indexOffset = Read.Int();
                _dataOffset = Read.Int();

                if (Read.Int() != 0) { Console.WriteLine("First NSRD unk is not null!"); return; }

                ProtocolVersion = Read.Int();
                TimeStamp = Util.Time.DateTimeFromUnixTimestampMicroseconds(Read.Long());

                if (Read.Int() != 5000) { Console.WriteLine("NSRD 5000 is not 5000!!"); return; }
                if (Read.Int() != 0) { Console.WriteLine("Second NSRD unk is not null!"); return; }
            }

            public void Write(BinaryStream bs)
            {
                BinaryWriter Write = bs.Write;
                Write.String("NSRD");
                Write.Int(Version);

                Write.Int(_metaOffset);
                Write.Int(_metaLength);
                Write.Int(_indexOffset);
                Write.Int(_dataOffset);

                Write.Int(0); // first unk

                Write.Int(ProtocolVersion);
                Write.Long(Util.Time.UnixTimestampMicrosecondsFromDatetime(TimeStamp));

                Write.Int(5000);
                Write.Int(0); // second unk

            }
        }

        // NSRI | Network Stream Replay Index
        public class IndexSection : ReadWrite
        {
            public int Version = 5;
            public List<uint> Offsets = new List<uint>();

            public void Read(BinaryStream bs)
            {
                BinaryReader Read = bs.Read;
                //bs.ByteOffset = indexOffset;
                if (!Read.String(4).Equals("NSRI")) { Console.WriteLine("This is not a valid replay file >,>"); return; }

                Version = Read.Int();
                if (Read.Long() != 0) { Console.WriteLine("NSRI unknown is not null"); return; }
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

            public MetaSection()
            {
                Version = 4;
                ZoneId = 12;
                Description = "(generated by faufau)";
                LocalDateString = "";
                Position = new Vector3();
                Rotation = new Vector4 { x = 1f, y = 0f, z = 0f, w = 0f };
                CharacterGUID = 0;
                CharacterName = "TheMeldingWars";
                Unk2 = new byte[18];
                FirefallVersionString = "Firefall (v1.5.1962)";
                TimeStamp = DateTime.UtcNow;

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
                //bs.ByteOffset = metaOffset;

                Version = Read.Int();
                ZoneId = Read.Int();
                Description = ReadNullTerminatedString(bs);
                LocalDateString = ReadNullTerminatedString(bs);

                Position = Read.Type<Vector3>();
                Rotation = Read.Type<Vector4>();

                CharacterGUID = Read.ULong();
                CharacterName = ReadNullTerminatedString(bs);

                Unk2 = Read.ByteArray(18);

                FirefallVersionString = ReadNullTerminatedString(bs);
                TimeStamp = Util.Time.DateTimeFromUnixTimestampMicroseconds(Read.Long());

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
            public uint TimeStamp;
            public ushort Length;
            public ushort MessageId;
            public byte[] Data;

            public void Read(BinaryStream bs)
            {
                BinaryReader Read = bs.Read;
                TimeStamp = Read.UInt();
                Length = Read.UShort();
                MessageId = Read.UShort();
                Data = Read.ByteArray(Length);
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
