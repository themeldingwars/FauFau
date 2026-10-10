using System;
using System.IO;
using System.Numerics;

namespace FauFau.Formats
{
    // The ENWF data of the collision layers of chunks and the world chunk import of zones. Revision 2 has the collision
    // mesh in front of the Havok data, the other revisions nothing but the Havok data.
    public sealed class EnwfLayer : GtLayer
    {
        public const uint MagicId = 0x46574E45;

        public uint Magic = MagicId;
        public ushort Version;
        public ushort Revision;
        public uint EnwfId;
        public uint[] PhysicsMaterialIds = Array.Empty<uint>();

        public Vector3[][] VertBlocks = Array.Empty<Vector3[]>();
        public IndiceBlock[] IndiceBlocks = Array.Empty<IndiceBlock>();
        public MatItem[] MatItems = Array.Empty<MatItem>();
        public MoppBlock[] MoppBlocks = Array.Empty<MoppBlock>();

        // A Havok binary tagfile
        public byte[] HavokData = Array.Empty<byte>();

        public EnwfLayer(uint id = WorldLayerIds.StaticGeometryCollision) : base(id)
        {
        }

        public bool HasMesh => Revision == 2;

        protected override void ReadData(ReadOnlySpan<byte> data)
        {
            LayerReader read = new LayerReader(data);
            Magic = read.UInt();
            if (Magic != MagicId)
                throw new InvalidDataException($"Expected ENWF, got 0x{Magic:X8}");

            Version = read.UShort();
            Revision = read.UShort();
            EnwfId = read.UInt();

            PhysicsMaterialIds = new uint[read.UInt()];
            for (int i = 0; i < PhysicsMaterialIds.Length; i++)
            {
                PhysicsMaterialIds[i] = read.UInt();
            }

            VertBlocks = Array.Empty<Vector3[]>();
            IndiceBlocks = Array.Empty<IndiceBlock>();
            MatItems = Array.Empty<MatItem>();
            MoppBlocks = Array.Empty<MoppBlock>();
            if (HasMesh)
            {
                VertBlocks = new Vector3[read.UInt()][];
                for (int i = 0; i < VertBlocks.Length; i++)
                {
                    Vector3[] verts = new Vector3[read.UInt()];
                    for (int j = 0; j < verts.Length; j++)
                    {
                        verts[j] = read.Vector3();
                    }
                    VertBlocks[i] = verts;
                }

                IndiceBlocks = new IndiceBlock[read.UInt()];
                for (int i = 0; i < IndiceBlocks.Length; i++)
                {
                    IndiceBlocks[i] = IndiceBlock.Read(ref read);
                }

                MatItems = new MatItem[read.UInt()];
                for (int i = 0; i < MatItems.Length; i++)
                {
                    int length = (int)read.UInt();
                    MatItems[i] = new MatItem { Id = read.UInt(), Data = read.Bytes(length) };
                }

                MoppBlocks = new MoppBlock[read.UInt()];
                for (int i = 0; i < MoppBlocks.Length; i++)
                {
                    MoppBlocks[i] = MoppBlock.Read(ref read);
                }
            }

            HavokData = read.Bytes(read.Remaining);
        }

        protected override void WriteData(System.IO.BinaryWriter writer)
        {
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(Revision);
            writer.Write(EnwfId);
            writer.Write((uint)PhysicsMaterialIds.Length);
            foreach (uint id in PhysicsMaterialIds)
            {
                writer.Write(id);
            }

            if (HasMesh)
            {
                writer.Write((uint)VertBlocks.Length);
                foreach (Vector3[] verts in VertBlocks)
                {
                    writer.Write((uint)verts.Length);
                    foreach (Vector3 vert in verts)
                    {
                        writer.Write(vert);
                    }
                }

                writer.Write((uint)IndiceBlocks.Length);
                foreach (IndiceBlock block in IndiceBlocks)
                {
                    block.Write(writer);
                }

                writer.Write((uint)MatItems.Length);
                foreach (MatItem item in MatItems)
                {
                    writer.Write((uint)item.Data.Length);
                    writer.Write(item.Id);
                    writer.Write(item.Data);
                }

                writer.Write((uint)MoppBlocks.Length);
                foreach (MoppBlock block in MoppBlocks)
                {
                    block.Write(writer);
                }
            }

            writer.Write(HavokData);
        }

        public sealed class IndiceBlock
        {
            public enum IndiceTypes : uint
            {
                Shorts = 0x60002,
                Bytes = 0x30001,
            }

            public IndiceTypes IndiceType = IndiceTypes.Shorts;

            // Three per triangle, written as bytes or shorts depending on the type
            public ushort[] Indices = Array.Empty<ushort>();

            public int TriangleCount => Indices.Length / 3;

            internal static IndiceBlock Read(ref LayerReader read)
            {
                uint triangles = read.UInt();
                IndiceBlock block = new IndiceBlock { IndiceType = (IndiceTypes)read.UInt() };
                if (block.IndiceType != IndiceTypes.Shorts && block.IndiceType != IndiceTypes.Bytes)
                    throw new InvalidDataException($"Unknown indice type 0x{(uint)block.IndiceType:X}");

                block.Indices = new ushort[triangles * 3];
                for (int i = 0; i < block.Indices.Length; i++)
                {
                    block.Indices[i] = block.IndiceType == IndiceTypes.Shorts ? read.UShort() : read.Byte();
                }
                return block;
            }

            internal void Write(System.IO.BinaryWriter writer)
            {
                writer.Write((uint)TriangleCount);
                writer.Write((uint)IndiceType);
                foreach (ushort index in Indices)
                {
                    if (IndiceType == IndiceTypes.Shorts)
                        writer.Write(index);
                    else
                        writer.Write((byte)index);
                }
            }
        }

        public sealed class MatItem
        {
            public uint Id;
            public byte[] Data = Array.Empty<byte>();
        }

        // Havok MOPP code, the bounding volume tree of the mesh
        public sealed class MoppBlock
        {
            public Vector4 Floats;
            public byte[] Data = Array.Empty<byte>();
            public byte Unk1;
            public ushort Unk2;
            public ushort[] Shorts = Array.Empty<ushort>();

            internal static MoppBlock Read(ref LayerReader read)
            {
                MoppBlock block = new MoppBlock { Floats = read.Vector4() };
                block.Data = read.Bytes((int)read.UInt());
                block.Unk1 = read.Byte();
                block.Unk2 = read.UShort();
                block.Shorts = new ushort[read.UInt()];
                for (int i = 0; i < block.Shorts.Length; i++)
                {
                    block.Shorts[i] = read.UShort();
                }
                return block;
            }

            internal void Write(System.IO.BinaryWriter writer)
            {
                writer.Write(Floats);
                writer.Write((uint)Data.Length);
                writer.Write(Data);
                writer.Write(Unk1);
                writer.Write(Unk2);
                writer.Write((uint)Shorts.Length);
                foreach (ushort value in Shorts)
                {
                    writer.Write(value);
                }
            }
        }
    }
}
