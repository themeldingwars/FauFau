using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FauFau.Util;
using CDT = FauFau.Util.CommmonDataTypes;
using DBType = FauFau.Formats.StaticDB.DBType;
using HeaderFlags = FauFau.Formats.StaticDB.HeaderFlags;

namespace FauFau.Formats
{
    // A read-only StaticDB, the file stays in one pooled buffer instead of an object per cell
    // Rows and cells are read from that buffer on access, so spans from it are only valid until Dispose
    public sealed class StaticDBView : IDisposable, IReadOnlyList<StaticDBView.Table>
    {
        private const uint Magic = 0xDA7ABA5E;
        private const int HeaderLength = 128;
        private const int PatchNameLength = 104;
        private const int RowBlockSize = 8192;

        public string Patch { get; }
        public DateTime Timestamp { get; }
        public HeaderFlags Flags { get; }
        public uint FileVersion { get; }
        public uint MemoryVersion { get; }

        private readonly ArrayPool<byte> pool;
        private readonly Table[] tables;
        private readonly int poolOffset;
        private readonly int poolLength;
        private byte[] data;

        private StaticDBView(ReadOnlySpan<byte> file, ArrayPool<byte> pool)
        {
            if (!BitConverter.IsLittleEndian)
                throw new PlatformNotSupportedException("StaticDBView reads cells in place and needs a little endian machine");

            this.pool = pool;
            if (file.Length < HeaderLength || BinaryPrimitives.ReadUInt32LittleEndian(file) != Magic)
                throw new InvalidDataException("Not a StaticDB file, the magic is missing");

            FileVersion = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(4));
            uint payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(8));
            Flags = (HeaderFlags)BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(12));
            Timestamp = Time.DateTimeFromUnixTimestampMicroseconds((long)BinaryPrimitives.ReadUInt64LittleEndian(file.Slice(16)));
            Patch = ReadPatchName(file.Slice(24, PatchNameLength));
            if (payloadSize > file.Length - HeaderLength)
                throw new InvalidDataException($"The payload of {payloadSize} bytes is longer than the file");

            try
            {
                int length = Inflate(file.Slice(HeaderLength, (int)payloadSize));
                ReadOnlySpan<byte> inflated = data.AsSpan(0, length);
                MemoryVersion = BinaryPrimitives.ReadUInt32LittleEndian(inflated);
                if (MemoryVersion != 1000 && MemoryVersion != 1002)
                    throw new NotSupportedException($"Memory version {MemoryVersion} isn't supported, only 1000 and 1002");

                tables = ReadTables(inflated, out poolOffset);
                poolLength = length - poolOffset;
                DecryptPool();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        // Rents its buffers from the given pool, or the shared one
        public static StaticDBView Open(ReadOnlySpan<byte> file, ArrayPool<byte> pool = null)
        {
            return new StaticDBView(file, pool ?? ArrayPool<byte>.Shared);
        }

        public static StaticDBView Open(string path, ArrayPool<byte> pool = null)
        {
            pool ??= ArrayPool<byte>.Shared;
            using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
            if (stream.Length > Array.MaxLength)
                throw new InvalidDataException($"{path} is too big to be a StaticDB");

            int length = (int)stream.Length;
            byte[] file = pool.Rent(length);
            try
            {
                stream.ReadExactly(file, 0, length);
                return new StaticDBView(file.AsSpan(0, length), pool);
            }
            finally
            {
                pool.Return(file);
            }
        }

        public void Dispose()
        {
            byte[] buffer = Interlocked.Exchange(ref data, null);
            if (buffer != null)
            {
                pool.Return(buffer);
            }
        }

        internal byte[] Data => data ?? throw new ObjectDisposedException(nameof(StaticDBView));

        private static string ReadPatchName(ReadOnlySpan<byte> name)
        {
            int end = name.IndexOf((byte)0);
            return Encoding.ASCII.GetString(end >= 0 ? name.Slice(0, end) : name).TrimStart();
        }

        #region Payload
        // Deobfuscates and inflates the payload into data, returns the inflated length
        private int Inflate(ReadOnlySpan<byte> payload)
        {
            int start = FileVersion == 7 ? 2 : 10;
            if (payload.Length < start)
                throw new InvalidDataException("The payload is too short");

            byte[] deflated = pool.Rent(payload.Length);
            try
            {
                payload.CopyTo(deflated);
                if (Flags.HasFlag(HeaderFlags.ObfuscatedPool))
                {
                    MersenneTwister.Xor(Checksum.FFnv32(Patch), deflated.AsSpan(0, payload.Length));
                }

                using DeflateStream deflate = new DeflateStream(new MemoryStream(deflated, start, payload.Length - start, false), CompressionMode.Decompress);
                if (FileVersion != 7)
                {
                    // 1297 has no inflated size, the later versions have it in front of the zlib header
                    int size = BinaryPrimitives.ReadInt32LittleEndian(deflated);
                    if (size < 0)
                        throw new InvalidDataException($"Invalid inflated size {size}");

                    data = pool.Rent(size);
                    if (deflate.ReadAtLeast(data.AsSpan(0, size), size, false) != size)
                        throw new InvalidDataException($"The payload inflates to less than {size} bytes");

                    return size;
                }

                data = pool.Rent(System.Math.Max(payload.Length * 4, 4096));
                int length = 0;
                int read;
                while ((read = deflate.Read(data.AsSpan(length))) > 0)
                {
                    length += read;
                    if (length == data.Length)
                    {
                        byte[] bigger = pool.Rent(data.Length * 2);
                        data.AsSpan(0, length).CopyTo(bigger);
                        pool.Return(data);
                        data = bigger;
                    }
                }
                return length;
            }
            finally
            {
                pool.Return(deflated);
            }
        }

        private Table[] ReadTables(ReadOnlySpan<byte> inflated, out int poolStart)
        {
            const int TableInfoLength = 11;
            const int FieldInfoLength = 8;
            const int RowInfoLength = 8;

            int count = BinaryPrimitives.ReadUInt16LittleEndian(inflated.Slice(4));
            ReadOnlySpan<byte> tableInfos = inflated.Slice(6, count * TableInfoLength);

            int fieldCount = 0;
            for (int i = 0; i < count; i++)
            {
                fieldCount += BinaryPrimitives.ReadUInt16LittleEndian(tableInfos.Slice(i * TableInfoLength + 6));
            }
            ReadOnlySpan<byte> fieldInfos = inflated.Slice(6 + tableInfos.Length, fieldCount * FieldInfoLength);
            ReadOnlySpan<byte> rowInfos = inflated.Slice(6 + tableInfos.Length + fieldInfos.Length, count * RowInfoLength);
            poolStart = BinaryPrimitives.ReadInt32LittleEndian(inflated.Slice(6 + tableInfos.Length + fieldInfos.Length + rowInfos.Length));
            if (poolStart < 0 || poolStart > inflated.Length)
                throw new InvalidDataException($"The pool offset {poolStart} is outside the payload");

            Table[] result = new Table[count];
            int field = 0;
            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> tableInfo = tableInfos.Slice(i * TableInfoLength);
                uint id = BinaryPrimitives.ReadUInt32LittleEndian(tableInfo);
                int rowSize = BinaryPrimitives.ReadUInt16LittleEndian(tableInfo.Slice(4));
                int numFields = BinaryPrimitives.ReadUInt16LittleEndian(tableInfo.Slice(6));
                int usedBytes = BinaryPrimitives.ReadUInt16LittleEndian(tableInfo.Slice(8));
                int nullBytes = tableInfo[10];

                Column[] columns = new Column[numFields];
                for (int x = 0; x < numFields; x++, field++)
                {
                    ReadOnlySpan<byte> fieldInfo = fieldInfos.Slice(field * FieldInfoLength);
                    DBType type = (DBType)fieldInfo[7];
                    int offset = BinaryPrimitives.ReadUInt16LittleEndian(fieldInfo.Slice(4));
                    int nullableIndex = fieldInfo[6] == 255 ? -1 : fieldInfo[6];
                    if ((byte)type > (byte)DBType.Half || offset + StaticDB.DBTypeLength(type, MemoryVersion) > rowSize || nullableIndex >= nullBytes * 8)
                        throw new InvalidDataException($"Column {x} of table {id} doesn't fit its rows");

                    columns[x] = new Column(BinaryPrimitives.ReadUInt32LittleEndian(fieldInfo), type, offset, nullableIndex);
                }

                long rowOffset = BinaryPrimitives.ReadUInt32LittleEndian(rowInfos.Slice(i * RowInfoLength));
                long rowCount = BinaryPrimitives.ReadUInt32LittleEndian(rowInfos.Slice(i * RowInfoLength + 4));
                if (usedBytes + nullBytes > rowSize || rowOffset + rowCount * rowSize > poolStart)
                    throw new InvalidDataException($"The rows of table {id} don't fit the payload");

                result[i] = new Table(this, id, columns, (int)rowOffset, (int)rowCount, rowSize, usedBytes);
            }
            return result;
        }

        // Decrypts every pool entry in place once, so cells can hand out spans of it
        // That only works as long as no two entries share bytes, which holds for every client file we checked
        private void DecryptPool()
        {
            bool encrypted = MemoryVersion == 1000 || Flags.HasFlag(HeaderFlags.Client);
            if (!encrypted || poolLength == 0)
                return;

            int words = (poolLength >> 5) + 1;
            int[] starts = ArrayPool<int>.Shared.Rent(words);
            int[] covered = ArrayPool<int>.Shared.Rent(words);
            try
            {
                starts.AsSpan(0, words).Clear();
                covered.AsSpan(0, words).Clear();
                Parallel.ForEach(GetDataBlocks(), block => DecryptBlock(block.Table, block.Column, block.Start, block.End, starts, covered));
            }
            catch (AggregateException e) when (e.InnerExceptions.Count == 1)
            {
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            }
            finally
            {
                ArrayPool<int>.Shared.Return(starts);
                ArrayPool<int>.Shared.Return(covered);
            }
        }

        private IEnumerable<(Table Table, int Column, int Start, int End)> GetDataBlocks()
        {
            foreach (Table table in tables)
            {
                for (int x = 0; x < table.ColumnCount; x++)
                {
                    if (!StaticDB.IsDataType(table.Columns[x].Type))
                        continue;

                    for (int start = 0; start < table.Count; start += RowBlockSize)
                    {
                        yield return (table, x, start, System.Math.Min(start + RowBlockSize, table.Count));
                    }
                }
            }
        }

        private void DecryptBlock(Table table, int column, int start, int end, int[] starts, int[] covered)
        {
            Span<byte> pool = data.AsSpan(poolOffset, poolLength);
            for (int y = start; y < end; y++)
            {
                Row row = table[y];
                if (row.IsNull(column))
                    continue;

                int offset = row.Offset + table.Columns[column].Offset;
                int address;
                int length;
                uint seed;
                if (MemoryVersion == 1000)
                {
                    ulong key = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset));
                    address = (int)(uint)key;
                    length = (int)(key >> 32);
                    seed = (uint)y;
                }
                else
                {
                    uint key = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
                    ResolveKey(key, out address, out length);
                    seed = key;
                }

                if (length == 0)
                    continue;

                // A length prefix belongs to the entry too, another entry mustn't overlap it
                int first = MemoryVersion == 1002 && (seed & 1) != 0 ? address - 2 : address;
                if (address < 0 || length < 0 || (long)address + length > poolLength)
                    throw new InvalidDataException($"Pool entry {address}+{length} in table {table.Id} is outside the pool");

                // Version 1002 cells with the same key share an entry, version 1000 entries are seeded per row and can't
                if (!TryClaimBit(starts, address))
                {
                    if (MemoryVersion == 1000)
                        throw new NotSupportedException($"Pool entry {address} is shared by several rows of table {table.Id}, read the file with StaticDB instead");
                    continue;
                }
                if (!TryClaimBits(covered, first, address + length))
                    throw new NotSupportedException($"Pool entry {address}+{length} in table {table.Id} overlaps another entry, read the file with StaticDB instead");

                MersenneTwister.Xor(seed, pool.Slice(address, length));
            }
        }

        private static bool TryClaimBit(int[] bits, int index)
        {
            int mask = 1 << (index & 31);
            return (Interlocked.Or(ref bits[index >> 5], mask) & mask) == 0;
        }

        private static bool TryClaimBits(int[] bits, int start, int end)
        {
            bool clear = true;
            while (start < end)
            {
                int word = start >> 5;
                int bit = start & 31;
                int count = System.Math.Min(32 - bit, end - start);
                int mask = (int)(uint.MaxValue >> (32 - count) << bit);
                clear &= (Interlocked.Or(ref bits[word], mask) & mask) == 0;
                start += count;
            }
            return clear;
        }

        private void ResolveKey(uint key, out int address, out int length)
        {
            if ((key & 1) != 0)
            {
                address = (int)(key >> 1);
                if (address > poolLength - 2)
                    throw new InvalidDataException($"Pool key {key:X8} is outside the pool");

                length = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(poolOffset + address));
                address += 2;
            }
            else
            {
                address = (int)((key >> 1) & 0x7FFFFF);
                length = (int)(key >> 24);
            }
        }

        // The decrypted pool entry of a data cell
        internal ReadOnlySpan<byte> GetEntry(byte[] buffer, int offset)
        {
            int address;
            int length;
            if (MemoryVersion == 1000)
            {
                ulong key = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(offset));
                address = (int)(uint)key;
                length = (int)(key >> 32);
            }
            else
            {
                ResolveKey(BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset)), out address, out length);
            }
            return length == 0 ? ReadOnlySpan<byte>.Empty : buffer.AsSpan(poolOffset, poolLength).Slice(address, length);
        }
        #endregion

        #region Tables
        public int Count => tables.Length;

        public Table this[int index] => tables[index];

        public int GetIndexByName(string name)
        {
            return GetIndexById(Checksum.FFnv32(name));
        }

        public int GetIndexById(uint id)
        {
            for (int i = 0; i < tables.Length; i++)
            {
                if (tables[i].Id == id)
                    return i;
            }
            return -1;
        }

        public bool TryGetTable(uint id, out Table table)
        {
            int index = GetIndexById(id);
            table = index >= 0 ? tables[index] : null;
            return table != null;
        }

        public Table GetTableByName(string name)
        {
            int index = GetIndexByName(name);
            return index >= 0 ? tables[index] : throw new KeyNotFoundException($"No table named {name}");
        }

        public Table GetTableById(uint id)
        {
            int index = GetIndexById(id);
            return index >= 0 ? tables[index] : throw new KeyNotFoundException($"No table with id {id}");
        }

        public IEnumerator<Table> GetEnumerator() => ((IEnumerable<Table>)tables).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => tables.GetEnumerator();
        #endregion

        #region Subclasses
        public readonly struct Column
        {
            public uint Id { get; }
            public DBType Type { get; }

            // Byte offset in the row
            public int Offset { get; }

            // Bit in the null bits after the used row bytes, -1 if the column can't be null
            public int NullableIndex { get; }

            public bool IsNullable => NullableIndex >= 0;

            internal Column(uint id, DBType type, int offset, int nullableIndex)
            {
                Id = id;
                Type = type;
                Offset = offset;
                NullableIndex = nullableIndex;
            }
        }

        public sealed class Table : IReadOnlyList<Row>
        {
            public uint Id { get; }
            public StaticDBView View { get; }
            public int Count { get; }
            public int ColumnCount => columns.Length;
            public ReadOnlySpan<Column> Columns => columns;

            internal readonly int RowSize;
            internal readonly int NullOffset;
            private readonly Column[] columns;
            private readonly int rowOffset;

            internal Table(StaticDBView view, uint id, Column[] columns, int rowOffset, int count, int rowSize, int nullOffset)
            {
                View = view;
                Id = id;
                this.columns = columns;
                this.rowOffset = rowOffset;
                Count = count;
                RowSize = rowSize;
                NullOffset = nullOffset;
            }

            public Row this[int index]
            {
                get
                {
                    if ((uint)index >= (uint)Count)
                        throw new ArgumentOutOfRangeException(nameof(index));

                    return new Row(this, index, rowOffset + index * RowSize);
                }
            }

            internal Column GetColumn(int index) => columns[index];

            public int GetColumnIndexByName(string name)
            {
                return GetColumnIndexById(Checksum.FFnv32(name));
            }

            public int GetColumnIndexById(uint id)
            {
                for (int i = 0; i < columns.Length; i++)
                {
                    if (columns[i].Id == id)
                        return i;
                }
                return -1;
            }

            public Enumerator GetEnumerator() => new Enumerator(this);

            IEnumerator<Row> IEnumerable<Row>.GetEnumerator() => GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public struct Enumerator : IEnumerator<Row>
            {
                private readonly Table table;
                private int index;

                internal Enumerator(Table table)
                {
                    this.table = table;
                    index = -1;
                }

                public Row Current => table[index];

                object IEnumerator.Current => Current;

                public bool MoveNext() => ++index < table.Count;

                public void Reset() => index = -1;

                public void Dispose()
                {
                }
            }
        }

        public readonly struct Row
        {
            public Table Table { get; }
            public int Index { get; }

            // Byte offset of the row in the payload
            internal int Offset { get; }

            internal Row(Table table, int index, int offset)
            {
                Table = table;
                Index = index;
                Offset = offset;
            }

            public bool IsNull(int column)
            {
                int bit = Table.GetColumn(column).NullableIndex;
                if (bit < 0)
                    return false;

                return ((Table.View.Data[Offset + Table.NullOffset + (bit >> 3)] >> (bit & 7)) & 1) != 0;
            }

            // The bytes of a fixed size cell, checked against the column type
            private ReadOnlySpan<byte> Cell(int column, DBType type)
            {
                Column info = Table.GetColumn(column);
                if (info.Type != type)
                    throw new InvalidCastException($"Column {column} of table {Table.Id} is {info.Type}, not {type}");

                return Table.View.Data.AsSpan(Offset + info.Offset);
            }

            public byte GetByte(int column) => Cell(column, DBType.Byte)[0];
            public sbyte GetSByte(int column) => (sbyte)Cell(column, DBType.SByte)[0];
            public ushort GetUShort(int column) => BinaryPrimitives.ReadUInt16LittleEndian(Cell(column, DBType.UShort));
            public short GetShort(int column) => BinaryPrimitives.ReadInt16LittleEndian(Cell(column, DBType.Short));
            public uint GetUInt(int column) => BinaryPrimitives.ReadUInt32LittleEndian(Cell(column, DBType.UInt));
            public int GetInt(int column) => BinaryPrimitives.ReadInt32LittleEndian(Cell(column, DBType.Int));
            public ulong GetULong(int column) => BinaryPrimitives.ReadUInt64LittleEndian(Cell(column, DBType.ULong));
            public long GetLong(int column) => BinaryPrimitives.ReadInt64LittleEndian(Cell(column, DBType.Long));
            public float GetFloat(int column) => BinaryPrimitives.ReadSingleLittleEndian(Cell(column, DBType.Float));
            public double GetDouble(int column) => BinaryPrimitives.ReadDoubleLittleEndian(Cell(column, DBType.Double));
            public float GetHalf(int column) => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(Cell(column, DBType.Half)));

            // Like Bitter, bytes outside ASCII read as '?'
            public char GetAsciiChar(int column)
            {
                byte value = Cell(column, DBType.AsciiChar)[0];
                return value < 0x80 ? (char)value : '?';
            }

            public Vector2 GetVector2(int column) => MemoryMarshal.Read<Vector2>(Cell(column, DBType.Vector2));
            public Vector3 GetVector3(int column) => MemoryMarshal.Read<Vector3>(Cell(column, DBType.Vector3));
            public Vector4 GetVector4(int column) => MemoryMarshal.Read<Vector4>(Cell(column, DBType.Vector4));

            // Rows x, y, z, w as M1*, M2*, M3*, M4*
            public Matrix4x4 GetMatrix4x4(int column) => MemoryMarshal.Read<Matrix4x4>(Cell(column, DBType.Matrix4x4));

            public (Vector3 Min, Vector3 Max) GetBox3(int column)
            {
                ReadOnlySpan<byte> cell = Cell(column, DBType.Box3);
                return (MemoryMarshal.Read<Vector3>(cell), MemoryMarshal.Read<Vector3>(cell.Slice(12)));
            }

            // Four rows of three halves, as an affine matrix with the last column 0, 0, 0, 1
            public Matrix4x4 GetHalfMatrix4x3(int column)
            {
                ReadOnlySpan<byte> cell = Cell(column, DBType.HalfMatrix4x3);
                Span<float> values = stackalloc float[12];
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(cell.Slice(i * 2)));
                }
                return new Matrix4x4(
                    values[0], values[1], values[2], 0,
                    values[3], values[4], values[5], 0,
                    values[6], values[7], values[8], 0,
                    values[9], values[10], values[11], 1);
            }

            // The pool entry of a data column (String, Blob and the arrays), empty if the cell is null
            public ReadOnlySpan<byte> GetBytes(int column)
            {
                Column info = Table.GetColumn(column);
                if (!StaticDB.IsDataType(info.Type))
                    throw new InvalidCastException($"Column {column} of table {Table.Id} is {info.Type}, not a pool type");

                if (IsNull(column))
                    return ReadOnlySpan<byte>.Empty;

                return Table.View.GetEntry(Table.View.Data, Offset + info.Offset);
            }

            private ReadOnlySpan<byte> Entry(int column, DBType type)
            {
                DBType actual = Table.GetColumn(column).Type;
                if (actual != type)
                    throw new InvalidCastException($"Column {column} of table {Table.Id} is {actual}, not {type}");

                return GetBytes(column);
            }

            // Null for null and empty cells, like StaticDB
            public string GetString(int column)
            {
                ReadOnlySpan<byte> entry = Entry(column, DBType.String);
                return entry.IsEmpty ? null : StaticDB.DecodeString(entry);
            }

            public ReadOnlySpan<ushort> GetUShorts(int column) => MemoryMarshal.Cast<byte, ushort>(Entry(column, DBType.UShortArray));
            public ReadOnlySpan<uint> GetUInts(int column) => MemoryMarshal.Cast<byte, uint>(Entry(column, DBType.UIntArray));
            public ReadOnlySpan<Vector2> GetVector2s(int column) => MemoryMarshal.Cast<byte, Vector2>(Entry(column, DBType.Vector2Array));
            public ReadOnlySpan<Vector3> GetVector3s(int column) => MemoryMarshal.Cast<byte, Vector3>(Entry(column, DBType.Vector3Array));
            public ReadOnlySpan<Vector4> GetVector4s(int column) => MemoryMarshal.Cast<byte, Vector4>(Entry(column, DBType.Vector4Array));

            // The cell as the object StaticDB would hold, allocates like StaticDB does
            public object GetValue(int column)
            {
                if (IsNull(column))
                    return null;

                DBType type = Table.GetColumn(column).Type;
                switch (type)
                {
                    case DBType.Byte: return GetByte(column);
                    case DBType.UShort: return GetUShort(column);
                    case DBType.UInt: return GetUInt(column);
                    case DBType.ULong: return GetULong(column);
                    case DBType.SByte: return GetSByte(column);
                    case DBType.Short: return GetShort(column);
                    case DBType.Int: return GetInt(column);
                    case DBType.Long: return GetLong(column);
                    case DBType.Float: return GetFloat(column);
                    case DBType.Double: return GetDouble(column);
                    case DBType.Half: return GetHalf(column);
                    case DBType.AsciiChar: return GetAsciiChar(column);
                    case DBType.Vector2: return ToCommon(GetVector2(column));
                    case DBType.Vector3: return ToCommon(GetVector3(column));
                    case DBType.Vector4: return ToCommon(GetVector4(column));
                    case DBType.Matrix4x4:
                        Matrix4x4 m = GetMatrix4x4(column);
                        return new CDT.Matrix4x4
                        {
                            x = new CDT.Vector4 { x = m.M11, y = m.M12, z = m.M13, w = m.M14 },
                            y = new CDT.Vector4 { x = m.M21, y = m.M22, z = m.M23, w = m.M24 },
                            z = new CDT.Vector4 { x = m.M31, y = m.M32, z = m.M33, w = m.M34 },
                            w = new CDT.Vector4 { x = m.M41, y = m.M42, z = m.M43, w = m.M44 },
                        };
                    case DBType.Box3:
                        (Vector3 min, Vector3 max) = GetBox3(column);
                        return new CDT.Box3 { min = ToCommon(min), max = ToCommon(max) };
                    case DBType.HalfMatrix4x3:
                        Matrix4x4 h = GetHalfMatrix4x3(column);
                        return new CDT.HalfMatrix4x3
                        {
                            x = new CDT.Half3 { x = h.M11, y = h.M12, z = h.M13 },
                            y = new CDT.Half3 { x = h.M21, y = h.M22, z = h.M23 },
                            z = new CDT.Half3 { x = h.M31, y = h.M32, z = h.M33 },
                            w = new CDT.Half3 { x = h.M41, y = h.M42, z = h.M43 },
                        };
                    case DBType.String:
                        return GetString(column);
                    case DBType.Blob:
                    case DBType.ByteArray:
                        ReadOnlySpan<byte> bytes = GetBytes(column);
                        return bytes.IsEmpty ? null : new List<byte>(bytes.ToArray());
                    // An entry shorter than one element, like the single byte of some empty lists, is an empty list
                    case DBType.UShortArray:
                        return GetBytes(column).IsEmpty ? null : new List<ushort>(GetUShorts(column).ToArray());
                    case DBType.UIntArray:
                        return GetBytes(column).IsEmpty ? null : new List<uint>(GetUInts(column).ToArray());
                    case DBType.Vector2Array:
                        return GetBytes(column).IsEmpty ? null : ToList(GetVector2s(column), ToCommon);
                    case DBType.Vector3Array:
                        return GetBytes(column).IsEmpty ? null : ToList(GetVector3s(column), ToCommon);
                    case DBType.Vector4Array:
                        return GetBytes(column).IsEmpty ? null : ToList(GetVector4s(column), ToCommon);
                    default:
                        return null;
                }
            }

            private static CDT.Vector2 ToCommon(Vector2 v) => new CDT.Vector2 { x = v.X, y = v.Y };
            private static CDT.Vector3 ToCommon(Vector3 v) => new CDT.Vector3 { x = v.X, y = v.Y, z = v.Z };
            private static CDT.Vector4 ToCommon(Vector4 v) => new CDT.Vector4 { x = v.X, y = v.Y, z = v.Z, w = v.W };

            private static List<TResult> ToList<T, TResult>(ReadOnlySpan<T> values, Func<T, TResult> convert)
            {
                List<TResult> list = new List<TResult>(values.Length);
                foreach (T value in values)
                {
                    list.Add(convert(value));
                }
                return list;
            }
        }
        #endregion
    }
}
