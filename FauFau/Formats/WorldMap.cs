using Bitter;
using FauFau.Util.CommmonDataTypes;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace FauFau.Formats
{
    // A world map tile (.worldMap), its payload is a list of layers like textures and geometry
    public class WorldMap : BinaryWrapper
    {
        private const int GeneralLayerId = 0;
        private const int CompressedLayerId = 100;

        public const uint DiffuseTextureLayerId = 10;
        public const uint NormalTextureLayerId = 11;
        public const uint VerticesLayerId = 21;
        public const uint IndicesLayerId = 31;
        public const uint PropsLayerId = 50;
        public const uint RoadsLayerId = 61;

        public string Magic = "GTNO";
        public uint Version = 2;

        public uint EntryId;
        public uint ZoomLevel;
        public float OriginX;
        public float OriginY;
        public float Size;
        public Vector3 BoundsMin;
        public Vector3 BoundsMax;
        public bool Compressed;

        // The uncompressed payload and the layers it consists of
        public byte[] Data;
        public List<Layer> Layers = new ();

        public override void Read(BinaryStream bs)
        {
            Bitter.BinaryReader Read = bs.Read;

            Magic = Read.String(4);
            if (Magic != "GTNO")
            {
                throw new InvalidDataException($"Not a world map, the magic is {Magic}");
            }
            Version = Read.UInt();
            if (Version != 2)
            {
                throw new NotSupportedException($"World map version {Version} isn't supported, only version 2");
            }

            Layer general = ReadLayerHeader(bs);
            if (general.Id != GeneralLayerId)
            {
                throw new InvalidDataException($"Expected the general layer, got layer {general.Id}");
            }
            long generalEnd = bs.ByteOffset + general.Length;
            EntryId = Read.UInt();
            ZoomLevel = Read.UInt();
            OriginX = Read.Float();
            OriginY = Read.Float();
            Size = Read.Float();
            BoundsMin = Read.Type<Vector3>();
            BoundsMax = Read.Type<Vector3>();
            Compressed = Read.Byte() != 0;
            bs.ByteOffset = generalEnd;

            if (Compressed)
            {
                Layer compressed = ReadLayerHeader(bs);
                if (compressed.Id != CompressedLayerId)
                {
                    throw new InvalidDataException($"Expected the compressed layer, got layer {compressed.Id}");
                }
                uint uncompressedSize = Read.UInt();
                uint compressedSize = Read.UInt();
                byte[] deflated = Read.ByteArray((int)compressedSize);

                Data = new byte[uncompressedSize];
                using ZLibStream zlib = new ZLibStream(new MemoryStream(deflated), CompressionMode.Decompress);
                zlib.ReadAtLeast(Data, Data.Length, false);
            }
            else
            {
                Data = Read.ByteArray((int)(bs.Length - bs.ByteOffset));
            }

            Layers = new List<Layer>();
            using BinaryStream payload = new BinaryStream(new MemoryStream(Data));
            while (payload.Length - payload.ByteOffset >= 8)
            {
                Layer layer = ReadLayerHeader(payload);
                if (payload.Length - payload.ByteOffset < layer.Length)
                {
                    throw new InvalidDataException($"Layer {layer.Id} is cut off");
                }
                layer.Data = payload.Read.ByteArray((int)layer.Length);
                Layers.Add(layer);
            }
        }

        private static Layer ReadLayerHeader(BinaryStream bs)
        {
            return new Layer { Id = bs.Read.UInt(), Length = bs.Read.UInt() };
        }

        // The entry id is the quadtree path from the cube face down to the tile, two bits per level with the top level in the
        // lowest bits. Bit 0 of each step is the X half, bit 1 the Y half, so a face has 2^zoom tiles per side.
        public static (uint X, uint Y) GetTilePosition(uint entryId, uint zoomLevel)
        {
            uint x = 0;
            uint y = 0;
            for (int level = 0; level < zoomLevel; level++)
            {
                uint child = (entryId >> (2 * level)) & 3;
                x = (x << 1) | (child & 1);
                y = (y << 1) | (child >> 1);
            }
            return (x, y);
        }

        public (uint X, uint Y) TilePosition => GetTilePosition(EntryId, ZoomLevel);

        // DXT5, the top-down view of the tile that the client puts on the tile mesh
        public Texture GetDiffuseTexture() => ReadTexture(DiffuseTextureLayerId);

        // DXT5 with X in alpha and Y in green
        public Texture GetNormalTexture() => ReadTexture(NormalTextureLayerId);

        private Texture ReadTexture(uint layerId)
        {
            Layer layer = Layers.Find(l => l.Id == layerId);
            if (layer == null)
            {
                return null;
            }

            int length = BinaryPrimitives.ReadInt32LittleEndian(layer.Data);
            if (length > layer.Data.Length - 4)
            {
                throw new InvalidDataException($"The texture of layer {layerId} is cut off");
            }

            // Square and DXT5, so one byte per pixel
            int size = (int)Math.Sqrt(length);
            if (size * size != length)
            {
                throw new InvalidDataException($"The texture of layer {layerId} isn't square, it has {length} bytes");
            }

            return new Texture { Width = size, Height = size, Data = layer.Data.AsSpan(4, length).ToArray() };
        }

        public RoadMesh GetRoads()
        {
            Layer layer = Layers.Find(l => l.Id == RoadsLayerId);
            if (layer == null)
            {
                return null;
            }

            using BinaryStream bs = new BinaryStream(new MemoryStream(layer.Data));
            RoadMesh roads = new RoadMesh();
            int vertexCount = bs.Read.Int();
            roads.Positions = new System.Numerics.Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                roads.Positions[i] = new System.Numerics.Vector3(bs.Read.Float(), bs.Read.Float(), bs.Read.Float());
            }
            roads.Alpha = bs.Read.ByteArray(vertexCount);

            int indexCount = bs.Read.Int();
            roads.Indices = new ushort[indexCount];
            for (int i = 0; i < indexCount; i++)
            {
                roads.Indices[i] = bs.Read.UShort();
            }
            return roads;
        }

        // Ids 10 and 11 are the diffuse and normal textures, 21 vertices, 31 and 41 indices
        public class Layer
        {
            public uint Id;
            public uint Length;
            public byte[] Data;
        }

        public class Texture
        {
            public int Width;
            public int Height;
            public byte[] Data;
        }

        // Triangles along the roads, 7 or 15 m wide in build 1962. The alpha runs across a road, 255 on one side and 0 on the
        // other, 128 at junctions. The client draws the roads white.
        public class RoadMesh
        {
            // Relative to the tile center like the tile mesh, Z is the height
            public System.Numerics.Vector3[] Positions = Array.Empty<System.Numerics.Vector3>();
            public byte[] Alpha = Array.Empty<byte>();
            public ushort[] Indices = Array.Empty<ushort>();
        }
    }
}
