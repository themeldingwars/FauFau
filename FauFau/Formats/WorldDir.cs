using Bitter;
using FauFau.Util.CommmonDataTypes;
using System;
using System.Collections.Generic;
using System.IO;

namespace FauFau.Formats
{
    // The index of the world map tiles of a zone (.worldDir)
    public class WorldDir : BinaryWrapper
    {
        private const int HeaderLayerId = 0;
        private const int TileLayerId = 100;

        public string Magic = "WMAP";
        public uint Version = 3;
        public bool Embedded;
        public string Path;
        public uint DirectorySize;

        public List<Tile> Tiles = new ();

        public override void Read(BinaryStream bs)
        {
            Bitter.BinaryReader Read = bs.Read;

            (uint id, uint length) = (Read.UInt(), Read.UInt());
            if (id != HeaderLayerId)
            {
                throw new InvalidDataException($"Expected the header layer, got layer {id}");
            }
            long headerEnd = bs.ByteOffset + length;

            Magic = Read.String(4);
            if (Magic != "WMAP")
            {
                throw new InvalidDataException($"Not a world map directory, the magic is {Magic}");
            }
            Version = Read.UInt();
            if (Version != 3)
            {
                throw new NotSupportedException($"World map directory version {Version} isn't supported, only version 3");
            }
            uint tileCount = Read.UInt();
            Embedded = Read.Byte() != 0;
            Path = ReadString(bs);
            DirectorySize = Read.UInt();
            bs.ByteOffset = headerEnd;

            Tiles = new List<Tile>((int)tileCount);
            for (int i = 0; i < tileCount; i++)
            {
                (id, length) = (Read.UInt(), Read.UInt());
                if (id != TileLayerId)
                {
                    throw new InvalidDataException($"Expected a tile layer, got layer {id}");
                }
                long tileEnd = bs.ByteOffset + length;

                Tile tile = new Tile
                {
                    Name = ReadString(bs),
                    DataOffset = Read.UInt(),
                    DataLength = Read.UInt(),
                    CubeFace = Read.UInt(),
                    ZoomLevel = Read.UInt(),
                    EntryId = Read.UInt(),
                    BoundsMin = Read.Type<Vector3>(),
                    BoundsMax = Read.Type<Vector3>(),
                    OriginX = Read.Float(),
                    OriginY = Read.Float(),
                    Size = Read.Float(),
                };
                Tiles.Add(tile);
                bs.ByteOffset = tileEnd;
            }
        }

        // The length includes the terminating NUL
        private static string ReadString(BinaryStream bs)
        {
            int length = bs.Read.Int();
            string text = length > 1 ? bs.Read.String(length - 1) : "";
            if (length > 0)
            {
                bs.Read.Byte();
            }
            return text;
        }

        public class Tile
        {
            // Like "0_07_0000001074_opt.worldMap" in maps/worldmapchunks
            public string Name;
            public uint DataOffset;
            public uint DataLength;
            public uint CubeFace;
            public uint ZoomLevel;
            public uint EntryId;
            public Vector3 BoundsMin;
            public Vector3 BoundsMax;
            public float OriginX;
            public float OriginY;
            public float Size;
        }
    }
}
