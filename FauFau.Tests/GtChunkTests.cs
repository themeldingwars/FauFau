using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using Bitter;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using static FauFau.Formats.GtChunkV8;

namespace FauFau.Tests
{
    [TestClass]
    public class GtChunkTests
    {
        private static RootNode CreateRoot()
        {
            SubChunkNode[] subChunks = new SubChunkNode[4];
            for (int i = 0; i < subChunks.Length; i++)
                subChunks[i] = new SubChunkNode { NodeId = (uint)NodeTypes.SubChunk, CompressedSize = 100 + i, UncompressedSize = 200 + i };

            return new RootNode
            {
                NodeId = (uint)NodeTypes.Root,
                Version = VERSION,
                Timestamp = 1234,
                NumLods = 1,
                LodNodes = new[]
                {
                    new LodNode { NodeId = (uint)NodeTypes.LOD, NumSubchunks = 4, CompressedSize = 10, UncompressedSize = 20, SubChunkNodes = subChunks },
                },
            };
        }

        [TestMethod]
        public void RootNode_WriteReadThroughReadWrite_KeepsLodHeaders()
        {
            BinaryStream stream = new BinaryStream(new MemoryStream());
            CreateRoot().Write(stream);
            stream.ByteOffset = 0;

            RootNode read = stream.Read.Type<RootNode>();

            read.Version.ShouldBe((uint)VERSION);
            read.NumLods.ShouldBe(1U);
            read.LodNodes[0].NumSubchunks.ShouldBe(4U);
            read.LodNodes[0].CompressedSize.ShouldBe(10);
            read.LodNodes[0].UncompressedSize.ShouldBe(20);
            read.LodNodes[0].SubChunkNodes[3].UncompressedSize.ShouldBe(203);
            stream.ByteOffset.ShouldBe(stream.Length);
        }

        [TestMethod]
        public void SubChunkNode_Write_StoresMinBeforeMax()
        {
            SubChunkNode node = new SubChunkNode { BoundsMin = new Vector3(-256, -256, 0), BoundsMax = new Vector3(256, 256, 0) };
            BinaryStream stream = new BinaryStream(new MemoryStream());

            node.Write(stream);
            stream.ByteOffset = Node.HeaderLength + 12;
            float firstX = stream.Read.Float();

            firstX.ShouldBe(-256f);
        }

        [TestMethod]
        public void Block_Data_DecompressesZlib()
        {
            byte[] original = Enumerable.Range(0, 1000).Select(i => (byte)(i % 7)).ToArray();
            MemoryStream compressed = new MemoryStream();
            using (ZLibStream zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
                zlib.Write(original);
            BinaryStream stream = new BinaryStream(new MemoryStream());
            stream.Write.UInt(Block.DATA_ID);
            stream.Write.ByteArray(compressed.ToArray());
            stream.ByteOffset = 0;
            Block block = new Block { CompressedSize = (int)stream.Length, UncompressedSize = original.Length };

            block.Read(stream);
            byte[] decompressed = block.Decompress();

            block.IsLzma.ShouldBeFalse();
            decompressed.ShouldBe(original);
        }

        [TestMethod]
        public void Block_UnknownId_Throws()
        {
            BinaryStream stream = new BinaryStream(new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
            Block block = new Block { CompressedSize = 8 };

            System.Action act = () => block.Read(stream);

            act.ShouldThrow<InvalidDataException>();
        }
    }
}
