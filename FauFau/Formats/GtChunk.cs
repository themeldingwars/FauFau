using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using Bitter;
using FauFau.Formats.GtChunk;
using FauFau.Util;
using SharpCompress.Compressors.LZMA;

namespace FauFau.Formats
{
    // Terrain Chunks
    // Codes not the most optimised atm so TODO: revisit
    public class GtChunkV8 : BinaryWrapper
    {
        public const int   VERSION     = 8;
        public const ulong NODE_MARKER = 0x12ED5A12ED5B12ED;

        public RootNode         Root;
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

        // Check if the node id and version match
        public bool CheckIsValid(BinaryStream bs)
        {
            var node = new Node();
            node.Read(bs);
            var version = bs.Read.UInt();

            var isValid = node.NodeMarker == NODE_MARKER && version == VERSION;
            return isValid;
        }

        public override void Read(BinaryStream bs)
        {
            Root = new RootNode();
            Root.Read(bs);

            LoadCompressedBlocks(bs);
        }

        public LodSubChunkData GetDecompressedLod(int lodLevel)
        {
            var lod     = Root.LodNodes[lodLevel];
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

        public List<NodeDataWrapper> GetSubChunkNodes(int subChunkIdx)
        {
            var nodes = new List<NodeDataWrapper>();

            var sc = GetDecompressedSubChunk(subChunkIdx);
            using (var bs = new BinaryStream(new MemoryStream(sc.ToArray()))) {
                while (bs.ByteOffset < bs.Length) {
                    var nodeHeader = ReadNodeHeader(bs);

                    switch ((NodeTypes) nodeHeader.NodeId) {
                        case NodeTypes.StaticGeometryCollision:
                        case NodeTypes.MovementBlockerCollision:
                        case NodeTypes.WaterCollision:
                        {
                            var geoData = new GtChunk_MeshData(bs, nodeHeader.Length);
                            var wrapper = new NodeDataWrapper(nodeHeader.NodeId, geoData);
                            nodes.Add(wrapper);
                            break;
                        }

                        default:
                        {
                            var nodeData = bs.Read.ByteArray(nodeHeader.Length);
                            var wrapper  = new NodeDataWrapper(nodeHeader.NodeId, nodeData);
                            nodes.Add(wrapper);

                            break;
                        }
                    }
                }
            }

            return nodes;
        }

        // load the compressed chunks into memory, doesn't decompress them
        private void LoadCompressedBlocks(BinaryStream bs)
        {
            // The block offsets are relative to the end of the root node
            long dataStart = Node.HeaderLength + Root.Length;

            List<Block> datBlocks           = new List<Block>(100);
            List<short> datBlockLodMappings = new List<short>(100);
            DataBlocks = new Block[Root.NumLods];
            LodDataMap = new LodDataMapping[Root.NumLods];
            for (int i = 0; i < DataBlocks.Length; i++) {
                var lod = Root.LodNodes[i];
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
                    var subChunk = lod.SubChunkNodes[j];
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

        private Node ReadNodeHeader(BinaryStream bs)
        {
            var nodeHeader = new Node(bs);
            return nodeHeader;
        }

    #region Types

        public enum NodeTypes : int
        {
            // Structure nodes
            Root     = 262144,
            LOD      = 262145,
            SubChunk = 262146,

            // Compressed block nodes
            TerrainChunk             = 262400,
            StaticGeometryCollision  = 262401,
            SubZoneGrid              = 262402,
            MovementBlockerCollision = 262403,
            EncounterNameRegistry2   = 262404,
            WaterCollision           = 262405,
            PropChunk                = 262656,
            GeometryTree2            = 262659,
            PropEncNameReg           = 262660,
            VegetationChunk          = 262661,
            OverlayChunk             = 262662,
            SectorsChunk             = 262663,
            WaterObjectChunk         = 262664,
            VegetationChunk2         = 262665,
            GeometryTree             = 262672,
        }

        // casting and boxing yay, but can revise later if its really an issue in how it ends up getting used
        public struct NodeDataWrapper
        {
            public uint   NodeId;
            public object NodeData;

            public NodeDataWrapper(uint nodeType, object obj)
            {
                NodeId   = nodeType;
                NodeData = obj;
            }

            public NodeTypes NodeType => (NodeTypes) NodeId;

            public GtChunk_MeshData AsMeshData => NodeData as GtChunk_MeshData;
        }

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

        public class Node : ReadWrite
        {
            public const int HeaderLength = 16;

            public ulong NodeMarker;
            public uint  NodeId;
            public int   Length;

            public NodeTypes NodeType => (NodeTypes) NodeId;

            public Node(BinaryStream bs)
            {
                Read(bs);
            }

            public Node()
            {
            }

            public virtual void Read(BinaryStream bs)
            {
                NodeMarker = bs.Read.ULong();
                NodeId     = bs.Read.UInt();
                Length     = bs.Read.Int();
            }

            public virtual void Write(BinaryStream bs)
            {
                bs.Write.ULong(NODE_MARKER);
                bs.Write.UInt(NodeId);
                bs.Write.Int(Length);
            }
        }

        public class RootNode : Node
        {
            public uint  Version;
            public ulong Timestamp;
            public uint  NumLods;

            public LodNode[] LodNodes;

            public DateTime TimeStamp => Util.Time.DateTimeFromUnixTimestampMilliseconds((long)Timestamp);

            public override void Read(BinaryStream bs)
            {
                base.Read(bs);
                Version   = bs.Read.UInt();
                Timestamp = bs.Read.ULong();
                NumLods   = bs.Read.UInt();

                LodNodes = new LodNode[NumLods];
                for (int i = 0; i < NumLods; i++) {
                    var lodNode = new LodNode();
                    lodNode.Read(bs);
                    LodNodes[i] = lodNode;
                }
            }

            public override void Write(BinaryStream bs)
            {
                base.Write(bs);
                bs.Write.UInt(Version);
                bs.Write.ULong(Timestamp);
                bs.Write.UInt(NumLods);

                for (int i = 0; i < NumLods; i++) {
                    LodNodes[i].Write(bs);
                }
            }
        }

        public class LodNode : Node
        {
            public uint LodIdx;
            public uint NumSubchunks;
            public uint DataOffset;
            public int  CompressedSize;
            public int  UncompressedSize;

            public SubChunkNode[] SubChunkNodes;

            public override void Read(BinaryStream bs)
            {
                base.Read(bs);
                LodIdx           = bs.Read.UInt();
                NumSubchunks     = (uint) (1 << 2 * bs.Read.Int());
                DataOffset       = bs.Read.UInt();
                CompressedSize   = bs.Read.Int();
                UncompressedSize = bs.Read.Int();

                // The chunks
                SubChunkNodes = new SubChunkNode[NumSubchunks];
                for (int i = 0; i < NumSubchunks; i++) {
                    var subChunk = new SubChunkNode();
                    subChunk.Read(bs);
                    SubChunkNodes[i] = subChunk;
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
                    SubChunkNodes[i].Write(bs);
                }
            }
        }

        public class SubChunkNode : Node
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
                var decompressed = new byte[UncompressedSize];
                if (UncompressedSize == 0)
                    return decompressed;

                using Stream stream = IsLzma
                    ? LzmaStream.Create(Properties, new MemoryStream(CompressedData))
                    : new ZLibStream(new MemoryStream(CompressedData), CompressionMode.Decompress);
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