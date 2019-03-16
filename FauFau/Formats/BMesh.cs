using FauFau.Util.CommmonDataTypes;
using Bitter;
using System;
using System.Collections.Generic;
using System.Text;

namespace FauFau.Formats
{
    public class BMesh : BinaryWrapper
    {
        public List<Vertex> Vertices = new List<Vertex>();

        public override void Read(BinaryStream bs)
        {
            if(bs.Length < 7 || !bs.Read.String(5).Equals("bMesh"))
            {
                throw new Exception("uh oh, this dosnt seem to be a bMesh file?");
            }

            ushort version = bs.Read.UShort();
            bs.ByteOffset = 0;

            switch(version)
            {
                case 32:
                    ReadBMesh32(bs);
                    break;
                default:
                    throw new Exception("uh oh, i dont know how to parse bMesh version " + version);
            }
        }
        public override void Write(BinaryStream bs)
        {
            base.Write(bs);
        }

        private void ReadBMesh32(BinaryStream bs)
        {
            BMesh32 bm = new BMesh32();
            bm.Read(bs);

            if (bm.Vertices.Count != bm.Normals.Count)
            {
                throw new Exception("uh oh, this mesh has " + bm.Vertices.Count + " vertices and " + bm.Normals.Count + " normals??");
            }

            Vertices = new List<Vertex>(bm.Vertices.Count);
            for (int i = 0; i < bm.Vertices.Count; i++)
            {

                Vertices.Add(new Vertex
                {
                    x = bm.Vertices[i].x,
                    y = bm.Vertices[i].y,
                    z = bm.Vertices[i].z,

                    normal = new Normal
                    {
                        x = bm.Normals[i].x,
                        y = bm.Normals[i].y,
                        z = bm.Normals[i].z
                    }
                });
            }        
        }




        public class Vertex
        {
            public float x;
            public float y;
            public float z;
            public Normal normal;
        }
        public class Normal
        {
            public float x;
            public float y;
            public float z;
        }
        public class Tangent
        {
            public float x;
            public float y;
            public float z;
            public bool reverseBitangent;
        }

        public enum TangentType : byte
        {
            Melody = 0,
            Turtle = 1
        }
        public enum MeshType : byte
        {
            None = 0,
            BaseVariant = 1,
            ConformMesh = 2
        }
    }
}
