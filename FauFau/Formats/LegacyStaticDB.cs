using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Bitter;
using FauFau.Util;
using FauFau.Util.CommmonDataTypes;
using BinaryReader = Bitter.BinaryReader;

namespace FauFau.Formats
{
    // The .sdb of the builds before 1297. Unlike the .sd2 it has the table and column names in plain text and the cells inline
    // in the rows. Read only.
    public class LegacyStaticDB : BinaryWrapper
    {
        // "DATABASE"
        public const uint PlainMagic = 0xDA7ABA5E;

        // "BETAFASE", from beta-1265 on: a patch number and the data after the table offsets scrambled
        public const uint ScrambledMagic = 0xBE7AFA5E;

        public uint Version;
        public uint Magic;
        public DateTime TimeStamp;
        public uint Flags;
        public uint Patch;

        // Strings are obfuscated when this isn't 0
        public uint ScramblerVersion;

        public List<Table> Tables = new ();

        public override void Read(BinaryStream bs)
        {
            BinaryReader Read = bs.Read;
            Version = Read.UInt();
            Magic = Read.UInt();
            TimeStamp = DateTime.UnixEpoch.AddSeconds(Read.UInt());
            Flags = Read.UInt();

            if (Magic == ScrambledMagic)
            {
                Patch = Read.UInt();
                ScramblerVersion = Read.UInt();
            }
            else if (Magic != PlainMagic)
            {
                throw new InvalidDataException($"Not a pre-1297 static database, the magic is 0x{Magic:X8}");
            }

            int tableCount = Read.Int();
            int[] tableOffsets = Read.IntArray(tableCount);

            // The offsets count from the start of the file, so keep the descrambled data at the same place
            bs.ByteOffset = 0;
            byte[] data = Read.ByteArray((int)bs.Length);
            if (Magic == ScrambledMagic && ScramblerVersion < 10000)
            {
                int start = 24 + 4 + tableCount * 4;
                MersenneTwister.Xor(Patch, data.AsSpan(start));
            }

            using BinaryStream tables = new BinaryStream(new MemoryStream(data, false));
            Tables = new List<Table>(tableCount);
            foreach (int offset in tableOffsets)
            {
                tables.ByteOffset = offset;
                Tables.Add(ReadTable(tables));
            }
        }

        public override void Write(BinaryStream bs)
        {
            throw new NotSupportedException("Writing pre-1297 static databases isn't supported");
        }

        public Table GetTableByName(string name)
        {
            return Tables.Find(table => table.Name == name);
        }

        private Table ReadTable(BinaryStream bs)
        {
            BinaryReader Read = bs.Read;
            Table table = new Table { Id = Read.UInt() };

            byte columnCount = Read.Byte();
            for (int i = 0; i < columnCount; i++)
            {
                table.Columns.Add(new Column
                {
                    Type = (DBType)Read.Byte(),
                    Id = Read.UInt(),
                    TypeName = ReadString(bs),
                    Name = ReadString(bs),
                    Unk1 = ReadString(bs),
                    Offset = Read.UInt(),
                    NullableIndex = Read.Int(),
                });
            }

            table.NumDataBytes = Read.Int();
            table.NumDataAndNullableBytes = Read.Int();
            table.NumNullableColumns = Read.UInt();
            table.Name = ReadString(bs);
            table.PrimaryKey = ReadString(bs);
            table.SqlWhere = ReadString(bs);
            table.SqlMore = ReadString(bs);
            table.Unk3 = Read.UInt();

            uint rowCount = Read.UInt();
            table.Rows = new List<Row>((int)rowCount);
            for (uint i = 0; i < rowCount; i++)
            {
                table.Rows.Add(ReadRow(bs, table));
            }
            return table;
        }

        private Row ReadRow(BinaryStream bs, Table table)
        {
            Row row = new Row { Id = bs.Read.UInt(), Values = new object[table.Columns.Count] };

            // One bit per nullable column, set when the cell has a value
            uint present = 0;
            if (table.NumNullableColumns > 0)
            {
                int bytes = (int)((table.NumNullableColumns + 7) / 8);
                for (int i = 0; i < bytes; i++)
                {
                    present |= (uint)bs.Read.Byte() << (i * 8);
                }
            }

            for (int i = 0; i < table.Columns.Count; i++)
            {
                Column column = table.Columns[i];
                if (column.NullableIndex == -1 || (present & (1u << column.NullableIndex)) != 0)
                {
                    row.Values[i] = ReadValue(bs, column.Type, row.Id);
                }
            }
            return row;
        }

        private object ReadValue(BinaryStream bs, DBType type, uint rowId)
        {
            BinaryReader Read = bs.Read;
            switch (type)
            {
                case DBType.Byte:
                    return Read.Byte();
                case DBType.UShort:
                    return Read.UShort();
                case DBType.UInt:
                    return Read.UInt();
                case DBType.ULong:
                    return Read.ULong();
                case DBType.SByte:
                    return Read.SByte();
                case DBType.Short:
                    return Read.Short();
                case DBType.Int:
                    return Read.Int();
                case DBType.Long:
                    return Read.Long();
                case DBType.Float:
                    return Read.Float();
                case DBType.Double:
                    return Read.Double();
                case DBType.Vector2:
                    return Read.Type<Vector2>();
                case DBType.Vector3:
                    return Read.Type<Vector3>();
                case DBType.Vector4:
                    return Read.Type<Vector4>();
                case DBType.Matrix4x4:
                    return Read.Type<Matrix4x4>();
                case DBType.Char:
                    return Read.Char();
                case DBType.Box3:
                    return Read.Type<Box3>();
                case DBType.AsciiChar:
                    return Read.Char(BinaryStream.TextEncoding.ASCII);
                case DBType.HalfMatrix4x3:
                    return Read.Type<HalfMatrix4x3>();
                case DBType.Half:
                    return Read.Half();
                case DBType.String:
                    return ScramblerVersion == 0 ? ReadString(bs) : ReadScrambledString(bs, rowId);
                case DBType.Blob:
                case DBType.ByteArray:
                    return Read.ByteArray(Read.UShort());
                case DBType.UShortArray:
                    return Read.UShortArray(Read.UShort());
                case DBType.UIntArray:
                    return Read.UIntArray(Read.UShort());
                case DBType.Vector2Array:
                    return Read.TypeList<Vector2>(Read.Byte());
                case DBType.Vector3Array:
                    return Read.TypeList<Vector3>(Read.Byte());
                case DBType.Vector4Array:
                    return Read.TypeList<Vector4>(Read.Byte());
                default:
                    throw new InvalidDataException($"Unknown column type {(byte)type}");
            }
        }

        private static byte[] ReadToNull(BinaryStream bs)
        {
            List<byte> bytes = new List<byte>(64);
            byte value;
            while ((value = bs.Read.Byte()) != 0)
            {
                bytes.Add(value);
            }
            return bytes.ToArray();
        }

        private static string ReadString(BinaryStream bs) => Encoding.UTF8.GetString(ReadToNull(bs));

        private static string ReadScrambledString(BinaryStream bs, uint rowId)
        {
            byte[] bytes = ReadToNull(bs);
            Descramble(bytes, rowId);
            return Encoding.UTF8.GetString(bytes);
        }

        // Each byte had a byte of a Mersenne Twister seeded with the row id added, carrying one when the sum wraps
        public static void Descramble(Span<byte> data, uint seed)
        {
            MersenneTwister random = new MersenneTwister(seed);
            for (int i = 0; i < data.Length; i++)
            {
                byte key = (byte)random.Next();
                data[i] = key < data[i] ? (byte)(data[i] - key) : (byte)(data[i] - key - 1);
            }
        }

        public class Table
        {
            public uint Id;
            public string Name;
            public string PrimaryKey;
            public string SqlWhere;
            public string SqlMore;
            public uint Unk3;

            public int NumDataBytes;
            public int NumDataAndNullableBytes;
            public uint NumNullableColumns;

            public List<Column> Columns = new ();
            public List<Row> Rows = new ();

            public int GetColumnIndexByName(string name) => Columns.FindIndex(column => column.Name == name);
        }

        public class Column
        {
            public DBType Type;
            public uint Id;
            public string TypeName;
            public string Name;
            public string Unk1;
            public uint Offset;

            // -1 if the column can't be null, otherwise the bit in the row's bitfield
            public int NullableIndex;
        }

        public class Row
        {
            public uint Id;

            public object[] Values;

            public object this[int column] => Values[column];
        }

        // Has Char at 17, which the .sd2 types lost, so everything after it is one higher than in StaticDB.DBType
        public enum DBType : byte
        {
            Unknown = 0,
            Byte = 1,
            UShort = 2,
            UInt = 3,
            ULong = 4,
            SByte = 5,
            Short = 6,
            Int = 7,
            Long = 8,
            Float = 9,
            Double = 10,
            String = 11,
            Vector2 = 12,
            Vector3 = 13,
            Vector4 = 14,
            Matrix4x4 = 15,
            Blob = 16,
            Char = 17,
            Box3 = 18,
            Vector2Array = 19,
            Vector3Array = 20,
            Vector4Array = 21,
            AsciiChar = 22,
            ByteArray = 23,
            UShortArray = 24,
            UIntArray = 25,
            HalfMatrix4x3 = 26,
            Half = 27,
        }
    }
}
