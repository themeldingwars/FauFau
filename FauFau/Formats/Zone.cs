using Bitter;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace FauFau.Formats
{
    public class Zone : BinaryWrapper
    {
        public const uint RootLayerId = 0x30000;
        public const uint ChunkInfoLayerId = 0x20400;
        public const uint ChunkRangeLayerId = 0x10000;
        public const uint ChunkRefLayerId = 0x10101;

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

        // The terrain chunks of the zone, the files are in maps/chunks
        public List<ChunkRef> GetChunks()
        {
            List<ChunkRef> chunks = new ();
            GtLayer chunkInfo = Root?.Find(ChunkInfoLayerId);
            if (chunkInfo == null)
            {
                return chunks;
            }

            List<uint[]> ranges = new ();
            foreach (GtLayer range in chunkInfo.FindAll(ChunkRangeLayerId))
            {
                // Cube face, min x, max x, min y, max y
                uint[] values = new uint[5];
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = BinaryPrimitives.ReadUInt32LittleEndian(range.Data.AsSpan(i * 4));
                }
                ranges.Add(values);
            }

            foreach (GtLayer reference in chunkInfo.FindAll(ChunkRefLayerId))
            {
                ChunkRef chunk = new ChunkRef
                {
                    X = BinaryPrimitives.ReadUInt32LittleEndian(reference.Data),
                    Y = BinaryPrimitives.ReadUInt32LittleEndian(reference.Data.AsSpan(4)),
                    ChunkRecordId = BinaryPrimitives.ReadUInt32LittleEndian(reference.Data.AsSpan(8)),
                };

                // The references don't store the cube face, it's the one of the range they're in
                foreach (uint[] range in ranges)
                {
                    if (chunk.X >= range[1] && chunk.X <= range[2] && chunk.Y >= range[3] && chunk.Y <= range[4])
                    {
                        chunk.CubeFace = range[0];
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
            public uint ChunkRecordId;

            public string FileName => $"{CubeFace}_{X:D4}_{Y:D4}.gtchunk";
        }
    }
}
