using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using FauFau.Formats;
using static FauFau.Formats.StaticDB;

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

        [GlobalSetup(Targets = new[] { nameof(ReadStaticDB), nameof(OpenStaticDBView), nameof(ScanStaticDBView), nameof(ScanStaticDBViewValues) })]
        public void SetupStaticDB()
        {
            sdb = Load(SdbPath, nameof(SdbPath));
        }

        [GlobalSetup(Targets = new[] { nameof(ReadNsr), nameof(ScanNsrView) })]
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
        public int OpenStaticDBView()
        {
            using StaticDBView db = StaticDBView.Open(sdb);
            return db.Count;
        }

        // Touches every cell through the typed getters, strings stay UTF-8 bytes
        [Benchmark]
        public long ScanStaticDBView()
        {
            using StaticDBView db = StaticDBView.Open(sdb);
            long sum = 0;
            foreach (StaticDBView.Table table in db)
            {
                ReadOnlySpan<StaticDBView.Column> columns = table.Columns;
                foreach (StaticDBView.Row row in table)
                {
                    for (int x = 0; x < columns.Length; x++)
                    {
                        sum += ReadCell(row, x, columns[x].Type);
                    }
                }
            }
            return sum;
        }

        // Touches every cell as the object StaticDB would hold
        [Benchmark]
        public long ScanStaticDBViewValues()
        {
            using StaticDBView db = StaticDBView.Open(sdb);
            long count = 0;
            foreach (StaticDBView.Table table in db)
            {
                foreach (StaticDBView.Row row in table)
                {
                    for (int x = 0; x < table.ColumnCount; x++)
                    {
                        if (row.GetValue(x) != null)
                            count++;
                    }
                }
            }
            return count;
        }

        private static long ReadCell(StaticDBView.Row row, int column, DBType type)
        {
            switch (type)
            {
                case DBType.Byte: return row.GetByte(column);
                case DBType.UShort: return row.GetUShort(column);
                case DBType.UInt: return row.GetUInt(column);
                case DBType.ULong: return (long)row.GetULong(column);
                case DBType.SByte: return row.GetSByte(column);
                case DBType.Short: return row.GetShort(column);
                case DBType.Int: return row.GetInt(column);
                case DBType.Long: return row.GetLong(column);
                case DBType.Float: return (long)row.GetFloat(column);
                case DBType.Double: return (long)row.GetDouble(column);
                case DBType.Half: return (long)row.GetHalf(column);
                case DBType.AsciiChar: return row.GetAsciiChar(column);
                case DBType.Vector2: return (long)row.GetVector2(column).X;
                case DBType.Vector3: return (long)row.GetVector3(column).X;
                case DBType.Vector4: return (long)row.GetVector4(column).X;
                case DBType.Matrix4x4: return (long)row.GetMatrix4x4(column).M11;
                case DBType.Box3: return (long)row.GetBox3(column).Min.X;
                case DBType.HalfMatrix4x3: return (long)row.GetHalfMatrix4x3(column).M11;
                case DBType.Unknown: return 0;
                default: return row.GetBytes(column).Length;
            }
        }

        [Benchmark]
        public int ReadNsr()
        {
            Nsr replay = new Nsr();
            replay.Read(nsr);
            return replay.Packets.Count;
        }

        // Touches every packet, the data stays in the view
        [Benchmark]
        public long ScanNsrView()
        {
            using NsrView replay = NsrView.Open(nsr);
            long length = 0;
            foreach (NsrView.Packet packet in replay)
            {
                length += packet.Data.Span.Length + packet.MessageId;
            }
            return length;
        }
    }
}
