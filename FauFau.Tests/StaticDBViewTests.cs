using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using FauFau.Formats;
using FauFau.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using static FauFau.Formats.StaticDB;
using CDT = FauFau.Util.CommmonDataTypes;

namespace FauFau.Tests
{
    [TestClass]
    public class StaticDBViewTests
    {
        private const string TableName = "dbtest::Thing";
        private const HeaderFlags ClientFlags = HeaderFlags.ObfuscatedPool | HeaderFlags.Compressed | HeaderFlags.Client;

        private static byte[] WriteFile(HeaderFlags flags, params (string Name, DBType Type, bool Nullable, object[] Values)[] columns)
        {
            Table table = new Table
            {
                Id = Checksum.FFnv32(TableName),
                Columns = columns.Select(c => new StaticDB.Column { Id = Checksum.FFnv32(c.Name), Type = c.Type }).ToList(),
                Rows = new List<StaticDB.Row>(),
            };
            table.NullableColumn = table.Columns.Where((_, i) => columns[i].Nullable).ToList();
            for (int y = 0; y < columns[0].Values.Length; y++)
            {
                StaticDB.Row row = new StaticDB.Row();
                foreach (var column in columns)
                    row.Fields.Add(column.Values[y]);
                table.Rows.Add(row);
            }

            StaticDB sdb = new StaticDB
            {
                Patch = "test-1962",
                Flags = flags,
                Timestamp = new DateTime(2016, 11, 15, 18, 30, 0, DateTimeKind.Utc),
                Tables = new List<Table> { table },
            };
            sdb.Write(out byte[] bytes);
            return bytes;
        }

        private static byte[] WriteSample(HeaderFlags flags)
        {
            return WriteFile(flags,
                ("id", DBType.UInt, false, new object[] { 1U, 2U, 3U }),
                ("name", DBType.String, true, new object[] { "first", null, "first" }),
                ("value", DBType.Float, true, new object[] { 1.5f, null, -4f }));
        }

        private static StaticDBView.Row FirstRow(StaticDBView view)
        {
            return view.GetTableByName(TableName)[0];
        }

        [TestMethod]
        public void Open_ReadsHeader()
        {
            byte[] bytes = WriteSample(ClientFlags);

            using StaticDBView view = StaticDBView.Open(bytes);

            view.Patch.ShouldBe("test-1962");
            view.Flags.ShouldBe(ClientFlags);
            view.Timestamp.ShouldBe(new DateTime(2016, 11, 15, 18, 30, 0, DateTimeKind.Utc));
            view.FileVersion.ShouldBe(12U);
            view.MemoryVersion.ShouldBe(1002U);
        }

        [TestMethod]
        public void Open_ReadsSchema()
        {
            byte[] bytes = WriteSample(ClientFlags);

            using StaticDBView view = StaticDBView.Open(bytes);
            StaticDBView.Table table = view.GetTableByName(TableName);

            view.Count.ShouldBe(1);
            table.Count.ShouldBe(3);
            table.ColumnCount.ShouldBe(3);
            table.Columns[table.GetColumnIndexByName("name")].Type.ShouldBe(DBType.String);
            table.Columns[table.GetColumnIndexByName("value")].IsNullable.ShouldBeTrue();
            table.Columns[table.GetColumnIndexByName("id")].IsNullable.ShouldBeFalse();
            table.Columns[2].Offset.ShouldBe(8);
        }

        [TestMethod]
        [DataRow(HeaderFlags.Compressed)]
        [DataRow(HeaderFlags.ObfuscatedPool | HeaderFlags.Compressed)]
        [DataRow(ClientFlags)]
        public void Open_PoolFlags_ReadsStrings(HeaderFlags flags)
        {
            byte[] bytes = WriteSample(flags);

            using StaticDBView view = StaticDBView.Open(bytes);
            StaticDBView.Table table = view.GetTableByName(TableName);
            string[] names = table.Select(row => row.GetString(1)).ToArray();

            names.ShouldBe(new[] { "first", null, "first" });
        }

        [TestMethod]
        public void GetValue_MatchesStaticDB()
        {
            byte[] bytes = WriteSample(ClientFlags);
            StaticDB sdb = new StaticDB();
            sdb.Read(bytes);

            using StaticDBView view = StaticDBView.Open(bytes);
            object[][] values = view[0].Select(row => Enumerable.Range(0, 3).Select(row.GetValue).ToArray()).ToArray();

            values.ShouldBe(sdb[0].Rows.Select(row => row.Fields.ToArray()).ToArray());
        }

        [TestMethod]
        public void Getters_ReadFixedSizeTypes()
        {
            byte[] bytes = WriteFile(HeaderFlags.Compressed,
                ("byte", DBType.Byte, false, new object[] { (byte)200 }),
                ("sbyte", DBType.SByte, false, new object[] { (sbyte)-100 }),
                ("ushort", DBType.UShort, false, new object[] { (ushort)60000 }),
                ("short", DBType.Short, false, new object[] { (short)-30000 }),
                ("uint", DBType.UInt, false, new object[] { 4000000000U }),
                ("int", DBType.Int, false, new object[] { -2000000000 }),
                ("ulong", DBType.ULong, false, new object[] { 18000000000000000000UL }),
                ("long", DBType.Long, false, new object[] { -9000000000000000000L }),
                ("float", DBType.Float, false, new object[] { 0.25f }),
                ("double", DBType.Double, false, new object[] { -0.125 }),
                ("char", DBType.AsciiChar, false, new object[] { 'x' }),
                ("half", DBType.Half, false, new object[] { 1.5f }));

            using StaticDBView view = StaticDBView.Open(bytes);
            StaticDBView.Row row = FirstRow(view);

            row.GetByte(0).ShouldBe((byte)200);
            row.GetSByte(1).ShouldBe((sbyte)-100);
            row.GetUShort(2).ShouldBe((ushort)60000);
            row.GetShort(3).ShouldBe((short)-30000);
            row.GetUInt(4).ShouldBe(4000000000U);
            row.GetInt(5).ShouldBe(-2000000000);
            row.GetULong(6).ShouldBe(18000000000000000000UL);
            row.GetLong(7).ShouldBe(-9000000000000000000L);
            row.GetFloat(8).ShouldBe(0.25f);
            row.GetDouble(9).ShouldBe(-0.125);
            row.GetAsciiChar(10).ShouldBe('x');
            row.GetHalf(11).ShouldBe(1.5f);
        }

        [TestMethod]
        public void Getters_ReadVectorAndMatrixTypes()
        {
            CDT.Vector4 Row(float x) => new CDT.Vector4 { x = x, y = x + 1, z = x + 2, w = x + 3 };
            CDT.Half3 HalfRow(float x) => new CDT.Half3 { x = x, y = x + 1, z = x + 2 };
            byte[] bytes = WriteFile(HeaderFlags.Compressed,
                ("v2", DBType.Vector2, false, new object[] { new CDT.Vector2 { x = 1, y = 2 } }),
                ("v3", DBType.Vector3, false, new object[] { new CDT.Vector3 { x = 1, y = 2, z = 3 } }),
                ("v4", DBType.Vector4, false, new object[] { Row(1) }),
                ("m", DBType.Matrix4x4, false, new object[] { new CDT.Matrix4x4 { x = Row(1), y = Row(5), z = Row(9), w = Row(13) } }),
                ("box", DBType.Box3, false, new object[] { new CDT.Box3 { min = new CDT.Vector3 { x = -1, y = -2, z = -3 }, max = new CDT.Vector3 { x = 1, y = 2, z = 3 } } }),
                ("hm", DBType.HalfMatrix4x3, false, new object[] { new CDT.HalfMatrix4x3 { x = HalfRow(1), y = HalfRow(4), z = HalfRow(7), w = HalfRow(10) } }));

            using StaticDBView view = StaticDBView.Open(bytes);
            StaticDBView.Row row = FirstRow(view);

            row.GetVector2(0).ShouldBe(new Vector2(1, 2));
            row.GetVector3(1).ShouldBe(new Vector3(1, 2, 3));
            row.GetVector4(2).ShouldBe(new Vector4(1, 2, 3, 4));
            row.GetMatrix4x4(3).ShouldBe(new Matrix4x4(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16));
            row.GetBox3(4).ShouldBe((new Vector3(-1, -2, -3), new Vector3(1, 2, 3)));
            row.GetHalfMatrix4x3(5).ShouldBe(new Matrix4x4(1, 2, 3, 0, 4, 5, 6, 0, 7, 8, 9, 0, 10, 11, 12, 1));
        }

        [TestMethod]
        [DataRow(HeaderFlags.Compressed)]
        [DataRow(ClientFlags)]
        public void Getters_ReadPoolTypes(HeaderFlags flags)
        {
            byte[] bytes = WriteFile(flags,
                ("blob", DBType.Blob, false, new object[] { new List<byte> { 1, 2, 3 } }),
                ("ushorts", DBType.UShortArray, false, new object[] { new List<ushort> { 1, 60000 } }),
                ("uints", DBType.UIntArray, false, new object[] { new List<uint> { 7, 4000000000U } }),
                ("v2s", DBType.Vector2Array, false, new object[] { new List<CDT.Vector2> { new CDT.Vector2 { x = 1, y = 2 } } }),
                ("v3s", DBType.Vector3Array, false, new object[] { new List<CDT.Vector3> { new CDT.Vector3 { x = 1, y = 2, z = 3 } } }),
                ("v4s", DBType.Vector4Array, false, new object[] { new List<CDT.Vector4> { new CDT.Vector4 { x = 1, y = 2, z = 3, w = 4 } } }));

            using StaticDBView view = StaticDBView.Open(bytes);
            StaticDBView.Row row = FirstRow(view);

            row.GetBytes(0).ToArray().ShouldBe(new byte[] { 1, 2, 3 });
            row.GetUShorts(1).ToArray().ShouldBe(new ushort[] { 1, 60000 });
            row.GetUInts(2).ToArray().ShouldBe(new uint[] { 7, 4000000000U });
            row.GetVector2s(3).ToArray().ShouldBe(new[] { new Vector2(1, 2) });
            row.GetVector3s(4).ToArray().ShouldBe(new[] { new Vector3(1, 2, 3) });
            row.GetVector4s(5).ToArray().ShouldBe(new[] { new Vector4(1, 2, 3, 4) });
        }

        [TestMethod]
        public void GetBytes_String_ReturnsTerminatedUtf8()
        {
            byte[] bytes = WriteSample(ClientFlags);

            using StaticDBView view = StaticDBView.Open(bytes);
            byte[] name = FirstRow(view).GetBytes(1).ToArray();

            name.ShouldBe(Encoding.UTF8.GetBytes("first\0"));
        }

        [TestMethod]
        public void NullCells_AreNullOrEmpty()
        {
            byte[] bytes = WriteSample(ClientFlags);

            using StaticDBView view = StaticDBView.Open(bytes);
            StaticDBView.Row row = view.GetTableByName(TableName)[1];

            row.IsNull(0).ShouldBeFalse();
            row.IsNull(1).ShouldBeTrue();
            row.IsNull(2).ShouldBeTrue();
            row.GetString(1).ShouldBeNull();
            row.GetBytes(1).IsEmpty.ShouldBeTrue();
            row.GetValue(2).ShouldBeNull();
        }

        [TestMethod]
        public void GetHalf_MatchesStaticDB()
        {
            object[] values = Enumerable.Range(0, 65536).Select(i => (object)(ushort)i).ToArray();
            byte[] file = WriteFile(HeaderFlags.Compressed, ("half", DBType.UShort, false, values));
            byte[] payload = StaticDBTests.InflatePayload(file);
            const int fieldTypePosition = 6 + 11 + 7;
            payload[fieldTypePosition] = (byte)DBType.Half;
            byte[] bytes = StaticDBTests.BuildFile(file, payload);
            StaticDB sdb = new StaticDB();
            sdb.Read(bytes);

            using StaticDBView view = StaticDBView.Open(bytes);
            int[] halfs = view[0].Select(row => BitConverter.SingleToInt32Bits(row.GetHalf(0))).ToArray();
            int[] expected = sdb[0].Rows.Select(row => BitConverter.SingleToInt32Bits((float)row[0])).ToArray();

            halfs.Where((bits, i) => bits != expected[i] && !(float.IsNaN(BitConverter.Int32BitsToSingle(bits)) && float.IsNaN(BitConverter.Int32BitsToSingle(expected[i])))).ShouldBeEmpty();
        }

        [TestMethod]
        public void Getter_WrongType_ThrowsInvalidCast()
        {
            byte[] bytes = WriteSample(ClientFlags);
            using StaticDBView view = StaticDBView.Open(bytes);
            StaticDBView.Row row = FirstRow(view);

            Action act = () => row.GetInt(0);

            act.ShouldThrow<InvalidCastException>();
        }

        [TestMethod]
        public void GetBytes_FixedSizeColumn_ThrowsInvalidCast()
        {
            byte[] bytes = WriteSample(ClientFlags);
            using StaticDBView view = StaticDBView.Open(bytes);
            StaticDBView.Row row = FirstRow(view);

            Action act = () => row.GetBytes(0);

            act.ShouldThrow<InvalidCastException>();
        }

        [TestMethod]
        public void Row_AfterDispose_ThrowsObjectDisposed()
        {
            byte[] bytes = WriteSample(ClientFlags);
            StaticDBView view = StaticDBView.Open(bytes);
            StaticDBView.Row row = FirstRow(view);
            view.Dispose();

            Action act = () => row.GetUInt(0);

            act.ShouldThrow<ObjectDisposedException>();
        }

        [TestMethod]
        public void Open_Path_ReadsFile()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, WriteSample(ClientFlags));

                using StaticDBView view = StaticDBView.Open(path);
                uint id = view.GetTableByName(TableName)[2].GetUInt(0);

                id.ShouldBe(3U);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void Open_LeavesCallerArrayUntouched()
        {
            byte[] bytes = WriteSample(ClientFlags);
            byte[] copy = bytes.ToArray();

            StaticDBView.Open(bytes).Dispose();

            bytes.ShouldBe(copy);
        }

        [TestMethod]
        public void Open_NotAStaticDB_ThrowsInvalidData()
        {
            byte[] bytes = new byte[256];

            Action act = () => StaticDBView.Open(bytes);

            act.ShouldThrow<InvalidDataException>();
        }

        [TestMethod]
        public void Open_OverlappingPoolEntries_ThrowsNotSupported()
        {
            byte[] file = WriteFile(HeaderFlags.Compressed | HeaderFlags.Client,
                ("id", DBType.UInt, false, new object[] { 1U, 2U }),
                ("name", DBType.String, false, new object[] { "first", "second" }));
            byte[] payload = StaticDBTests.InflatePayload(file);
            const int rowInfoPosition = 6 + 11 + 2 * 8;
            const int rowSize = 8;
            int rowOffset = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(rowInfoPosition));
            uint firstKey = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(rowOffset + 4));
            uint firstData = (firstKey >> 1) + 2;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(rowOffset + rowSize + 4), (2U << 24) | ((firstData + 1) << 1));
            byte[] bytes = StaticDBTests.BuildFile(file, payload);

            Action act = () => StaticDBView.Open(bytes);

            act.ShouldThrow<NotSupportedException>();
        }

        [TestMethod]
        public void Lookups_ResolveByNameAndId()
        {
            byte[] bytes = WriteSample(ClientFlags);
            uint id = Checksum.FFnv32(TableName);

            using StaticDBView view = StaticDBView.Open(bytes);
            int indexByName = view.GetIndexByName(TableName);
            bool found = view.TryGetTable(id, out StaticDBView.Table table);
            bool foundMissing = view.TryGetTable(1, out _);
            Action missing = () => view.GetTableByName("dbtest::Missing");

            indexByName.ShouldBe(0);
            found.ShouldBeTrue();
            table.ShouldBeSameAs(view[0]);
            foundMissing.ShouldBeFalse();
            missing.ShouldThrow<KeyNotFoundException>();
        }
    }
}
