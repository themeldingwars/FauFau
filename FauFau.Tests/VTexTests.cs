using System;
using System.IO;
using System.Linq;
using Bitter;
using FauFau.Formats;
using FauFau.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class VTexTests
    {
        private const int TileSize = VTexTile.LayerCount * VTexTile.LayerSize;

        private static byte[] CreateLayers()
        {
            byte[] layers = new byte[TileSize];
            Random random = new Random(1);
            for (int i = 0; i < layers.Length; i++)
            {
                layers[i] = (byte)(i % 7 == 0 ? random.Next(256) : i / 16 % 251);
            }
            return layers;
        }

        private static byte[] Header(byte[] properties, byte mode)
        {
            return properties.Concat(new byte[] { 0x00, 0xC0, 0x00, mode }).ToArray();
        }

        private static byte[][] SplitBlocksIntoPlaneStreams(byte[] layers)
        {
            byte[] planes = new byte[TileSize];
            for (int layer = 0; layer < VTexTile.LayerCount; layer++)
            {
                for (int block = 0; block < VTexTile.LayerSize / 16; block++)
                {
                    int source = layer * VTexTile.LayerSize + block * 16;
                    Array.Copy(layers, source, planes, 0xA800 + layer * 0x800 + block * 2, 2);
                    Array.Copy(layers, source + 2, planes, 0x3000 + layer * 0x1800 + block * 6, 6);
                    Array.Copy(layers, source + 8, planes, 0x7800 + layer * 0x1000 + block * 4, 4);
                    Array.Copy(layers, source + 12, planes, layer * 0x1000 + block * 4, 4);
                }
            }
            return new[] { planes[..0x3000], planes[0x3000..0x7800], planes[0x7800..] };
        }

        [TestMethod]
        public void Decode_Raw_SplitsLayers()
        {
            byte[] layers = CreateLayers();
            byte[] data = Header(new byte[5], 13).Concat(layers).ToArray();

            VTexTile tile = VTexTile.Decode(data);

            tile.Layers[0].ShouldBe(layers[..VTexTile.LayerSize]);
            tile.Layers[2].ShouldBe(layers[(2 * VTexTile.LayerSize)..]);
        }

        [TestMethod]
        [DataRow((byte)1)]
        [DataRow((byte)15)]
        public void Decode_Lzma_SplitsLayers(byte mode)
        {
            byte[] layers = CreateLayers();
            (byte[] properties, byte[] stream) = TestLzma.Compress(layers);
            byte[] data = Header(properties, mode).Concat(stream).ToArray();

            VTexTile tile = VTexTile.Decode(data);

            tile.Layers[1].ShouldBe(layers[VTexTile.LayerSize..(2 * VTexTile.LayerSize)]);
        }

        [TestMethod]
        public void Decode_LzmaPlanes_RebuildsBlocks()
        {
            byte[] layers = CreateLayers();
            var streams = SplitBlocksIntoPlaneStreams(layers).Select(TestLzma.Compress).ToArray();
            byte[] streamProperties = streams.Select(s => s.Properties[0]).ToArray();
            byte[] data = Header(streams[0].Properties, 16).Concat(streamProperties)
                .Concat(streams.SelectMany(s => s.Stream)).Concat(new byte[12]).ToArray();

            VTexTile tile = VTexTile.Decode(data);

            tile.Layers.SelectMany(l => l).ToArray().ShouldBe(layers);
        }

        [TestMethod]
        public void Crc_XorsTheLayerChecksums()
        {
            byte[] layers = CreateLayers();
            VTexTile tile = VTexTile.Decode(Header(new byte[5], 13).Concat(layers).ToArray());

            uint crc = tile.Crc;

            uint expected = Checksum.Crc32(layers.AsSpan(0, VTexTile.LayerSize)) ^ Checksum.Crc32(layers.AsSpan(VTexTile.LayerSize, VTexTile.LayerSize)) ^ Checksum.Crc32(layers.AsSpan(2 * VTexTile.LayerSize));
            crc.ShouldBe(expected);
        }

        [TestMethod]
        public void Decode_EntropyMode_Throws()
        {
            byte[] data = Header(new byte[5], 0).Concat(new byte[16]).ToArray();

            Action act = () => VTexTile.Decode(data);

            act.ShouldThrow<NotSupportedException>();
        }

        private static byte[] FastLzRepeatedRows(byte[] firstRow, byte[] otherRows, int rowCount)
        {
            MemoryStream output = new MemoryStream();
            output.WriteByte(15);
            output.Write(firstRow);
            output.WriteByte(15);
            output.Write(otherRows);
            int remaining = (rowCount - 2) * 16;
            while (remaining > 0)
            {
                int length = System.Math.Min(264, remaining);
                if (length < 3)
                {
                    length = 3;
                }
                if (length - 3 < 6)
                {
                    output.WriteByte((byte)((length - 2) << 5));
                }
                else
                {
                    output.WriteByte(0xE0);
                    output.WriteByte((byte)(length - 9));
                }
                output.WriteByte(15);
                remaining -= length;
            }
            return output.ToArray();
        }

        [TestMethod]
        public void Read_Index_ReadsImagesAndTileTables()
        {
            BinaryStream stream = new BinaryStream(new MemoryStream());
            stream.Write.UInt(1);
            stream.Write.UInt(1);
            stream.Write.UIntArray(new uint[] { 1, 2, 3 });
            stream.Write.FloatArray(Enumerable.Range(0, 9).Select(i => (float)i).ToArray());
            stream.Write.UShortArray(new ushort[] { 0, 0, 2048, 2048 });
            stream.Write.UIntArray(new uint[] { 18, 0, 34, 34 });
            stream.Write.UInt(0xDEADBEEF);
            for (int level = 0; level < VTexIndex.LevelCount; level++)
            {
                byte[] tile = BitConverter.GetBytes(1234UL).Concat(BitConverter.GetBytes(0xCAFEU)).Concat(BitConverter.GetBytes(99U)).ToArray();
                byte[] empty = Enumerable.Repeat((byte)0xFF, 16).ToArray();
                int side = 1024 >> level;
                byte[] compressed = FastLzRepeatedRows(level == 0 ? tile : empty, empty, side * side);
                stream.Write.Int(compressed.Length);
                stream.Write.ByteArray(compressed);
            }
            stream.ByteOffset = 0;
            VTexIndex index = new VTexIndex();

            index.Read(stream);

            index.Images.Count.ShouldBe(1);
            index.Images[0].Hashes.ShouldBe(new uint[] { 1, 2, 3 });
            index.Images[0].Bounds[2].ShouldBe((ushort)2048);
            index.Images[0].Crc.ShouldBe(0xDEADBEEF);
            index.Tiles[0][0].Exists.ShouldBeTrue();
            index.Tiles[0][0].Offset.ShouldBe(1234UL);
            index.Tiles[0][0].Size.ShouldBe(99U);
            index.Tiles[0][1].Exists.ShouldBeFalse();
            index.Tiles[0].Length.ShouldBe(1024 * 1024);
            index.Tiles[6][0].Exists.ShouldBeFalse();
        }

        [TestMethod]
        public void HashAssetPath_AssetId_MatchesClientHash()
        {
            uint hash = VTexIndex.HashAssetPath(113473);

            hash.ShouldBe(4264839707U);
        }
    }
}
