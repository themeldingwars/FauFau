using Bitter;
using FauFau.Util;
using System;
using System.Collections.Generic;
using System.IO;

namespace FauFau.Formats
{
    // The index of the virtual texture (vt/static.vtex_idx), the tiles of each mip level are in vt/static.vtex0 to 6
    public class VTexIndex : BinaryWrapper
    {
        public const int LevelCount = 7;

        public uint Version = 1;
        public List<Image> Images = new ();

        // Per level, 1024 x 1024 tiles at level 0 and a quarter of that per level up
        public TileInfo[][] Tiles = new TileInfo[LevelCount][];

        public override void Read(BinaryStream bs)
        {
            Bitter.BinaryReader Read = bs.Read;

            Version = Read.UInt();
            if (Version != 1)
            {
                throw new NotSupportedException($"Virtual texture index version {Version} isn't supported, only version 1");
            }

            uint imageCount = Read.UInt();
            Images = new List<Image>((int)imageCount);
            for (int i = 0; i < imageCount; i++)
            {
                Images.Add(new Image
                {
                    Hashes = Read.UIntArray(3),
                    Transform = Read.FloatArray(9),
                    Bounds = Read.UShortArray(4),
                    Offsets = Read.UIntArray(4),
                    Crc = Read.UInt(),
                });
            }

            for (int level = 0; level < LevelCount; level++)
            {
                int compressedSize = Read.Int();
                byte[] table = FastLz.Decompress(Read.ByteArray(compressedSize));
                if (table.Length % 16 != 0)
                {
                    throw new InvalidDataException($"The tile table of level {level} has {table.Length} bytes, not a multiple of 16");
                }

                BinaryStream tableStream = new BinaryStream(new MemoryStream(table));
                TileInfo[] tiles = new TileInfo[table.Length / 16];
                for (int i = 0; i < tiles.Length; i++)
                {
                    tiles[i] = new TileInfo
                    {
                        Offset = tableStream.Read.ULong(),
                        Crc = tableStream.Read.UInt(),
                        Size = tableStream.Read.UInt(),
                    };
                }
                Tiles[level] = tiles;
            }
        }

        // Reads a tile from the open static.vtex file of its level, null when the tile doesn't exist
        public static VTexTile ReadTile(Stream vtex, TileInfo tile)
        {
            if (!tile.Exists)
            {
                return null;
            }

            byte[] data = new byte[tile.Size];
            vtex.Position = (long)tile.Offset;
            vtex.ReadExactly(data);
            return VTexTile.Decode(data);
        }

        public class Image
        {
            // Hashes of the texture paths, see HashAssetPath
            public uint[] Hashes;
            public float[] Transform;
            public ushort[] Bounds;
            public uint[] Offsets;
            public uint Crc;
        }

        public struct TileInfo
        {
            public ulong Offset;

            // The CRC-32s of the three decoded layers XORed together
            public uint Crc;
            public uint Size;

            public bool Exists => Offset != ulong.MaxValue;
        }

        // Rotates by 7 bits and adds each character of the asset path, e.g. "00113000\00113473"
        public static uint HashAssetPath(uint assetId)
        {
            string id = assetId.ToString("D8");
            string path = id.Substring(0, id.Length - 3) + "000\\" + id;
            uint hash = 0;
            foreach (char c in path)
            {
                hash = (hash << 7) | (hash >> 25);
                hash += c;
            }
            return hash;
        }
    }

    // A tile of the virtual texture, three layers of 128 x 128 DXT5 blocks
    public class VTexTile
    {
        public const int LayerCount = 3;
        public const int LayerSize = 0x4000;
        private const int BlockCount = LayerSize / 16;
        private const int HeaderSize = 9;
        private const int SplitHeaderSize = 12;

        public byte[][] Layers = new byte[LayerCount][];

        public uint Crc => Checksum.Crc32(Layers[0]) ^ Checksum.Crc32(Layers[1]) ^ Checksum.Crc32(Layers[2]);

        public enum Mode : byte
        {
            // Bit coded DXT end points, no tile of build 1962 uses it
            Entropy = 0,
            Lzma = 1,
            Raw = 13,
            // Decodes the same way as Lzma
            LzmaAlternate = 15,

            // The fields of the DXT5 blocks in planes, compressed in three LZMA streams
            LzmaPlanes = 16,
        }

        // 5 LZMA property bytes, a 24 bit decoded size and the mode, then the data
        public static VTexTile Decode(byte[] data)
        {
            if (data.Length < HeaderSize)
            {
                throw new InvalidDataException("The tile is cut off");
            }

            int size = data[5] | (data[6] << 8) | (data[7] << 16);
            Mode mode = (Mode)data[8];
            if (size != LayerCount * LayerSize)
            {
                throw new InvalidDataException($"The tile decodes to {size} bytes, expected {LayerCount * LayerSize}");
            }

            byte[] properties = data.AsSpan(0, 5).ToArray();
            byte[] decoded = mode switch
            {
                Mode.Raw => data.AsSpan(HeaderSize, size).ToArray(),
                Mode.Lzma or Mode.LzmaAlternate => Lzma.Decompress(properties, data, HeaderSize, data.Length - HeaderSize, size),
                Mode.LzmaPlanes => DecodePlanes(data),
                _ => throw new NotSupportedException($"Tile mode {mode} isn't supported"),
            };

            VTexTile tile = new VTexTile();
            for (int i = 0; i < LayerCount; i++)
            {
                tile.Layers[i] = decoded.AsSpan(i * LayerSize, LayerSize).ToArray();
            }
            return tile;
        }

        private static byte[] DecodePlanes(byte[] data)
        {
            // Color indices, alpha indices, then colors and alpha end points, each plane holds all three layers
            int[] streamSizes = { 0x3000, 0x4800, 0x4800 };
            byte[] planes = new byte[0xC000];
            int position = SplitHeaderSize;
            int planeOffset = 0;
            for (int i = 0; i < streamSizes.Length; i++)
            {
                // Each stream has its own first property byte, the dictionary size is shared
                byte[] properties = data.AsSpan(0, 5).ToArray();
                properties[0] = data[HeaderSize + i];
                position += Lzma.DecompressInto(properties, data, position, data.Length - position, planes.AsSpan(planeOffset, streamSizes[i]));
                planeOffset += streamSizes[i];
            }

            byte[] decoded = new byte[LayerCount * LayerSize];
            for (int layer = 0; layer < LayerCount; layer++)
            {
                Span<byte> output = decoded.AsSpan(layer * LayerSize, LayerSize);
                for (int block = 0; block < BlockCount; block++)
                {
                    Span<byte> target = output.Slice(block * 16, 16);
                    planes.AsSpan(0xA800 + layer * 0x800 + block * 2, 2).CopyTo(target);
                    planes.AsSpan(0x3000 + layer * 0x1800 + block * 6, 6).CopyTo(target.Slice(2));
                    planes.AsSpan(0x7800 + layer * 0x1000 + block * 4, 4).CopyTo(target.Slice(8));
                    planes.AsSpan(layer * 0x1000 + block * 4, 4).CopyTo(target.Slice(12));
                }
            }
            return decoded;
        }
    }
}
