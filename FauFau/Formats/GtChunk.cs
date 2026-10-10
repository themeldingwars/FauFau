using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using Bitter;
using FauFau.Util;

namespace FauFau.Formats
{
    // Terrain Chunks
    // Codes not the most optimised atm so TODO: revisit
    public class GtChunkV8 : BinaryWrapper
    {
        public const int VERSION = 8;

        public RootLayer        Root;
        public Block[]          DataBlocks;
        public Block[]          DatBlocks;
        public LodDataMapping[] LodDataMap;

        public void Load(string filePath)
        {
            var fs = File.OpenRead(filePath);
            if (fs == null) return;
            using var bs = new BinaryStream(fs, BinaryStream.Endianness.LittleEndian);
            Read(bs);
        }

        // Check if the layer marker and version match
        public bool CheckIsValid(BinaryStream bs)
        {
            var header = new LayerHeader();
            header.Read(bs);
            var version = bs.Read.UInt();

            var isValid = header.Marker == GtLayer.Marker && version == VERSION;
            return isValid;
        }

        public override void Read(BinaryStream bs)
        {
            Root = new RootLayer();
            Root.Read(bs);

            LoadCompressedBlocks(bs);
        }

        public LodSubChunkData GetDecompressedLod(int lodLevel)
        {
            var lod     = Root.LodLayers[lodLevel];
            var lodData = LodDataMap[lodLevel];

            var lodDecompressed = new LodSubChunkData()
            {
                LodData      = DataBlocks[lodLevel].Decompress(),
                SubChunkData = new byte[lod.NumSubchunks][]
            };

            int idx = 0;
            foreach (var subChunkId in lodData.DatBlockIds) {
                lodDecompressed.SubChunkData[idx++] = GetDecompressedSubChunk(subChunkId).ToArray();
            }

            return lodDecompressed;
        }

        public Span<byte> GetDecompressedSubChunk(int SubChunkIdx)
        {
            var blockDat         = DatBlocks[SubChunkIdx];
            var decompressedData = blockDat.Decompress();

            return decompressedData;
        }

        // The layers of the data a LOD shares between its sub chunks
        public List<GtLayer> GetLodLayers(int lodLevel)
        {
            return GtLayer.ReadList(DataBlocks[lodLevel].Decompress(), WorldLayerIds.Lod);
        }

        public List<GtLayer> GetSubChunkLayers(int subChunkIdx)
        {
            return GtLayer.ReadList(DatBlocks[subChunkIdx].Decompress(), WorldLayerIds.SubChunk);
        }

        // load the compressed chunks into memory, doesn't decompress them
        private void LoadCompressedBlocks(BinaryStream bs)
        {
            // The block offsets are relative to the end of the root layer
            long dataStart = LayerHeader.HeaderLength + Root.Length;

            List<Block> datBlocks           = new List<Block>(100);
            List<short> datBlockLodMappings = new List<short>(100);
            DataBlocks = new Block[Root.NumLods];
            LodDataMap = new LodDataMapping[Root.NumLods];
            for (int i = 0; i < DataBlocks.Length; i++) {
                var lod = Root.LodLayers[i];
                LodDataMap[i].DataBlockIdx = i;

                bs.ByteOffset = dataStart + lod.DataOffset;
                DataBlocks[i] = new Block()
                {
                    CompressedSize   = lod.CompressedSize,
                    UncompressedSize = lod.UncompressedSize
                };
                DataBlocks[i].Read(bs);

                datBlockLodMappings.Clear();
                for (int j = 0; j < lod.NumSubchunks; j++) {
                    var subChunk = lod.SubChunkLayers[j];
                    datBlockLodMappings.Add((short) datBlocks.Count);

                    bs.ByteOffset = dataStart + subChunk.DataOffset;
                    var datBlock = new Block()
                    {
                        CompressedSize   = subChunk.CompressedSize,
                        UncompressedSize = subChunk.UncompressedSize
                    };
                    datBlock.Read(bs);
                    datBlocks.Add(datBlock);
                }
                LodDataMap[i].DatBlockIds = datBlockLodMappings.ToArray();
            }

            DatBlocks = datBlocks.ToArray();
        }

    #region Types

        // Mapp an lod idx to compressed blocks
        public struct LodDataMapping
        {
            public int     DataBlockIdx;
            public short[] DatBlockIds;
        }

        // the uncompressed byte array for an lod and its sub chunks
        public class LodSubChunkData
        {
            public byte[]   LodData;
            public byte[][] SubChunkData;
        }

        // The header of the root, LOD and sub chunk layers, the chunk files always write the marker
        public class LayerHeader : ReadWrite
        {
            public const int HeaderLength = 16;

            public ulong Marker;
            public uint  Id;
            public int   Length;

            public virtual void Read(BinaryStream bs)
            {
                Marker = bs.Read.ULong();
                Id     = bs.Read.UInt();
                Length = bs.Read.Int();
            }

            public virtual void Write(BinaryStream bs)
            {
                bs.Write.ULong(GtLayer.Marker);
                bs.Write.UInt(Id);
                bs.Write.Int(Length);
            }
        }

        public class RootLayer : LayerHeader
        {
            public uint  Version;
            public ulong Timestamp;
            public uint  NumLods;

            public LodLayer[] LodLayers;

            public DateTime TimeStamp => Util.Time.DateTimeFromUnixTimestampMilliseconds((long)Timestamp);

            public override void Read(BinaryStream bs)
            {
                base.Read(bs);
                Version   = bs.Read.UInt();
                Timestamp = bs.Read.ULong();
                NumLods   = bs.Read.UInt();

                LodLayers = new LodLayer[NumLods];
                for (int i = 0; i < NumLods; i++) {
                    var lod = new LodLayer();
                    lod.Read(bs);
                    LodLayers[i] = lod;
                }
            }

            public override void Write(BinaryStream bs)
            {
                base.Write(bs);
                bs.Write.UInt(Version);
                bs.Write.ULong(Timestamp);
                bs.Write.UInt(NumLods);

                for (int i = 0; i < NumLods; i++) {
                    LodLayers[i].Write(bs);
                }
            }
        }

        public class LodLayer : LayerHeader
        {
            public uint LodIdx;
            public uint NumSubchunks;
            public uint DataOffset;
            public int  CompressedSize;
            public int  UncompressedSize;

            public SubChunkLayer[] SubChunkLayers;

            public override void Read(BinaryStream bs)
            {
                base.Read(bs);
                LodIdx           = bs.Read.UInt();
                NumSubchunks     = (uint) (1 << 2 * bs.Read.Int());
                DataOffset       = bs.Read.UInt();
                CompressedSize   = bs.Read.Int();
                UncompressedSize = bs.Read.Int();

                // The chunks
                SubChunkLayers = new SubChunkLayer[NumSubchunks];
                for (int i = 0; i < NumSubchunks; i++) {
                    var subChunk = new SubChunkLayer();
                    subChunk.Read(bs);
                    SubChunkLayers[i] = subChunk;
                }
            }

            public override void Write(BinaryStream bs)
            {
                base.Write(bs);
                bs.Write.UInt(LodIdx);
                // Stored as the exponent of 4
                bs.Write.Int(BitOperations.Log2(NumSubchunks) / 2);
                bs.Write.UInt(DataOffset);
                bs.Write.Int(CompressedSize);
                bs.Write.Int(UncompressedSize);

                for (int i = 0; i < NumSubchunks; i++) {
                    SubChunkLayers[i].Write(bs);
                }
            }
        }

        public class SubChunkLayer : LayerHeader
        {
            public uint    DataOffset;
            public int     CompressedSize;
            public int     UncompressedSize;
            public Vector3 BoundsMin;
            public Vector3 BoundsMax;

            public override void Read(BinaryStream bs)
            {
                base.Read(bs);
                DataOffset       = bs.Read.UInt();
                CompressedSize   = bs.Read.Int();
                UncompressedSize = bs.Read.Int();

                BoundsMin = bs.Read.Vector3();
                BoundsMax = bs.Read.Vector3();
            }

            public override void Write(BinaryStream bs)
            {
                base.Write(bs);
                bs.Write.UInt(DataOffset);
                bs.Write.Int(CompressedSize);
                bs.Write.Int(UncompressedSize);

                bs.Write.Vector3(BoundsMin);
                bs.Write.Vector3(BoundsMax);
            }
        }

        // A compressed block, zlib after a DATA id or LZMA after a DAT2 id and the LZMA properties
        public class Block : ReadWrite
        {
            public const uint DATA_ID = 0x41544144;
            public const uint DAT2_ID = 0x32544144;

            public uint   Id;
            public byte[] Properties;
            public byte[] CompressedData;

            // Both sizes count the id and the LZMA properties as well
            public int CompressedSize;
            public int UncompressedSize;

            public bool IsLzma => Id == DAT2_ID;

            public byte[] Decompress()
            {
                if (IsLzma)
                    return Lzma.Decompress(Properties, CompressedData, 0, CompressedData.Length, UncompressedSize);

                var decompressed = new byte[UncompressedSize];
                if (UncompressedSize == 0)
                    return decompressed;

                using Stream stream = new ZLibStream(new MemoryStream(CompressedData), CompressionMode.Decompress);
                stream.ReadAtLeast(decompressed, decompressed.Length, false);
                return decompressed;
            }

            public void Read(BinaryStream bs)
            {
                Id = bs.Read.UInt();
                if (Id == DAT2_ID) {
                    Properties     = bs.Read.ByteArray(5);
                    CompressedData = bs.Read.ByteArray(CompressedSize - 4 - 5);
                }
                else if (Id == DATA_ID) {
                    CompressedData = bs.Read.ByteArray(CompressedSize - 4);
                }
                else {
                    throw new InvalidDataException($"Unknown block id 0x{Id:X8}, expected DATA or DAT2");
                }
            }

            public void Write(BinaryStream bs)
            {
                bs.Write.UInt(Id);
                if (IsLzma) {
                    bs.Write.ByteArray(Properties);
                }
                bs.Write.ByteArray(CompressedData);
            }
        }

    #endregion
    }
}