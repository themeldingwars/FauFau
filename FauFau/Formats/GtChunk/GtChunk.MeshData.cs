using System;
using Bitter;
using FauFau.Util;
using Vector3 = System.Numerics.Vector3;

namespace FauFau.Formats.GtChunk
{
    public class GtChunk_MeshData : BinaryWrapper.ReadWrite
    {
        public int NodeLength;

        public uint   Magic;
        public ushort Version;
        public ushort Revision;
        public uint   Unk1;
        public int    NumPhysicsMats;
        public uint[] PhysicsMaterialIds;

        public int         NumVertBlocks;
        public Vector3[][] Verts;

        public int           NumIndiceBlocks;
        public IndiceBlock[] IndiceBlocks;

        public int        NumMatBlocks;
        public MatBlock[] MatBlocks;

        public int         NumMoppBlocks;
        public MoppBlock[] MoppBlocks;

        public byte[] HavokData;

        public GtChunk_MeshData(BinaryStream bs, int nodeLen)
        {
            NodeLength = nodeLen;
            Read(bs);
        }

        public void Read(BinaryStream bs)
        {
            var startPos = bs.ByteOffset;

            Magic    = bs.Read.UInt();
            Version  = bs.Read.UShort();
            Revision = bs.Read.UShort();
            Unk1     = bs.Read.UInt();

            // Physics mat ids
            NumPhysicsMats     = bs.Read.Int();
            PhysicsMaterialIds = bs.Read.UIntArray(NumPhysicsMats);

            // Verts
            NumVertBlocks = bs.Read.Int();
            Verts         = new Vector3[NumVertBlocks][];
            for (int i = 0; i < NumVertBlocks; i++) {
                int numVerts = bs.Read.Int();
                Verts[i] = bs.Read.Vector3Array(numVerts);
            }

            // Indice blocks
            NumIndiceBlocks = bs.Read.Int();
            IndiceBlocks    = new IndiceBlock[NumIndiceBlocks];
            for (int i = 0; i < NumIndiceBlocks; i++) {
                IndiceBlocks[i] = new IndiceBlock(bs);
            }

            // Mats
            NumMatBlocks = bs.Read.Int();
            MatBlocks    = new MatBlock[NumMatBlocks];
            for (int i = 0; i < NumMatBlocks; i++) {
                MatBlocks[i] = new MatBlock(bs);
            }

            // Mopps
            NumMoppBlocks = bs.Read.Int();
            MoppBlocks    = new MoppBlock[NumMoppBlocks];
            for (int i = 0; i < NumMoppBlocks; i++) {
                MoppBlocks[i] = new MoppBlock(bs);
            }

            // Havok data
            var readBytes = bs.ByteOffset - startPos;
            var leftBytes = NodeLength    - readBytes;
            HavokData = bs.Read.ByteArray((int) leftBytes);
        }

        public void Write(BinaryStream bs)
        {
            throw new System.NotImplementedException();
        }

        public class IndiceBlock : BinaryWrapper.ReadWrite
        {
            public enum IndiceTypes : uint
            {
                Shorts = 393218,
                Bytes  = 196609
            }

            public uint        NumIndices;
            public IndiceTypes IndiceType; // Prob more to this than just an int id
            public ushort[]    ShortIndices;
            public byte[]      ByteIndices;

            public IndiceBlock(BinaryStream bs)
            {
                Read(bs);
            }

            public void Read(BinaryStream bs)
            {
                NumIndices = bs.Read.UInt();
                var indiceType = bs.Read.UInt();
                IndiceType = (IndiceTypes) indiceType;

                if (IndiceType == IndiceTypes.Shorts) {
                    ShortIndices = bs.Read.UShortArray((int) NumIndices * 3);
                }
                else if (IndiceType == IndiceTypes.Bytes) {
                    ByteIndices = bs.Read.ByteArray((int) NumIndices * 3);
                }
                else {
                    throw new Exception($"Unexpect indices type: {(uint) indiceType}, wat D:");
                }
            }

            public void Write(BinaryStream bs)
            {
                throw new System.NotImplementedException();
            }
        }

        public class MatBlock : BinaryWrapper.ReadWrite
        {
            public int    Count;
            public uint   Id;
            public byte[] MatIds;

            public MatBlock(BinaryStream bs)
            {
                Read(bs);
            }

            public void Read(BinaryStream bs)
            {
                Count = bs.Read.Int();
                Id    = bs.Read.UInt();

                MatIds = bs.Read.ByteArray(Count);
            }

            public void Write(BinaryStream bs)
            {
                bs.Write.Int(MatIds.Length);
                bs.Write.UInt(Id);
                bs.Write.ByteArray(MatIds);
            }
        }

        // WTF is a mopp anyway? the havok data mentions something mopp so maybe related?
        public class MoppBlock : BinaryWrapper.ReadWrite
        {
            public float[]  Floats;
            public uint     DataSize;
            public byte[]   Data;
            public byte     Unk1;
            public ushort   Unk2;
            public uint     NumShorts;
            public ushort[] Shorts;

            public MoppBlock(BinaryStream bs)
            {
                Read(bs);
            }

            public void Read(BinaryStream bs)
            {
                Floats    = bs.Read.FloatArray(4);
                DataSize  = bs.Read.UInt();
                Data      = bs.Read.ByteArray((int) DataSize);
                Unk1      = bs.Read.Byte();
                Unk2      = bs.Read.UShort();
                NumShorts = bs.Read.UInt();
                Shorts    = bs.Read.UShortArray((int) NumShorts);
            }

            public void Write(BinaryStream bs)
            {
                throw new System.NotImplementedException();
            }
        }
    }
}