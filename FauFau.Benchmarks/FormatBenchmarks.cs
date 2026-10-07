using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Attributes;
using FauFau.Formats;

namespace FauFau.Benchmarks
{
    // Reads the texture and geometry formats of a real install, point FirefallSystemFolder at its system folder
    [MemoryDiagnoser]
    [ShortRunJob]
    public class FormatBenchmarks
    {
        private const string FirefallSystemFolder = @"C:\Program Files (x86)\Steam\steamapps\common\Firefall\system";
        private const int SampleSize = 64;

        private byte[] vtexIndex;
        private byte[] vgeoIndex;
        private byte[] lowMipPak;
        private byte[] cziMap;
        private byte[] cziPattern;
        private List<byte[]> lzmaTiles;
        private List<byte[]> planeTiles;
        private List<(byte[] Data, int VertexCount)> vgeoPages;
        private LowMipTexturePak pak;

        private string PathOf(string file)
        {
            return Path.Combine(FirefallSystemFolder, file);
        }

        [GlobalSetup]
        public void Setup()
        {
            if (!Directory.Exists(FirefallSystemFolder))
                throw new InvalidOperationException($"Set {nameof(FirefallSystemFolder)} in {nameof(FormatBenchmarks)}.cs to the system folder of a Firefall install");

            vtexIndex = File.ReadAllBytes(PathOf(@"vt\static.vtex_idx"));
            vgeoIndex = File.ReadAllBytes(PathOf(@"vg\static.vgeo_idx"));
            lowMipPak = File.ReadAllBytes(PathOf(@"vt\lowmiptextures.pak"));
            cziMap = File.ReadAllBytes(PathOf(@"assetdb\00103000\00103802.czi"));
            cziPattern = File.ReadAllBytes(PathOf(@"assetdb\00212000\00212898.czip"));

            // Every 1000th tile of level 0, split by mode
            VTexIndex index = new VTexIndex();
            index.Read(vtexIndex);
            lzmaTiles = new List<byte[]>();
            planeTiles = new List<byte[]>();
            using (FileStream vtex = File.OpenRead(PathOf(@"vt\static.vtex0")))
            {
                foreach (VTexIndex.TileInfo tile in index.Tiles[0].Where(t => t.Exists).Where((_, i) => i % 1000 == 0))
                {
                    byte[] data = new byte[tile.Size];
                    vtex.Position = (long)tile.Offset;
                    vtex.ReadExactly(data);
                    List<byte[]> target = data[8] == (byte)VTexTile.Mode.LzmaPlanes ? planeTiles : lzmaTiles;
                    if (target.Count < SampleSize)
                        target.Add(data);
                }
            }

            VGeoIndex geometry = new VGeoIndex();
            geometry.Read(vgeoIndex);
            vgeoPages = new List<(byte[], int)>();
            using (FileStream vgeo = File.OpenRead(PathOf(@"vg\static.vgeo")))
            {
                foreach (VGeoIndex.PageInfo page in geometry.Pages.Where(p => p.VertexCount > 0).Where((_, i) => i % 200 == 0).Take(SampleSize))
                {
                    byte[] data = new byte[page.Size];
                    vgeo.Position = (long)page.Offset;
                    vgeo.ReadExactly(data);
                    vgeoPages.Add((data, page.VertexCount));
                }
            }

            pak = new LowMipTexturePak();
            pak.Read(lowMipPak);
        }

        [Benchmark]
        public int ReadVTexIndex()
        {
            VTexIndex index = new VTexIndex();
            index.Read(vtexIndex);
            return index.Tiles[0].Length;
        }

        [Benchmark(OperationsPerInvoke = SampleSize)]
        public int DecodeVTexLzmaTiles()
        {
            int length = 0;
            foreach (byte[] data in lzmaTiles)
                length += VTexTile.Decode(data).Layers[0].Length;
            return length;
        }

        [Benchmark(OperationsPerInvoke = SampleSize)]
        public int DecodeVTexPlaneTiles()
        {
            int length = 0;
            foreach (byte[] data in planeTiles)
                length += VTexTile.Decode(data).Layers[0].Length;
            return length;
        }

        [Benchmark]
        public int ReadVGeoIndex()
        {
            VGeoIndex index = new VGeoIndex();
            index.Read(vgeoIndex);
            return index.Pages.Count;
        }

        [Benchmark(OperationsPerInvoke = SampleSize)]
        public int DecodeVGeoPages()
        {
            int length = 0;
            foreach ((byte[] data, int vertexCount) in vgeoPages)
                length += VGeoPage.Decode(data, vertexCount, null).Data.Length;
            return length;
        }

        [Benchmark]
        public int ReadLowMipPak()
        {
            LowMipTexturePak read = new LowMipTexturePak();
            read.Read(lowMipPak);
            return read.Entries.Count;
        }

        [Benchmark]
        public int DecodeLowMips()
        {
            int length = 0;
            foreach (LowMipTexturePak.Entry entry in pak.Entries)
                length += pak.GetData(entry).Length;
            return length;
        }

        [Benchmark]
        public int ReadCziMap()
        {
            Czi czi = new Czi();
            czi.Read(cziMap);
            return czi.Mips.Count;
        }

        [Benchmark]
        public int ReadCziPattern()
        {
            Czi czi = new Czi();
            czi.Read(cziPattern);
            return czi.Mips.Count;
        }
    }
}
