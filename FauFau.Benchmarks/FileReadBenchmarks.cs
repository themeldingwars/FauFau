using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using FauFau.Formats;

namespace FauFau.Benchmarks
{
    // Reads real game files, point SdbPath and NsrPath at your own copies
    [MemoryDiagnoser]
    [ShortRunJob]
    public class FileReadBenchmarks
    {
        private const string SdbPath = @"C:\Program Files (x86)\Steam\steamapps\common\Firefall\system\db\clientdb.sd2";
        private const string NsrPath = @"";

        private byte[] sdb;
        private byte[] nsr;

        private static byte[] Load(string path, string name)
        {
            if (!File.Exists(path))
                throw new InvalidOperationException($"Set {name} in {nameof(FileReadBenchmarks)}.cs to the path of the file to read");

            return File.ReadAllBytes(path);
        }

        [GlobalSetup(Target = nameof(ReadStaticDB))]
        public void SetupStaticDB()
        {
            sdb = Load(SdbPath, nameof(SdbPath));
        }

        [GlobalSetup(Target = nameof(ReadNsr))]
        public void SetupNsr()
        {
            nsr = Load(NsrPath, nameof(NsrPath));
        }

        [Benchmark]
        public int ReadStaticDB()
        {
            StaticDB db = new StaticDB();
            db.Read(sdb);
            return db.Tables.Count;
        }

        [Benchmark]
        public int ReadNsr()
        {
            Nsr replay = new Nsr();
            replay.Read(nsr);
            return replay.Packets.Count;
        }
    }
}
