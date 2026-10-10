using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using FauFau.Formats;
using FauFau.Util;

namespace FauFau.Benchmarks
{
    // Reads real game files, point the paths at your own copies
    [MemoryDiagnoser]
    [ShortRunJob]
    public class AdditionsBenchmarks
    {
        private const string FirefallSystemFolder = @"C:\Program Files (x86)\Steam\steamapps\common\Firefall\system";
        private const string NsrPath = @"";
        private const string LegacySdbPath = @"";
        private const string NamesPath = @"";

        private byte[] nsr;
        private byte[] legacySdb;
        private byte[] zone;
        private byte[] chunk;
        private List<byte[]> localizedText;
        private string[] names;
        private uint[] ids;
        private StaticDBNames loadedNames;

        private static string PathOf(string file) => Path.Combine(FirefallSystemFolder, file);

        private static string Check(string path, string name)
        {
            if (!File.Exists(path))
                throw new InvalidOperationException($"Set {name} in {nameof(AdditionsBenchmarks)}.cs to the path of the file to read");

            return path;
        }

        [GlobalSetup]
        public void Setup()
        {
            nsr = File.ReadAllBytes(Check(NsrPath, nameof(NsrPath)));
            legacySdb = File.ReadAllBytes(Check(LegacySdbPath, nameof(LegacySdbPath)));
            zone = File.ReadAllBytes(PathOf(@"maps\1030.zone"));
            chunk = File.ReadAllBytes(PathOf(@"maps\chunks\1_0243_0915.gtchunk"));
            names = File.ReadAllLines(Check(NamesPath, nameof(NamesPath)));

            using StaticDBView sdb = StaticDBView.Open(File.ReadAllBytes(PathOf(@"db\clientdb.sd2")));
            localizedText = new List<byte[]>();
            foreach (StaticDBView.Row row in sdb.GetTableByName("dblocalization::LocalizedText"))
            {
                for (int column = 0; column < 6; column++)
                {
                    if (!row.IsNull(column))
                        localizedText.Add(row.GetBytes(column).ToArray());
                }
            }

            List<uint> tableAndColumnIds = new List<uint>();
            foreach (StaticDBView.Table table in sdb)
            {
                tableAndColumnIds.Add(table.Id);
                foreach (StaticDBView.Column column in table.Columns)
                    tableAndColumnIds.Add(column.Id);
            }
            ids = tableAndColumnIds.ToArray();

            loadedNames = new StaticDBNames();
            loadedNames.AddRange(names);
        }

        [Benchmark]
        public int NsrInfoRead()
        {
            return NsrInfo.Read(new MemoryStream(nsr, false)).Packets;
        }

        [Benchmark]
        public int NsrInfoReadHashed()
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            return NsrInfo.Read(new MemoryStream(nsr, false), hash).Packets;
        }

        [Benchmark]
        public int LegacyStaticDBRead()
        {
            LegacyStaticDB db = new LegacyStaticDB();
            db.Read(legacySdb);
            return db.Tables.Count;
        }

        [Benchmark]
        public int LocalizedTextDecode()
        {
            int length = 0;
            foreach (byte[] value in localizedText)
                length += LocalizedText.Decode(value).Length;

            return length;
        }

        [Benchmark]
        public int StaticDBNamesAdd()
        {
            StaticDBNames added = new StaticDBNames();
            added.AddRange(names);
            return added.Count;
        }

        [Benchmark]
        public int StaticDBNamesGetName()
        {
            int length = 0;
            foreach (uint id in ids)
                length += loadedNames.GetName(id).Length;

            return length;
        }

        [Benchmark]
        public int ZoneTypedLayers()
        {
            Zone read = new Zone();
            read.Read(zone);
            int count = read.GetChunks().Count;
            foreach (GtLayer layer in read.Root.Children)
            {
                switch (layer)
                {
                    case ZonePathLayer path:
                        count += path.Steps.Count;
                        break;
                    case SubZoneRegionLayer region:
                        count += region.Bitmap.Length;
                        break;
                    case GtContainerLayer melding when melding.Id == WorldLayerIds.Melding:
                        foreach (MeldingPerimeterLayer perimeter in melding.FindAll<MeldingPerimeterLayer>())
                            count += perimeter.Perimeters.Count;
                        break;
                }
            }
            return count;
        }

        [Benchmark]
        public int ChunkCollision()
        {
            GtChunkV8 read = new GtChunkV8();
            read.Read(chunk);
            int vertices = 0;
            for (int lod = 0; lod < read.LodDataMap.Length; lod++)
            {
                List<GtLayer> layers = read.GetLodLayers(lod);
                foreach (short subChunk in read.LodDataMap[lod].DatBlockIds)
                    layers.AddRange(read.GetSubChunkLayers(subChunk));

                foreach (GtLayer layer in layers)
                {
                    if (layer.Id != WorldLayerIds.StaticGeometryCollision || layer is not EnwfLayer enwf)
                        continue;

                    foreach (var block in enwf.VertBlocks)
                        vertices += block.Length;
                }
            }
            return vertices;
        }
    }
}
