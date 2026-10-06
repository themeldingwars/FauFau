using System.Collections.Generic;
using System.IO;
using Bitter;
using FauFau.Formats;
using FauFau.Util.CommmonDataTypes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class BMesh32Tests
    {
        private static BMesh32 CreateMesh(string sectionName)
        {
            return new BMesh32
            {
                Bounds = new Box3 { min = new Vector3(), max = new Vector3 { x = 1, y = 2, z = 3 } },
                Vertices = new List<Vector3> { new Vector3 { x = 1, y = 2, z = 3 } },
                FaceIndex = new List<uint> { 0 },
                MaterialSections = new List<BMesh32.MaterialSection>
                {
                    new BMesh32.MaterialSection { name = sectionName, faceStart = 0, faceCount = 1, vertexMin = 0, vertexMax = 0 },
                },
            };
        }

        private static (BMesh32 Mesh, bool ReadToEnd) RoundTrip(BMesh32 mesh)
        {
            mesh.Write(out byte[] bytes);
            BinaryStream stream = new BinaryStream(new MemoryStream(bytes));

            BMesh32 read = new BMesh32();
            read.Read(stream);
            return (read, stream.ByteOffset == stream.Length);
        }

        [TestMethod]
        public void WriteRead_NoVertexColors_ReadsWholeFile()
        {
            BMesh32 mesh = CreateMesh("default");

            (BMesh32 read, bool readToEnd) = RoundTrip(mesh);

            readToEnd.ShouldBeTrue();
            read.VertexColors.ShouldBeEmpty();
            read.Vertices[0].z.ShouldBe(3f);
            read.FaceIndex.ShouldBe(new uint[] { 0 });
            read.MaterialSections[0].name.ShouldBe("default");
        }

        [TestMethod]
        public void WriteRead_UnnamedMaterialSection_ReadsWholeFile()
        {
            BMesh32 mesh = CreateMesh("");

            (BMesh32 read, bool readToEnd) = RoundTrip(mesh);

            readToEnd.ShouldBeTrue();
            read.MaterialSections[0].name.ShouldBe("");
            read.MaterialSections[0].faceCount.ShouldBe(1U);
        }
    }
}
