using Bitter;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FauFau.Formats
{
    public class Zone : BinaryWrapper
    {
        public const uint RootLayerId = 0x30000;
        public const uint ChunkInfoLayerId = 0x20400;
        public const uint ChunkRangeLayerId = 0x10000;
        public const uint ChunkRefLayerId = 0x10101;
        public const uint ChunkRef2LayerId = 0x10100;

        public string Magic = "ZONE";
        public int Version = 8;
        public DateTime TimeStamp = DateTime.UtcNow;
        public string Name;

        // Bounds, skybox, environment, melding, water, chunk info, paths, props and more as layers
        public GtContainerLayer Root;

        public override void Read(BinaryStream bs)
        {
            Bitter.BinaryReader Read = bs.Read;

            Magic = Read.String(4);
            Version = Read.Int();
            TimeStamp = Util.Time.DateTimeFromUnixTimestampMilliseconds(Read.Long());

            // The length includes the terminating NUL
            int nameLength = Read.Int();
            Name = nameLength > 1 ? Read.String(nameLength - 1) : "";
            if (nameLength > 0)
            {
                Read.Byte();
            }

            if (bs.ByteOffset >= bs.Length)
            {
                Root = null;
                return;
            }

            Root = GtLayer.Read(bs) as GtContainerLayer;
            if (Root == null || Root.Id != RootLayerId)
            {
                throw new InvalidDataException($"Expected the zone root layer 0x{RootLayerId:X}");
            }
        }

        public override void Write(BinaryStream bs)
        {
            Bitter.BinaryWriter Write = bs.Write;

            Write.ByteArray(Encoding.ASCII.GetBytes(Magic));
            Write.Int(Version);
            Write.Long(new DateTimeOffset(TimeStamp).ToUnixTimeMilliseconds());

            byte[] name = Encoding.ASCII.GetBytes((Name ?? "") + "\0");
            Write.Int(name.Length);
            Write.ByteArray(name);

            Root?.Write(bs);
        }

        // The chunk coordinates the zone covers, usually one range on one cube face
        public List<ZoneChunkRangeLayer> GetChunkRanges()
        {
            GtContainerLayer chunkInfo = Root?.Find(ChunkInfoLayerId) as GtContainerLayer;
            return chunkInfo == null ? new List<ZoneChunkRangeLayer>() : new List<ZoneChunkRangeLayer>(chunkInfo.FindAll<ZoneChunkRangeLayer>());
        }

        // The terrain chunks of the zone, the files are in maps/chunks
        public List<ChunkRef> GetChunks()
        {
            List<ChunkRef> chunks = new ();
            if (Root?.Find(ChunkInfoLayerId) is not GtContainerLayer chunkInfo)
            {
                return chunks;
            }

            List<ZoneChunkRangeLayer> ranges = GetChunkRanges();
            foreach (ZoneChunkRefLayer reference in chunkInfo.FindAll<ZoneChunkRefLayer>())
            {
                ChunkRef chunk = new ChunkRef { X = reference.X, Y = reference.Y, ChunkRecordId = reference.ChunkRecordId };

                // The references don't store the cube face, it's the one of the range they're in
                foreach (ZoneChunkRangeLayer range in ranges)
                {
                    if (range.Contains(chunk.X, chunk.Y))
                    {
                        chunk.CubeFace = range.CubeFace;
                        break;
                    }
                }
                chunks.Add(chunk);
            }
            return chunks;
        }

        public struct ChunkRef
        {
            public uint CubeFace;
            public uint X;
            public uint Y;
            // 0 for chunks from a ChunkRef2 layer
            public uint ChunkRecordId;

            public string FileName => $"{CubeFace}_{X:D4}_{Y:D4}.gtchunk";
        }
    }
}
