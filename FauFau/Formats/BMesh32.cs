using System;
using System.Collections.Generic;
using FauFau.Util.CommmonDataTypes;
using Bitter;
using static FauFau.Formats.BMesh;

namespace FauFau.Formats
{
    public class BMesh32 : BinaryWrapper
    {
        public string Magic = "bMesh";
        public ushort Version = 32;
        public byte MeshUsage = 0;          // not used anymore
        public Box3 Bounds = new Box3();    // local space
        public TangentType TangentType;     // 0 = melody, 1 = turtle
        public bool TangentSplit = false;

        public List<Vector3> Vertices = new List<Vector3>();
        public List<Normal> Normals = new List<Normal>();
        public List<UV> TextureUVs = new List<UV>();
        public List<UV> NormalUVs = new List<UV>();
        public List<Tangent> Tangents = new List<Tangent>();
        public List<uint> VertexColors = new List<uint>();
        public List<uint> FaceIndex = new List<uint>();
        public List<Face> Faces = new List<Face>();
        public List<Bone> Bones = new List<Bone>();
        public List<Link> Links = new List<Link>();
        public List<BoneWeight> BoneWeights = new List<BoneWeight>();
        public List<Hardpoint> Hardpoints = new List<Hardpoint>();

        string ReferenceMeshFileName = string.Empty;
        ulong ReferenceMeshFileTime = 0;
        MeshType ReferenceMeshType = 0;

        public List<BaseVariantDiff> BaseVariantDiffs = new List<BaseVariantDiff>();
        public List<ConformWeight> ConformWeights = new List<ConformWeight>();
        public List<MaterialSection> MaterialSections = new List<MaterialSection>();

        public override void Read(BinaryStream bs)
        {
            BinaryReader Read = bs.Read;

            Magic = Read.String(5);
            Version = Read.UShort();
            MeshUsage = Read.Byte();
            Bounds = Read.Type<Box3>();
            TangentType = (TangentType)Read.Byte();
            TangentSplit = (Read.Byte() == 1);

            Vertices = bs.Read.TypeList<Vector3>(Read.Int());

            if (Read.Int() != 0) { throw new Exception("uh oh, this mesh has vertex lod positions"); }

            Normals = Read.TypeList<Normal>(Read.Int());
            TextureUVs = Read.TypeList<UV>(Read.Int());
            NormalUVs = Read.TypeList<UV>(Read.Int());
            Tangents = Read.TypeList<Tangent>(Read.Int());
            VertexColors = Read.UIntList(Read.Int());
            FaceIndex = Read.UIntList(Read.Int());
            Faces = Read.TypeList<Face>(Read.Int());
            Bones = Read.TypeList<Bone>(Read.Int());
            Links = Read.TypeList<Link>(Read.Int());
            BoneWeights = Read.TypeList<BoneWeight>(Read.Int());
            Hardpoints = Read.TypeList<Hardpoint>(Read.Int());

            int referenceMeshFileNameLength = Read.Int();
            if(referenceMeshFileNameLength > 0)
            {
                ReferenceMeshFileName = Read.String();
            }
            
            ReferenceMeshFileTime = Read.ULong();
            ReferenceMeshType = (MeshType)Read.Byte();

            BaseVariantDiffs = Read.TypeList<BaseVariantDiff>(Read.Int());
            ConformWeights = Read.TypeList<ConformWeight>(Read.Int());
            MaterialSections = Read.TypeList<MaterialSection>(Read.Int());

            Console.WriteLine(Hardpoints.Count);

        }
        public override void Write(BinaryStream bs)
        {
            BinaryWriter Write = bs.Write;

            Write.String(Magic);
            Write.UShort(Version);
            Write.Byte(MeshUsage);
            Write.Type(Bounds);
            Write.Byte((byte)TangentType);
            Write.Byte((byte)(TangentSplit ? 1 : 0));

            bs.Write.Int(Vertices.Count);
            bs.Write.TypeList(Vertices);

            Write.UInt(0); // aint nobody got time for vertex lod positions

            bs.Write.Int(Normals.Count);
            Write.TypeList(Normals);

            bs.Write.Int(TextureUVs.Count);
            Write.TypeList(TextureUVs);

            bs.Write.Int(NormalUVs.Count);
            Write.TypeList(NormalUVs);

            bs.Write.Int(Tangents.Count);
            Write.TypeList(Tangents);

            bs.Write.Int(VertexColors.Count);
            Write.UIntList(VertexColors);

            bs.Write.Int(FaceIndex.Count);
            Write.UIntList(FaceIndex);

            bs.Write.Int(Faces.Count);
            Write.TypeList(Faces);

            bs.Write.Int(Bones.Count);
            Write.TypeList(Bones);

            bs.Write.Int(Links.Count);
            Write.TypeList(Links);

            bs.Write.Int(BoneWeights.Count);
            Write.TypeList(BoneWeights);

            bs.Write.Int(Hardpoints.Count);
            Write.TypeList(Hardpoints);

            Write.Int(ReferenceMeshFileName.Length);
            Write.String(ReferenceMeshFileName);
            Write.ULong(ReferenceMeshFileTime);
            Write.Byte((byte)ReferenceMeshType);

            bs.Write.Int(BaseVariantDiffs.Count);
            Write.TypeList(BaseVariantDiffs);

            bs.Write.Int(ConformWeights.Count);
            Write.TypeList(ConformWeights);

            bs.Write.Int(MaterialSections.Count);
            Write.TypeList(MaterialSections);

        }

        private static string FloatFill(float f)
        {
            return '\0' + f.ToString("0.000000");
        }
        private static string Vector4Fill(Vector4 v)
        {
            return FloatFill(v.x) + FloatFill(v.y) + FloatFill(v.z) + FloatFill(v.w);
        }

        private static float ByteToRange(byte b)
        {
            return b * (2.0f / 255.0f) - 1.0f;
        }
        private static byte RangeToByte(float f)
        {
            return (byte) (((255 * ((f < -1 ? -1 : f > 1 ? 1 : f) + 1)) / 2) + 0.5f);
        }

        public class Normal : ReadWrite
        {
            public float x;
            public float y;
            public float z;

            public void Read(BinaryStream bs)
            {
                x = ByteToRange(bs.Read.Byte());
                y = ByteToRange(bs.Read.Byte());
                z = ByteToRange(bs.Read.Byte());
                bs.Read.Byte(); // unused/padding
            }

            public void Write(BinaryStream bs)
            {
                bs.Write.Byte(RangeToByte(x));
                bs.Write.Byte(RangeToByte(y));
                bs.Write.Byte(RangeToByte(z));
                bs.Write.Byte(0);
            }
        }
        public class Tangent : ReadWrite
        {
            public float x;
            public float y;
            public float z;
            public bool reverseBitangent; // is the bitangent be reversed/mirrored?

            public void Read(BinaryStream bs)
            {
                x = ByteToRange(bs.Read.Byte());
                y = ByteToRange(bs.Read.Byte());
                z = ByteToRange(bs.Read.Byte());
                reverseBitangent = bs.Read.Byte() == 255;
            }

            public void Write(BinaryStream bs)
            {
                bs.Write.Byte(RangeToByte(x));
                bs.Write.Byte(RangeToByte(y));
                bs.Write.Byte(RangeToByte(z));
                bs.Write.Byte((byte)(reverseBitangent ? 255 : 0));
            }
        }
        public class UV : ReadWrite
        {
            public float u;
            public float v;

            public void Read(BinaryStream bs)
            {
                u = bs.Read.Float();
                v = bs.Read.Float();
            }

            public void Write(BinaryStream bs)
            {
                bs.Write.Float(u);
                bs.Write.Float(v);
            }
        }
        public class Face : ReadWrite
        {
            public uint v1;
            public uint v2;
            public uint v3;

            public void Read(BinaryStream bs)
            {
                v1 = bs.Read.UInt();
                v2 = bs.Read.UInt();
                v3 = bs.Read.UInt();
            }

            public void Write(BinaryStream bs)
            {
                bs.Write.UInt(v1);
                bs.Write.UInt(v2);
                bs.Write.UInt(v3);
            }
        }
        public class Bone : ReadWrite
        {
            public string name; // 64 char
            public int parent;
            public Matrix4x4 bindMatrix;
            public Matrix4x4 inverseBindMatrix;
            public bool hasSkinnedVertices; // byte(0/1) + 3 bytes padding

            public void Read(BinaryStream bs)
            {
                this.name = bs.Read.String(64).Trim().Split('\0')[0];
                this.parent = bs.Read.Int();
                this.bindMatrix = bs.Read.Type<Matrix4x4>();
                this.inverseBindMatrix = bs.Read.Type<Matrix4x4>();
                this.hasSkinnedVertices = (bs.Read.Byte() == 1);
                bs.Read.ByteArray(3); // padding
            }

            public void Write(BinaryStream bs)
            {
                // make sure name is ok
                string n = this.name.TrimEnd();
                if (n.Length < 64)
                {
                    string fill = "" + '\0' + this.parent;

                    fill += Vector4Fill(bindMatrix.x);
                    fill += Vector4Fill(bindMatrix.y);
                    fill += Vector4Fill(bindMatrix.z);
                    fill += Vector4Fill(bindMatrix.w);

                    fill = fill.Replace(',', '.');
                    n += fill;

                }
                n = n.Substring(0, 63) + '\0';

                // write
                bs.Write.String(n); // name string
                bs.Write.Int(parent);
                bs.Write.Type(bindMatrix);
                bs.Write.Type(inverseBindMatrix);
                bs.Write.Byte((byte)(hasSkinnedVertices ? 1 : 0));
                bs.Write.ByteArray(new byte[3]); // padding
            }
        }
        public class Link : ReadWrite
        {
            public uint startWeight;
            public uint weightCount; // 1-4

            public void Read(BinaryStream bs)
            {
                startWeight = bs.Read.UInt();
                weightCount = bs.Read.UInt();
            }

            public void Write(BinaryStream bs)
            {
                bs.Write.UInt(startWeight);
                bs.Write.UInt(weightCount);
            }
        }
        public class BoneWeight : ReadWrite
        {
            public byte influence;
            public byte boneIndex;

            public void Read(BinaryStream bs)
            {
                influence = bs.Read.Byte();
                boneIndex = bs.Read.Byte();
            }

            public void Write(BinaryStream bs)
            {
                bs.Write.Byte(influence);
                bs.Write.Byte(boneIndex);
            }
        }
        public class Hardpoint : ReadWrite
        {
            public string name; // 64 char 
            public int parent;
            public Matrix4x4 bindMatrix;              // absolute transform for the hard point
            public Matrix4x4 inverseBindMatrix;       // used for skinning
            public Matrix4x4 hpToBoneMatrix;          // goes from hard point space to bone space if there's a parent bone (= bindMatrix * bone->inverseBindMatrix)
            public Matrix4x4 normalizedBindMatrix;
            public Matrix4x4 normalizedHpToBoneMatrix;

            public void Read(BinaryStream bs)
            {
                this.name = bs.Read.String(64).Trim().Split('\0')[0];
                this.parent = bs.Read.Int();
                this.bindMatrix = bs.Read.Type<Matrix4x4>();
                this.inverseBindMatrix = bs.Read.Type<Matrix4x4>();
                this.hpToBoneMatrix = bs.Read.Type<Matrix4x4>();
                this.normalizedBindMatrix = bs.Read.Type<Matrix4x4>();
                this.normalizedHpToBoneMatrix = bs.Read.Type<Matrix4x4>();
            }

            public void Write(BinaryStream bs)
            {
                // make sure name is ok
                string n = this.name.TrimEnd();
                if (n.Length < 64)
                {
                    string fill = "" + '\0' + this.parent;

                    fill += Vector4Fill(bindMatrix.x);
                    fill += Vector4Fill(bindMatrix.y);
                    fill += Vector4Fill(bindMatrix.z);
                    fill += Vector4Fill(bindMatrix.w);

                    fill = fill.Replace(',', '.');
                    n += fill;

                }
                n = n.Substring(0, 63) + '\0';

                // write
                bs.Write.String(n); // name string
                bs.Write.Int(parent);

                bs.Write.Type(bindMatrix);
                bs.Write.Type(inverseBindMatrix);
                bs.Write.Type(hpToBoneMatrix);
                bs.Write.Type(normalizedBindMatrix);
                bs.Write.Type(normalizedHpToBoneMatrix);

            }
        }
        public class MaterialSection : ReadWrite
        {
            //public uint nameLength;
            public string name;
            public uint faceStart;
            public uint faceCount;
            public uint vertexMin;
            public uint vertexMax;

            public void Read(BinaryStream bs)
            {
                this.name = bs.Read.String((int)bs.Read.UInt());
                this.faceStart = bs.Read.UInt();
                this.faceCount = bs.Read.UInt();
                this.vertexMin = bs.Read.UInt();
                this.vertexMax = bs.Read.UInt();
            }
            public void Write(BinaryStream bs)
            {
                bs.Write.UInt((uint)name.Length);
                bs.Write.String(name);
                bs.Write.UInt(this.faceStart);
                bs.Write.UInt(this.faceCount);
                bs.Write.UInt(this.vertexMin);
                bs.Write.UInt(this.vertexMax);
            }
        }

        // A "base variant" is for instance a head type. There's a base head and different variants of that head.
        // Base variant diffs encode the positional difference to that base head.
        // Conform meshes are for instance hair or beards.
        // The base variant/conform tech is used to modify the hair/beard meshes so they can fit on any head.
        public class BaseVariantDiff : ReadWrite
        {
            public ushort faceIndex;
            public float[] baryWeights;

            public void Read(BinaryStream bs)
            {
                this.faceIndex = bs.Read.UShort();
                this.baryWeights = bs.Read.HalfArray(2);
            }
            public void Write(BinaryStream bs)
            {
                bs.Write.UShort(faceIndex);
                bs.Write.HalfArray(baryWeights);
            }
        }
        public class ConformWeight : ReadWrite
        {
            public ushort[] geomIndex = new ushort[3];
            public float[] weight = new float[3];

            public void Read(BinaryStream bs)
            {
                for (int i = 0; i < 3; i++)
                {
                    this.geomIndex[i] = bs.Read.UShort();
                    this.weight[i] = bs.Read.Half();
                }
            }
            public void Write(BinaryStream bs)
            {
                for (int i = 0; i < 3; i++)
                {
                    bs.Write.UShort(geomIndex[i]);
                    bs.Write.Half(weight[i]);
                }
            }
        }
        public class VertexLodPosition : ReadWrite
        {
            public void Read(BinaryStream bs)
            {
                throw new Exception("uh oh, a wild vertex lod position appears");
            }
            public void Write(BinaryStream bs)
            {
                throw new Exception("uh oh, a wild vertex lod position appears");
            }
        }
    }
}
