using Bitter;
using FauFau.Util.CommmonDataTypes;
using System;
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

        // Ids 10 and 11 are the diffuse and normal textures, 21 vertices, 31 and 41 indices
        public class Layer
        {
            public uint Id;
            public uint Length;
            public byte[] Data;
        }
    }
}
