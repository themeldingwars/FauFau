using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

            Reader reader = new Reader(data, ScramblerVersion != 0);
            Tables = new List<Table>(tableCount);
            foreach (int offset in tableOffsets)
            {
                reader.Position = offset;
                Tables.Add(reader.Table());
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

        private sealed class Reader
        {
            private readonly byte[] data;
            private readonly bool scrambled;
            public int Position;

            public Reader(byte[] data, bool scrambled)
            {
                this.data = data;
                this.scrambled = scrambled;
            }

            private ReadOnlySpan<byte> Take(int length)
            {
                if (length < 0 || length > data.Length - Position)
                    throw new InvalidDataException($"The static database ends {length - (data.Length - Position)} bytes early");

                ReadOnlySpan<byte> span = data.AsSpan(Position, length);
                Position += length;
                return span;
            }

            private byte Byte() => Take(1)[0];
            private ushort UShort() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
            private uint UInt() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
            private int Int() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
            private float Float() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
            private float Half() => (float)BinaryPrimitives.ReadHalfLittleEndian(Take(2));

            // Read as ASCII, other bytes become '?'
            private char Char()
            {
                byte value = Byte();
                return value < 0x80 ? (char)value : '?';
            }

            private ReadOnlySpan<byte> ToNull()
            {
                int length = data.AsSpan(Position).IndexOf((byte)0);
                if (length < 0)
                    throw new InvalidDataException("A string of the static database isn't terminated");

                ReadOnlySpan<byte> text = data.AsSpan(Position, length);
                Position += length + 1;
                return text;
            }

            private string String() => Encoding.UTF8.GetString(ToNull());

            private string Cell(uint rowId)
            {
                ReadOnlySpan<byte> text = ToNull();
                if (!scrambled)
                    return Encoding.UTF8.GetString(text);

                Span<byte> plain = text.Length <= 256 ? stackalloc byte[text.Length] : new byte[text.Length];
                text.CopyTo(plain);
                Descramble(plain, rowId);
                return Encoding.UTF8.GetString(plain);
            }

            private Vector2 Vector2() => new Vector2 { x = Float(), y = Float() };
            private Vector3 Vector3() => new Vector3 { x = Float(), y = Float(), z = Float() };
            private Vector4 Vector4() => new Vector4 { x = Float(), y = Float(), z = Float(), w = Float() };
            private Half3 Half3() => new Half3 { x = Half(), y = Half(), z = Half() };

            private List<T> List<T>(int count, Func<T> read)
            {
                List<T> list = new List<T>(count);
                for (int i = 0; i < count; i++)
                    list.Add(read());

                return list;
            }

            private T[] Array<T>(int count) where T : unmanaged
            {
                return MemoryMarshal.Cast<byte, T>(Take(count * Unsafe.SizeOf<T>())).ToArray();
            }

            public Table Table()
            {
                Table table = new Table { Id = UInt() };

                byte columnCount = Byte();
                for (int i = 0; i < columnCount; i++)
                {
                    table.Columns.Add(new Column
                    {
                        Type = (DBType)Byte(),
                        Id = UInt(),
                        TypeName = String(),
                        Name = String(),
                        Unk1 = String(),
                        Offset = UInt(),
                        NullableIndex = Int(),
                    });
                }

                table.NumDataBytes = Int();
                table.NumDataAndNullableBytes = Int();
                table.NumNullableColumns = UInt();
                table.Name = String();
                table.PrimaryKey = String();
                table.SqlWhere = String();
                table.SqlMore = String();
                table.Unk3 = UInt();

                uint rowCount = UInt();
                table.Rows = new List<Row>((int)rowCount);
                DBType[] types = table.Columns.ConvertAll(column => column.Type).ToArray();
                int[] nullable = table.Columns.ConvertAll(column => column.NullableIndex).ToArray();
                int bitfieldBytes = (int)((table.NumNullableColumns + 7) / 8);
                for (uint i = 0; i < rowCount; i++)
                {
                    table.Rows.Add(Row(types, nullable, bitfieldBytes));
                }
                return table;
            }

            private Row Row(DBType[] types, int[] nullable, int bitfieldBytes)
            {
                Row row = new Row { Id = UInt(), Values = new object[types.Length] };

                // One bit per nullable column, set when the cell has a value
                uint present = 0;
                for (int i = 0; i < bitfieldBytes; i++)
                {
                    present |= (uint)Byte() << (i * 8);
                }

                for (int i = 0; i < types.Length; i++)
                {
                    if (nullable[i] == -1 || (present & (1u << nullable[i])) != 0)
                    {
                        row.Values[i] = Value(types[i], row.Id);
                    }
                }
                return row;
            }

            private object Value(DBType type, uint rowId)
            {
                switch (type)
                {
                    case DBType.Byte:
                        return StaticDB.Box(Byte());
                    case DBType.UShort:
                        return StaticDB.Box(UShort());
                    case DBType.UInt:
                        return StaticDB.Box(UInt());
                    case DBType.ULong:
                        return BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
                    case DBType.SByte:
                        return (sbyte)Byte();
                    case DBType.Short:
                        return BinaryPrimitives.ReadInt16LittleEndian(Take(2));
                    case DBType.Int:
                        return StaticDB.Box(Int());
                    case DBType.Long:
                        return BinaryPrimitives.ReadInt64LittleEndian(Take(8));
                    case DBType.Float:
                        return StaticDB.Box(Float());
                    case DBType.Double:
                        return BinaryPrimitives.ReadDoubleLittleEndian(Take(8));
                    case DBType.Vector2:
                        return Vector2();
                    case DBType.Vector3:
                        return Vector3();
                    case DBType.Vector4:
                        return Vector4();
                    case DBType.Matrix4x4:
                        return new Matrix4x4 { x = Vector4(), y = Vector4(), z = Vector4(), w = Vector4() };
                    case DBType.Char:
                    case DBType.AsciiChar:
                        return Char();
                    case DBType.Box3:
                        return new Box3 { min = Vector3(), max = Vector3() };
                    case DBType.HalfMatrix4x3:
                        return new HalfMatrix4x3 { x = Half3(), y = Half3(), z = Half3(), w = Half3() };
                    case DBType.Half:
                        return Half();
                    case DBType.String:
                        return Cell(rowId);
                    case DBType.Blob:
                    case DBType.ByteArray:
                        return Take(UShort()).ToArray();
                    case DBType.UShortArray:
                        return Array<ushort>(UShort());
                    case DBType.UIntArray:
                        return Array<uint>(UShort());
                    case DBType.Vector2Array:
                        return List(Byte(), Vector2);
                    case DBType.Vector3Array:
                        return List(Byte(), Vector3);
                    case DBType.Vector4Array:
                        return List(Byte(), Vector4);
                    default:
                        throw new InvalidDataException($"Unknown column type {(byte)type}");
                }
            }
        }

        // Each byte had a byte of a Mersenne Twister seeded with the row id added, carrying one when the sum wraps
        public static void Descramble(Span<byte> data, uint seed)
        {
            Span<uint> keys = data.Length <= 256 ? stackalloc uint[data.Length] : new uint[data.Length];
            MersenneTwister.Fill(seed, keys);
            for (int i = 0; i < data.Length; i++)
            {
                byte key = (byte)keys[i];
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
