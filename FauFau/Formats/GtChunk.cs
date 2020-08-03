using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Bitter;
using FauFau.Util;

namespace FauFau.Formats
{
    // Terrain Chunks
    public class GtChunkV8 : BinaryWrapper
    {
        public const int   VERSION     = 8;
        public const ulong NODE_MARKER = 0x12ED5A12ED5B12ED;

        public RootNode    Root;
        public DataBlock[] DataBlocks;
        public DatBlock[] DatBlocks;
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

        public void GetDecompressedLod(int lodLevel)
        {
            var lod = Root.LodNodes[lodLevel];
        }
        
        // load the compressed chunks into memory, doesn't decompress them
        private void LoadCompressedBlocks(BinaryStream bs)
        {
            List<DatBlock> datBlocks = new List<DatBlock>(100);
            List<short> datBlockLodMappings = new List<short>(100);
            DataBlocks = new DataBlock[Root.NumLods];
            LodDataMap = new LodDataMapping[Root.NumLods];
            for (int i = 0; i < DataBlocks.Length; i++) {
                var lod = Root.LodNodes[i];
                LodDataMap[i].DataBlockIdx = i;

                // Data blocks
                DataBlocks[i] = new DataBlock()
                {
                    CompressedSize = lod.CompressedSize - 8 // id and unk ints
                };

                DataBlocks[i].Read(bs);
                
                // Dat blocks
                datBlockLodMappings.Clear();
                for (int j = 0; j < lod.NumSubchunks; j++) {
                    var subChunk = lod.SubChunkNodes[j];
                    datBlockLodMappings.Add((short)datBlocks.Count);
                    
                    var datBlock = new DatBlock()
                    {
                        CompressedSize = subChunk.CompressedSize - 8 // id and unk ints
                    };

                    datBlock.Read(bs);
                    datBlocks.Add(datBlock);
                    LodDataMap[i].DatBlockIds = datBlockLodMappings.ToArray();
                }
            }

            DatBlocks = datBlocks.ToArray();
        }

        public override void Write(BinaryStream bs)
        {
            base.Write(bs);
        }

    #region Types

        public enum NodeTypes : int
        {
            Root     = 262144,
            LOD      = 262145,
            SubChunk = 262146
        }

        // Mapp an lod idx to compressed blocks
        public struct LodDataMapping
        {
            public int DataBlockIdx;
            public short[] DatBlockIds;
        }

        public class Node : ReadWrite
        {
            public ulong NodeMarker;
            public uint  NodeId;
            public int   Length;

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
            public int CompressedSize;
            public int UncompressedSize;

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
            public int    CompressedSize;
            public int    UncompressedSize;
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

            public void Read(BinaryStream bs)
            {
                var id = bs.Read.UInt();
                if (id != DATA_ID) throw new Exception("Didn't get a DATA id");
                Unk1           = bs.Read.Int();
                CompressedData = bs.Read.ByteArray(CompressedSize);
            }

            public void Write(BinaryStream bs)
            {
                bs.Write.UInt(DATA_ID);
                bs.Write.Int(Unk1);
                bs.Write.ByteArray(CompressedData);
            }
        }

        public class DatBlock : ReadWrite
        {
            private const int    DAT_ID = 0x32544144;
            public        int   Unk1;
            public        byte[] CompressedData;

            public int CompressedSize;

            public void Read(BinaryStream bs)
            {
                var id = bs.Read.UInt();
                if (id != DAT_ID) throw new Exception("Didn't get a DAT id");
                Unk1           = bs.Read.Int();
                CompressedData = bs.Read.ByteArray(CompressedSize);
            }

            public void Write(BinaryStream bs)
            {
                bs.Write.UInt(DAT_ID);
                bs.Write.Int(Unk1);
                bs.Write.ByteArray(CompressedData);
            }
        }

    #endregion
    }
}