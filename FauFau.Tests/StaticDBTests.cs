using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bitter;
using FauFau.Formats;
using FauFau.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using static FauFau.Formats.StaticDB;

namespace FauFau.Tests
{
    [TestClass]
    public class StaticDBTests
    {
        private const string TableName = "dbtest::Thing";

        private static StaticDB CreateSample()
        {
            Column id = new Column { Id = Checksum.FFnv32("id"), Type = DBType.UInt };
            Column name = new Column { Id = Checksum.FFnv32("name"), Type = DBType.String };
            Column value = new Column { Id = Checksum.FFnv32("value"), Type = DBType.Float };

            Table table = new Table
            {
                Id = Checksum.FFnv32(TableName),
                Columns = new List<Column> { id, name, value },
                NullableColumn = new List<Column> { value },
                Rows = new List<Row>(),
            };

            table.Rows.Add(new Row { Fields = { 1U, "first", 1.5f } });
            table.Rows.Add(new Row { Fields = { 2U, "second", null } });
            table.Rows.Add(new Row { Fields = { 3U, "first", -4f } });

            return new StaticDB
            {
                Patch = "test-1962",
                Flags = HeaderFlags.ObfuscatedPool,
                Tables = new List<Table> { table },
            };
        }

        private static StaticDB RoundTrip(StaticDB sdb)
        {
            sdb.Write(out byte[] bytes);

            StaticDB read = new StaticDB();
            read.Read(bytes);
            return read;
        }

        [TestMethod]
        public void WriteRead_KeepsHeader()
        {
            StaticDB sdb = CreateSample();

            StaticDB read = RoundTrip(sdb);

            read.Patch.ShouldBe("test-1962");
            read.Flags.ShouldBe(HeaderFlags.ObfuscatedPool);
        }

        [TestMethod]
        public void WriteRead_KeepsSchema()
        {
            StaticDB sdb = CreateSample();

            StaticDB read = RoundTrip(sdb);

            read.Tables.Count.ShouldBe(1);
            Table table = read.GetTableByName(TableName);
            table.Columns.Count.ShouldBe(3);
            table.Columns[table.GetColumnIndexByName("id")].Type.ShouldBe(DBType.UInt);
            table.Columns[table.GetColumnIndexByName("name")].Type.ShouldBe(DBType.String);
            table.IsColumnNullable(table.GetColumnByName("value")).ShouldBeTrue();
            table.IsColumnNullable(table.GetColumnByName("id")).ShouldBeFalse();
        }

        [TestMethod]
        public void WriteRead_KeepsRows()
        {
            StaticDB sdb = CreateSample();

            Table table = RoundTrip(sdb).GetTableByName(TableName);

            table.Rows.Count.ShouldBe(3);
            table[0].Fields.ShouldBe(new object[] { 1U, "first", 1.5f });
            table[1].Fields.ShouldBe(new object[] { 2U, "second", null });
            table[2].Fields.ShouldBe(new object[] { 3U, "first", -4f });
        }

        [TestMethod]
        public void Write_LeavesRowsUntouched()
        {
            StaticDB sdb = CreateSample();

            sdb.Write(out byte[] first);
            sdb.Write(out byte[] second);

            sdb[0][0].Fields.ShouldBe(new object[] { 1U, "first", 1.5f });
            second.ShouldBe(first);
        }

        [TestMethod]
        public void Write_KeepsTimestamp()
        {
            StaticDB sdb = CreateSample();
            sdb.Timestamp = new DateTime(2016, 11, 15, 18, 30, 0, DateTimeKind.Utc);

            StaticDB read = RoundTrip(sdb);

            read.Timestamp.ShouldBe(sdb.Timestamp);
        }

        [TestMethod]
        public void WriteRead_WithoutObfuscation_KeepsRows()
        {
            StaticDB sdb = CreateSample();
            sdb.Flags = 0;

            StaticDB read = RoundTrip(sdb);

            read.Flags.ShouldBe((HeaderFlags)0);
            read.GetTableByName(TableName)[1].Fields.ShouldBe(new object[] { 2U, "second", null });
        }

        [TestMethod]
        public void WriteRead_NullableTableEndingOn128Bytes_KeepsRows()
        {
            Column id = new Column { Id = Checksum.FFnv32("id"), Type = DBType.UInt };
            Column value = new Column { Id = Checksum.FFnv32("value"), Type = DBType.Float };
            Table table = new Table
            {
                Id = Checksum.FFnv32(TableName),
                Columns = new List<Column> { id, value },
                NullableColumn = new List<Column> { value },
                Rows = new List<Row>(),
            };
            for (uint i = 0; i < 11; i++)
                table.Rows.Add(new Row { Fields = { i, i % 2 == 0 ? null : (object)(float)i } });
            StaticDB sdb = new StaticDB { Patch = "test-1962", Flags = HeaderFlags.ObfuscatedPool, Tables = new List<Table> { table } };

            Table read = RoundTrip(sdb).GetTableByName(TableName);

            read.Rows.Count.ShouldBe(11);
            read[9].Fields.ShouldBe(new object[] { 9U, 9f });
            read[10].Fields.ShouldBe(new object[] { 10U, null });
        }

        [TestMethod]
        public void Read_SeveralInstancesInParallel_KeepTheirOwnData()
        {
            byte[][] files = new byte[8][];
            for (int i = 0; i < files.Length; i++)
            {
                StaticDB sdb = CreateSample();
                sdb[0][0][1] = "file " + i;
                sdb.Write(out files[i]);
            }
            StaticDB[] read = new StaticDB[files.Length];

            Parallel.For(0, files.Length, i =>
            {
                read[i] = new StaticDB();
                read[i].Read(files[i]);
            });

            read.Select(sdb => sdb[0][0][1]).ShouldBe(Enumerable.Range(0, files.Length).Select(i => (object)("file " + i)));
        }

        [TestMethod]
        public void Read_LeavesCallerStreamOpen()
        {
            CreateSample().Write(out byte[] bytes);
            BinaryStream stream = new BinaryStream(new MemoryStream(bytes));

            new StaticDB().Read(stream);
            stream.ByteOffset = 0;
            uint magic = stream.Read.UInt();

            magic.ShouldBe(0xDA7ABA5EU);
        }

        [TestMethod]
        public void GetTableByName_Missing_ThrowsKeyNotFound()
        {
            StaticDB sdb = CreateSample();

            Action act = () => sdb.GetTableByName("dbtest::Missing");

            act.ShouldThrow<KeyNotFoundException>();
        }

        [TestMethod]
        public void GetColumnByName_Missing_ThrowsKeyNotFound()
        {
            StaticDB sdb = CreateSample();

            Action act = () => sdb[0].GetColumnByName("missing");

            act.ShouldThrow<KeyNotFoundException>();
        }

        [TestMethod]
        public void Lookups_ResolveByNameAndId()
        {
            StaticDB sdb = CreateSample();
            uint id = Checksum.FFnv32(TableName);

            int indexByName = sdb.GetIndexByName(TableName);
            int indexById = sdb.GetIndexById(id);
            Table tableById = sdb.GetTableById(id);
            int missingTable = sdb.GetIndexByName("dbtest::Missing");
            int missingColumn = sdb[0].GetColumnIndexByName("missing");

            indexByName.ShouldBe(0);
            indexById.ShouldBe(0);
            tableById.ShouldBeSameAs(sdb[0]);
            missingTable.ShouldBe(-1);
            missingColumn.ShouldBe(-1);
        }

        [TestMethod]
        [DataRow(DBType.String, true)]
        [DataRow(DBType.Blob, true)]
        [DataRow(DBType.UIntArray, true)]
        [DataRow(DBType.UInt, false)]
        [DataRow(DBType.Vector3, false)]
        public void IsDataType_OnlyPoolTypes(DBType type, bool expected)
        {
            bool isDataType = IsDataType(type);

            isDataType.ShouldBe(expected);
        }

        [TestMethod]
        [DataRow(DBType.UInt, 1002U, 4)]
        [DataRow(DBType.Vector3, 1002U, 12)]
        [DataRow(DBType.String, 1002U, 4)]
        [DataRow(DBType.String, 1000U, 8)]
        public void DBTypeLength_MatchesMemoryVersion(DBType type, uint memoryVersion, int expected)
        {
            byte length = DBTypeLength(type, memoryVersion);

            length.ShouldBe((byte)expected);
        }
    }
}
