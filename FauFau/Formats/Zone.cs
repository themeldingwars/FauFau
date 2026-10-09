using Bitter;
using System;
using System.Collections.Generic;

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
        public GtLayer Root;

        public override void Read(BinaryStream bs)
        {
            BinaryReader Read = bs.Read;

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

            Root = bs.ByteOffset < bs.Length ? GtLayer.Read(bs) : null;
        }

        // The chunk coordinates the zone covers, usually one range on one cube face
        public List<ZoneChunkRange> GetChunkRanges()
        {
            List<ZoneChunkRange> ranges = new ();
            GtLayer chunkInfo = Root?.Find(ChunkInfoLayerId);
            if (chunkInfo == null)
            {
                return ranges;
            }

            foreach (GtLayer range in chunkInfo.FindAll(ChunkRangeLayerId))
            {
                ranges.Add(ZoneChunkRange.Read(range.Data));
            }
            return ranges;
        }

        // The terrain chunks of the zone, the files are in maps/chunks
        public List<ChunkRef> GetChunks()
        {
            List<ChunkRef> chunks = new ();
            GtLayer chunkInfo = Root?.Find(ChunkInfoLayerId);
            if (chunkInfo == null)
            {
                return chunks;
            }

            List<ZoneChunkRange> ranges = GetChunkRanges();
            foreach (GtLayer reference in chunkInfo.Children)
            {
                if (reference.Id != ChunkRefLayerId && reference.Id != ChunkRef2LayerId)
                {
                    continue;
                }

                ZoneChunkRef read = ZoneChunkRef.Read(reference.Data);
                ChunkRef chunk = new ChunkRef { X = read.X, Y = read.Y, ChunkRecordId = read.ChunkRecordId };

                // The references don't store the cube face, it's the one of the range they're in
                foreach (ZoneChunkRange range in ranges)
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
