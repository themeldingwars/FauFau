using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using Bitter;
using FauFau.Formats.GtChunk;
using FauFau.Util;
using SharpCompress.Compressors.Deflate;
using SharpCompress.Compressors.LZMA;
using CompressionLevel = SharpCompress.Compressors.Deflate.CompressionLevel;

namespace FauFau.Formats
{
    // Terrain Chunks
    // Codes not the most optimised atm so TODO: revisit
    public class GtChunkV8 : BinaryWrapper
    {
        public const int   VERSION     = 8;
        public const ulong NODE_MARKER = 0x12ED5A12ED5B12ED;

        public RootNode         Root;
        public DataBlock[]      DataBlocks;
        public DatBlock[]       DatBlocks;
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
                LodData      = new byte[lod.UncompressedSize],
                SubChunkData = new byte[lod.NumSubchunks][]
            };

            int idx = 0;
            foreach (var subChunkId in lodData.DatBlockIds) {
                lodDecompressed.SubChunkData[idx] = GetDecompressedSubChunk(subChunkId).ToArray();
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
                    Console.WriteLine($" Node Offset: {bs.ByteOffset}");

                    switch ((NodeTypes) nodeHeader.NodeId) {
                        case NodeTypes.GeoData:
                        case NodeTypes.GeoData2:
                        case NodeTypes.GeoData3:
                        {
                            var geoData = new GtChunk_MeshData(bs, nodeHeader.Length);
                            var wrapper = new NodeDataWrapper(nodeHeader.NodeId, geoData);
                            nodes.Add(wrapper);
                            break;
                        }

                        default:
                        {
                            Console.WriteLine($"SubChunk node Id: {nodeHeader.NodeId}");
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
            List<DatBlock> datBlocks           = new List<DatBlock>(100);
            List<short>    datBlockLodMappings = new List<short>(100);
            DataBlocks = new DataBlock[Root.NumLods];
            LodDataMap = new LodDataMapping[Root.NumLods];
            for (int i = 0; i < DataBlocks.Length; i++) {
                var lod = Root.LodNodes[i];
                LodDataMap[i].DataBlockIdx = i;

                // Data blocks
                DataBlocks[i] = new DataBlock()
                {
                    CompressedSize   = lod.CompressedSize - 4, // id and unk ints
                    UncompressedSize = lod.UncompressedSize,
                    LodIdx           = i
                };

                DataBlocks[i].Read(bs);

                // Dat blocks
                datBlockLodMappings.Clear();
                for (int j = 0; j < lod.NumSubchunks; j++) {
                    var subChunk = lod.SubChunkNodes[j];
                    datBlockLodMappings.Add((short) datBlocks.Count);

                    var datBlock = new DatBlock()
                    {
                        CompressedSize   = subChunk.CompressedSize - (4 + 5), // id and unk ints
                        UncompressedSize = subChunk.UncompressedSize,
                        LodIdx           = i,
                        SubChunkIdx      = j
                    };

                    datBlock.Read(bs);
                    datBlocks.Add(datBlock);
                    LodDataMap[i].DatBlockIds = datBlockLodMappings.ToArray();
                }
            }

            DatBlocks = datBlocks.ToArray();
        }

        private Node ReadNodeHeader(BinaryStream bs)
        {
            var nodeHeader = new Node(bs);
            return nodeHeader;
        }

        public override void Write(BinaryStream bs)
        {
            base.Write(bs);
        }

    #region Types

        public enum NodeTypes : int
        {
            // Structure nodes
            Root     = 262144,
            LOD      = 262145,
            SubChunk = 262146,

            // Compressed block nodes
            TerrainChunk     = 262400,
            GeoData          = 262401,
            GeoData2         = 262405,
            GeoData3         = 262403,
            PropEncNameReg   = 262660,
            VegationChunk    = 262661,
            VegationChunk2   = 262665,
            OverlayChunk     = 262662,
            SectorsChunk     = 262663,
            WaterObjectChunk = 262664,
            PropChunk        = 262656,
            SubZoneGrid      = 262402,
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

            public void Read(BinaryStream bs)
            {
                NodeMarker = bs.Read.ULong();
                NodeId     = bs.Read.UInt();
                Length     = bs.Read.Int();
            }

            public void Write(BinaryStream bs)
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

            public new void Read(BinaryStream bs)
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

            public new void Write(BinaryStream bs)
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
            public uint UNK1;
            public int  CompressedSize;
            public int  UncompressedSize;

            public SubChunkNode[] SubChunkNodes;

            public new void Read(BinaryStream bs)
            {
                base.Read(bs);
                LodIdx           = bs.Read.UInt();
                NumSubchunks     = (uint) (1 << 2 * bs.Read.Int());
                UNK1             = bs.Read.UInt();
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

            public new void Write(BinaryStream bs)
            {
                base.Write(bs);
                bs.Write.UInt(LodIdx);
                bs.Write.UInt((uint) (1 >> (2 * (int) NumSubchunks)));
                bs.Write.UInt(UNK1);
                bs.Write.Int(UncompressedSize);
                bs.Write.Int(CompressedSize);

                for (int i = 0; i < NumSubchunks; i++) {
                    SubChunkNodes[i].Write(bs);
                }
            }
        }

        public class SubChunkNode : Node
        {
            public uint    UNK1;
            public int     CompressedSize;
            public int     UncompressedSize;
            public Vector3 BoundsMin;
            public Vector3 BoundsMax;

            public new void Read(BinaryStream bs)
            {
                base.Read(bs);
                UNK1             = bs.Read.UInt();
                CompressedSize   = bs.Read.Int();
                UncompressedSize = bs.Read.Int();

                BoundsMax = bs.Read.Vector3();
                BoundsMin = bs.Read.Vector3();
            }

            public new void Write(BinaryStream bs)
            {
                base.Write(bs);
                bs.Write.UInt(UNK1);
                bs.Write.Int(CompressedSize);
                bs.Write.Int(UncompressedSize);

                bs.Write.Vector3(BoundsMax);
                bs.Write.Vector3(BoundsMin);
            }
        }

        // A compressed block of memory
        public class DataBlock : ReadWrite
        {
            private const int    DATA_ID = 0x41544144;
            public        int    Unk1;
            public        byte[] CompressedData;

            public int CompressedSize;
            public int UncompressedSize;
            public int LodIdx;

            public void Read(BinaryStream bs)
            {
                var id = bs.Read.UInt();
                if (id != DATA_ID) throw new Exception("Didn't get a DATA id");
                //Unk1           = bs.Read.Int();
                //bs.Read.ByteArray(5 * 4);
                CompressedData = bs.Read.ByteArray(CompressedSize);
            }

            public void Write(BinaryStream bs)
            {
                bs.Write.UInt(DATA_ID);
                //bs.Write.Int(Unk1);
                bs.Write.ByteArray(CompressedData);
            }
        }

        // LZMA compressed subchunk data
        public class DatBlock : ReadWrite
        {
            private const int    DAT_ID = 0x32544144;
            public        byte[] Properites;
            public        byte[] CompressedData;

            public int CompressedSize;
            public int UncompressedSize;
            public int LodIdx;
            public int SubChunkIdx;

            // Decompressed the data in this block
            public byte[] Decompress()
            {
                var       decompressed = new byte[UncompressedSize];
                using var lzmaStream   = new LzmaStream(Properites, new MemoryStream(CompressedData));

                lzmaStream.Read(decompressed, 0, decompressed.Length);
                return decompressed;
            }

            public void Read(BinaryStream bs)
            {
                var id = bs.Read.UInt();
                if (id != DAT_ID) throw new Exception("Didn't get a DAT id");
                Properites     = bs.Read.ByteArray(5);
                CompressedData = bs.Read.ByteArray(CompressedSize);
            }

            public void Write(BinaryStream bs)
            {
                bs.Write.UInt(DAT_ID);
                //bs.Write.Int(Unk1);
                bs.Write.ByteArray(CompressedData);
            }
        }

    #endregion
    }
}