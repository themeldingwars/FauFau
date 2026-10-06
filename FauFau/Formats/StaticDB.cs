using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bitter;
using FauFau.Util;
using FauFau.Util.CommmonDataTypes;
using static Bitter.BinaryUtil;
using static FauFau.Util.Common;

namespace FauFau.Formats
{
    public class StaticDB : BinaryWrapper, IEnumerable<StaticDB.Table>
    {
        public string Patch;
        public DateTime Timestamp;
        public HeaderFlags Flags;
        public List<Table> Tables;

        private uint fileVersion = 12;
        private uint memoryVersion = 1002;
        private int numThreads = Environment.ProcessorCount;

        private Dictionary<(ulong, int), byte[]> uniqueEntries1000 = new ();
        private Dictionary<uint, byte[]> uniqueEntries1002 = new ();


        #region File read & write
        public override void Read(BinaryStream bs)
        {
            // Read Header
            HeaderInfo headerInfo = bs.Read.Type<HeaderInfo>();

            this.Patch = headerInfo.patchName;
            this.Timestamp = new DateTime();
            this.Timestamp = Util.Time.DateTimeFromUnixTimestampMicroseconds((long)headerInfo.timestamp);
            this.Flags = headerInfo.flags;
            this.fileVersion = headerInfo.version;

            // Deobfuscate
            byte[] data = bs.Read.ByteArray((int)headerInfo.payloadSize);
            if (Flags.HasFlag(HeaderFlags.ObfuscatedPool)) {
                MTXor(Checksum.FFnv32(headerInfo.patchName), ref data);
            }

            // Decompress
            BinaryStream ibs;
            byte[] inflated;
            if (fileVersion == 7) // 1297 - No pre-payload data, unknown inflated size
            {
                // ushort 0x78 0x01 zlib deflate low/no compression
                var payloadStream = new BinaryStream(new MemoryStream(data[2..]));
                var inflatedStream = new MemoryStream();
                ibs = new BinaryStream(inflatedStream);
                InflateUnknownTargetSize(payloadStream, ibs, SharpCompress.Compressors.Deflate.CompressionLevel.BestSpeed);
                inflated = inflatedStream.ToArray();
            }
            else
            {
                // read compression header
                // uint inflated size
                // uint padding
                // ushort 0x78 0x01 zlib deflate low/no compression
                uint inflatedSize = UIntFromBufferLE(data);
                inflated = new byte[inflatedSize];
                Inflate(data, ref inflated, SharpCompress.Compressors.Deflate.CompressionLevel.BestSpeed, (int)inflatedSize, 10);
                ibs = new BinaryStream(new MemoryStream(inflated));
            }

            // Cleanup memory, deobfuscated data is no longer needed
            data = null;

            // Read memory header
            ibs.ByteOffset = 0;
            this.memoryVersion = ibs.Read.UInt();
            ushort indexLength = ibs.Read.UShort();

            // Read table info
            TableInfo[] tableInfos = new TableInfo[indexLength];
            for (ushort i = 0; i < indexLength; i++)
            {
                tableInfos[i] = ibs.Read.Type<TableInfo>();
            }

            // Read field info
            FieldInfo[][] fieldInfos = new FieldInfo[indexLength][];
            for (int i = 0; i < indexLength; i++)
            {
                fieldInfos[i] = new FieldInfo[tableInfos[i].numFields];
                for (int x = 0; x < tableInfos[i].numFields; x++)
                {
                    fieldInfos[i][x] = ibs.Read.Type<FieldInfo>();
                }
            }

            // Read row info
            RowInfo[] rowInfos = new RowInfo[indexLength];
            for (ushort i = 0; i < indexLength; i++)
            {
                rowInfos[i] = ibs.Read.Type<RowInfo>();
            }

            // Build tables (No reading)
            Tables = new List<Table>(indexLength);
            for (ushort i = 0; i < indexLength; i++)
            {
                TableInfo tableInfo = tableInfos[i];
                FieldInfo[] fieldInfo = fieldInfos[i];
                RowInfo rowInfo = rowInfos[i];

                // setup table
                Table table = new Table();
                table.Id = tableInfo.id;

                // add fields
                table.Columns = new List<Column>(tableInfo.numFields);
                int currentWidth = 0;
                for (int x = 0; x < tableInfo.numFields; x++)
                {
                    Column field = new Column();
                    field.Id = fieldInfos[i][x].id;
                    field.Type = (DBType)fieldInfos[i][x].type;

                    // fix removed fields? (weird padding some places)
                    if (fieldInfo[x].start != currentWidth)
                    {
                        int padding = fieldInfo[x].start - currentWidth;
                        field.Padding = padding;
                        currentWidth += padding;
                    }
                    currentWidth += DBTypeLength((DBType)fieldInfo[x].type);


                    table.Columns.Add(field);
                }

                // if any, add nullable fields
                if (tableInfo.nullableBitfields != 0)
                {
                    int count = 0;
                    for (int x = 0; x < tableInfo.numFields; x++)
                    {
                        if (fieldInfos[i][x].nullableIndex != 255)
                        {
                            count++;
                        }
                    }

                    Column[] nullableColumns = new Column[count];
                    for (int x = 0; x < tableInfo.numFields; x++)
                    {
                        if (fieldInfos[i][x].nullableIndex != 255)
                        {
                            nullableColumns[fieldInfos[i][x].nullableIndex] = table.Columns[x];
                        }
                    }
                    table.NullableColumn = new List<Column>(nullableColumns);
                }
                else
                {
                    table.NullableColumn = new List<Column>();
                }

                Tables.Add(table);
            }

            // Parse pool offset after rowInfo
            uint poolOffset = 0;
            poolOffset = ibs.Read.UInt();

            // Read rows in blocks, so the big tables are spread over all threads too
            Row[][] tableRows = new Row[indexLength][];
            int[][] nullableIndices = new int[indexLength][];
            for (int i = 0; i < indexLength; i++)
            {
                Table table = Tables[i];
                tableRows[i] = new Row[rowInfos[i].rowCount];
                nullableIndices[i] = new int[table.NullableColumn.Count];
                for (int n = 0; n < nullableIndices[i].Length; n++)
                {
                    nullableIndices[i][n] = table.Columns.IndexOf(table.NullableColumn[n]);
                }
            }

            Parallel.ForEach(GetRowBlocks(tableRows), () => new BinaryStream(new MemoryStream(inflated)), (block, _, fallback) =>
            {
                TableInfo tableInfo = tableInfos[block.Table];
                FieldInfo[] fieldInfo = fieldInfos[block.Table];
                RowInfo rowInfo = rowInfos[block.Table];
                int[] nullable = nullableIndices[block.Table];

                for (int y = block.Start; y < block.End; y++)
                {
                    int rowStart = (int)(rowInfo.rowOffset + tableInfo.numBytes * y);
                    Row row = new Row(tableInfo.numFields);
                    for (int z = 0; z < tableInfo.numFields; z++)
                    {
                        // just read the basic type now, unpack & decrypt later to reduce seeking
                        row.Fields.Add(ReadCell(inflated, rowStart + fieldInfo[z].start, (DBType)fieldInfo[z].type, fallback));
                    }

                    // The null bits follow the used bytes, which can end after the last field
                    int nullStart = rowStart + tableInfo.numUsedBytes;
                    for (int n = 0; n < nullable.Length; n++)
                    {
                        if (((inflated[nullStart + (n >> 3)] >> (n & 7)) & 1) != 0)
                        {
                            row[nullable[n]] = null;
                        }
                    }
                    tableRows[block.Table][y] = row;
                }
                return fallback;
            }, fallback => fallback.Dispose());

            for (int i = 0; i < indexLength; i++)
            {
                Tables[i].Rows = new List<Row>(tableRows[i]);
            }

            inflated = null;

            // Seek to pool offset
            ibs.ByteOffset = poolOffset;

            // Copy the data to a new stream 
            int dataLength = (int)(ibs.Length - ibs.ByteOffset);
            byte[] dataBlock = ibs.Read.ByteArray(dataLength);

            // Cleanup
            ibs.Dispose();
            ibs = null;

            // Parse the pool data and fill in the tables
            if (memoryVersion == 1000) {
                uniqueEntries1000 = new Dictionary<(ulong, int), byte[]>();
                ParsePoolVersion1000(dataBlock);
            } else {
                uniqueEntries1002 = new Dictionary<uint, byte[]>();
                ParsePoolVersion1002(dataBlock);
            }

            // Cleanup :>
            headerInfo = null;
            tableInfos = null;
            fieldInfos = null;
            rowInfos = null;
        }

        private void ParsePoolVersion1000(byte[] dataBlock)
        {
            // The row is part of the seed, so the same key can decode differently per row
            HashSet<(ulong, int)> uniqueKeys = new HashSet<(ulong, int)>();
            foreach (Table table in Tables)
            {
                for (int x = 0; x < table.Columns.Count; x++)
                {
                    if (!IsDataType(table.Columns[x].Type))
                        continue;

                    for (int y = 0; y < table.Rows.Count; y++)
                    {
                        if (table.Rows[y][x] is ulong key)
                            uniqueKeys.Add((key, y));
                    }
                }
            }

            (ulong, int)[] keys = new (ulong, int)[uniqueKeys.Count];
            uniqueKeys.CopyTo(keys);
            byte[][] entries = new byte[keys.Length][];
            Parallel.For(0, keys.Length, i => entries[i] = GetDataEntry(dataBlock, keys[i].Item1, keys[i].Item2));

            uniqueEntries1000 = new Dictionary<(ulong, int), byte[]>(keys.Length);
            for (int i = 0; i < keys.Length; i++)
            {
                uniqueEntries1000.Add(keys[i], entries[i]);
            }

            // Replace all pool type cell values with the unpacked data
            Parallel.ForEach(GetRowBlocks(), block =>
            {
                Table table = block.Table;
                for (int x = 0; x < table.Columns.Count; x++)
                {
                    DBType type = table.Columns[x].Type;
                    if (!IsDataType(type))
                        continue;

                    for (int y = block.Start; y < block.End; y++)
                    {
                        object obj = null;
                        if (table.Rows[y][x] is ulong key && uniqueEntries1000.TryGetValue((key, y), out byte[] d) && d != null)
                        {
                            obj = BytesToDBType(type, d);
                        }
                        table.Rows[y][x] = obj;
                    }
                }
            });
        }

        private void ParsePoolVersion1002(byte[] dataBlock)
        {
            HashSet<uint> uniqueKeys = new HashSet<uint>();
            foreach (Table table in Tables)
            {
                for (int x = 0; x < table.Columns.Count; x++)
                {
                    if (!IsDataType(table.Columns[x].Type))
                        continue;

                    for (int y = 0; y < table.Rows.Count; y++)
                    {
                        if (table.Rows[y][x] is uint key)
                            uniqueKeys.Add(key);
                    }
                }
            }

            uint[] keys = new uint[uniqueKeys.Count];
            uniqueKeys.CopyTo(keys);
            byte[][] entries = new byte[keys.Length][];
            Parallel.For(0, keys.Length, i => entries[i] = GetDataEntry(dataBlock, keys[i]));

            uniqueEntries1002 = new Dictionary<uint, byte[]>(keys.Length);
            for (int i = 0; i < keys.Length; i++)
            {
                uniqueEntries1002.Add(keys[i], entries[i]);
            }

            // Strings can't change, so cells with the same key share one, the other types are lists and get their own
            ConcurrentDictionary<uint, string> strings = new ConcurrentDictionary<uint, string>();

            // Replace all pool type cell values with the unpacked data
            Parallel.ForEach(GetRowBlocks(), block =>
            {
                Table table = block.Table;
                for (int x = 0; x < table.Columns.Count; x++)
                {
                    DBType type = table.Columns[x].Type;
                    if (!IsDataType(type))
                        continue;

                    for (int y = block.Start; y < block.End; y++)
                    {
                        object obj = null;
                        if (table.Rows[y][x] is uint key && uniqueEntries1002.TryGetValue(key, out byte[] d) && d != null)
                        {
                            obj = type == DBType.String ? strings.GetOrAdd(key, _ => Encoding.UTF8.GetString(d)) : BytesToDBType(type, d);
                        }
                        table.Rows[y][x] = obj;
                    }
                }
            });
        }

        // Boxed values can't change, so cells with the same small value share one box instead of allocating millions
        private const int BoxCacheSize = 1024;
        private static readonly object[] boxedBytes = CreateBoxes(256, i => (byte)i);
        private static readonly object[] boxedUShorts = CreateBoxes(BoxCacheSize, i => (ushort)i);
        private static readonly object[] boxedUInts = CreateBoxes(BoxCacheSize, i => (uint)i);
        private static readonly object[] boxedInts = CreateBoxes(BoxCacheSize, i => i);
        private static readonly object boxedZeroFloat = 0f;
        private static readonly object boxedOneFloat = 1f;

        private static object[] CreateBoxes(int count, Func<int, object> box)
        {
            object[] boxes = new object[count];
            for (int i = 0; i < count; i++)
            {
                boxes[i] = box(i);
            }
            return boxes;
        }

        private static object Box(uint value) => value < BoxCacheSize ? boxedUInts[value] : value;
        private static object Box(ushort value) => value < BoxCacheSize ? boxedUShorts[value] : value;
        private static object Box(int value) => (uint)value < BoxCacheSize ? boxedInts[value] : value;

        private static object Box(float value)
        {
            // Compare the bits, so -0 keeps its sign
            int bits = BitConverter.SingleToInt32Bits(value);
            return bits == 0 ? boxedZeroFloat : bits == 0x3F800000 ? boxedOneFloat : value;
        }

        private const int RowBlockSize = 8192;

        private static IEnumerable<(int Table, int Start, int End)> GetRowBlocks(Row[][] tableRows)
        {
            for (int i = 0; i < tableRows.Length; i++)
            {
                for (int start = 0; start < tableRows[i].Length; start += RowBlockSize)
                {
                    yield return (i, start, System.Math.Min(start + RowBlockSize, tableRows[i].Length));
                }
            }
        }

        private IEnumerable<(Table Table, int Start, int End)> GetRowBlocks()
        {
            foreach (Table table in Tables)
            {
                for (int start = 0; start < table.Rows.Count; start += RowBlockSize)
                {
                    yield return (table, start, System.Math.Min(start + RowBlockSize, table.Rows.Count));
                }
            }
        }

        // Reads the common types straight from the payload, the rest through Bitter like ReadDBType
        private object ReadCell(byte[] data, int offset, DBType type, BinaryStream fallback)
        {
            ReadOnlySpan<byte> span = data.AsSpan(offset);
            switch (type)
            {
                case DBType.Byte:
                    return boxedBytes[data[offset]];
                case DBType.UShort:
                    return Box(BinaryPrimitives.ReadUInt16LittleEndian(span));
                case DBType.UInt:
                    return Box(BinaryPrimitives.ReadUInt32LittleEndian(span));
                case DBType.ULong:
                    return BinaryPrimitives.ReadUInt64LittleEndian(span);
                case DBType.SByte:
                    return (sbyte)data[offset];
                case DBType.Short:
                    return BinaryPrimitives.ReadInt16LittleEndian(span);
                case DBType.Int:
                    return Box(BinaryPrimitives.ReadInt32LittleEndian(span));
                case DBType.Long:
                    return BinaryPrimitives.ReadInt64LittleEndian(span);
                case DBType.Float:
                    return Box(BinaryPrimitives.ReadSingleLittleEndian(span));
                case DBType.Double:
                    return BinaryPrimitives.ReadDoubleLittleEndian(span);
                case DBType.Vector2:
                    return new Vector2 { x = BinaryPrimitives.ReadSingleLittleEndian(span), y = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(4)) };
                case DBType.Vector3:
                    return new Vector3 { x = BinaryPrimitives.ReadSingleLittleEndian(span), y = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(4)), z = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(8)) };
                case DBType.Vector4:
                    return new Vector4 { x = BinaryPrimitives.ReadSingleLittleEndian(span), y = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(4)), z = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(8)), w = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(12)) };
                case DBType.String:
                case DBType.Blob:
                case DBType.ByteArray:
                case DBType.UShortArray:
                case DBType.UIntArray:
                case DBType.Vector2Array:
                case DBType.Vector3Array:
                case DBType.Vector4Array:
                    return memoryVersion == 1000 ? (object)BinaryPrimitives.ReadUInt64LittleEndian(span) : BinaryPrimitives.ReadUInt32LittleEndian(span);
                default:
                    fallback.ByteOffset = offset;
                    return ReadDBType(fallback, type);
            }
        }

        public override void Write(BinaryStream bs)
        {
            if (fileVersion == 7 || memoryVersion != 1002)
                throw new NotSupportedException($"Writing file version {fileVersion} with memory version {memoryVersion} is not supported, only memory version 1002");

            // Doing this for debugging purposes
            MemoryStream memory_header     = new MemoryStream();
            MemoryStream memory_info       = new MemoryStream();
            MemoryStream memory_rows       = new MemoryStream();
            MemoryStream memory_data       = new MemoryStream();
            MemoryStream memory_inflated   = new MemoryStream();
            MemoryStream memory_deflated   = new MemoryStream();
            MemoryStream memory_obfuscated = new MemoryStream();
            BinaryStream header_bs         = new BinaryStream(memory_header);
            BinaryStream info_bs           = new BinaryStream(memory_info);
            BinaryStream rows_bs           = new BinaryStream(memory_rows);
            BinaryStream data_bs           = new BinaryStream(memory_data);
            BinaryStream inflated_bs       = new BinaryStream(memory_inflated);
            BinaryStream deflated_bs       = new BinaryStream(memory_deflated);
            BinaryStream obfuscated_bs     = new BinaryStream(memory_obfuscated);

            // === Regenerate info structures ==
            int numberOfTables = Tables.Count;
            TableInfo[] tableInfos = new TableInfo[numberOfTables];
            FieldInfo[][] fieldInfos = new FieldInfo[numberOfTables][];
            RowInfo[] rowInfos = new RowInfo[numberOfTables];
            for (ushort i = 0; i < numberOfTables; i++)
            {
                Table table = Tables[i];

                // FieldInfo
                FieldInfo[] gen_fieldInfo = new FieldInfo[table.Columns.Count];
                int currentWidth = 0;
                for (int x = 0; x < table.Columns.Count; x++) {
                    gen_fieldInfo[x] = new FieldInfo();
                    Column column = table.Columns[x];
                    gen_fieldInfo[x].id = column.Id;
                    gen_fieldInfo[x].type = (byte) column.Type;

                    // nullableIndex
                    if (table.IsColumnNullable(column)) {
                        gen_fieldInfo[x].nullableIndex = (byte) table.NullableColumn.IndexOf(column);
                    }
                    else {
                        gen_fieldInfo[x].nullableIndex = 255;
                    }

                    // start
                    if (column.Padding > 0) {
                        currentWidth += column.Padding;
                    }
                    gen_fieldInfo[x].start = (ushort) currentWidth;
                    currentWidth += DBTypeLength(column.Type);
                }

                // TableInfo
                TableInfo   gen_tableInfo = new TableInfo();
                gen_tableInfo.id = table.Id;
                gen_tableInfo.numFields = (ushort) table.Columns.Count;
                gen_tableInfo.nullableBitfields = table.NullableColumn.Count > 0 ? (byte) System.Math.Ceiling((double)table.NullableColumn.Count/8) : (byte) 0;
                gen_tableInfo.numUsedBytes = (ushort) currentWidth;
                int mustBeDivisableBy4 = 4;
                gen_tableInfo.numBytes = (ushort) FindClosestLargerNumber(gen_tableInfo.numUsedBytes + gen_tableInfo.nullableBitfields, mustBeDivisableBy4);

                // RowInfo
                RowInfo gen_rowInfo = new RowInfo();
                gen_rowInfo.rowCount = (uint) table.Rows.Count;
                gen_rowInfo.rowOffset = 0; // This is updated later, when we generate the data.

                // Assign
                tableInfos[i] = gen_tableInfo;
                fieldInfos[i] = gen_fieldInfo;
                rowInfos[i] = gen_rowInfo;
            }

            // === Data ===
            Dictionary<byte[], uint> uniqueDataObjectKeys = new Dictionary<byte[], uint>(new ByteArrayComparer());
            Dictionary<(int, int, int), uint> dataKeys = new Dictionary<(int, int, int), uint>();

            for (int tn = 0; tn < Tables.Count; tn++)
            {
                Table table = Tables[tn];
                for (int fn = 0; fn < table.Columns.Count; fn++)
                {
                    Column field = table.Columns[fn];
                    if (IsDataType(field.Type)) {
                        for (int rn = 0; rn < table.Rows.Count; rn++)
                        {
                            Row row = table[rn];
                            object obj = row[fn];
                            if (obj != null)
                            {
                                byte[] dat = DBTypeToBytes(field.Type, obj);
                                if (dat != null && dat.Length != 0) {
                                    if (!uniqueDataObjectKeys.TryGetValue(dat, out uint key)) {
                                        key = GenerateDataEntryKey((uint)data_bs.ByteOffset, (uint)dat.Length);
                                        if ((key & 1) > 0) {
                                            data_bs.Write.UShort((ushort)dat.Length);
                                        }
                                        byte[] twisted = Flags.HasFlag(HeaderFlags.Client) ? TwistDataEntry(key, (byte[])dat.Clone()) : dat;
                                        data_bs.Write.ByteArray(twisted);

                                        uniqueDataObjectKeys.Add(dat, key);
                                    }

                                    dataKeys[(tn, rn, fn)] = key;
                                }
                            }
                        }
                    }
                }
            }


            // === Rows ===
            // Since it contains data references, this should go after data

            // Working code
            for (ushort i = 0; i < Tables.Count; i++)
            {

                TableInfo tableInfo = tableInfos[i];
                FieldInfo[] fieldInfo = fieldInfos[i];
                RowInfo rowInfo = rowInfos[i];

                List<Row> Rows = Tables[i].Rows;

                rowInfo.rowOffset = (uint) rows_bs.ByteOffset; // Set offset relative to written data, later amend with the external offset.

                for (int y = 0; y < rowInfo.rowCount; y++)
                {

                    // Works as long as we write something :P
                    rows_bs.ByteOffset = rowInfo.rowOffset + (tableInfo.numBytes * y) + fieldInfo[0].start;

                    // Regular columns
                    for (int z = 0; z < tableInfo.numFields; z++)
                    {
                        if (Tables[i].Columns[z].Padding != 0)
                        {
                            uint new_offset = (uint) rows_bs.ByteOffset + (uint) Tables[i].Columns[z].Padding;
                            uint byte_len = (new_offset - (uint) rows_bs.ByteOffset);
                            rows_bs.Write.ByteArray(new byte[byte_len]);
                        }

                        // Write db type and packed / cryped field data
                        WriteDBType(rows_bs, (DBType)fieldInfo[z].type, GetWriteValue(dataKeys, i, y, z));
                    }

                    // Nullable Fields
                    if (tableInfo.nullableBitfields > 0)
                    {
                        byte[] bitArr = new byte[tableInfo.nullableBitfields*8];
                        for (int n = 0; n < Tables[i].NullableColumn.Count; n++) {
                            int index = Tables[i].Columns.IndexOf(Tables[i].NullableColumn[n]);
                            if (GetWriteValue(dataKeys, i, y, index) == null) {
                                bitArr[n] = 1;
                            }
                        }
                        rows_bs.ByteOffset = rowInfo.rowOffset + (tableInfo.numBytes * y) + tableInfo.numUsedBytes;
                        rows_bs.Write.BitArray(bitArr);
                    }
                }

                if (rowInfo.rowCount > 0) {
                    // Calc table length and add offset for alignment, the last row is shorter
                    uint tableLen = (uint)rows_bs.ByteOffset - rowInfo.rowOffset;
                    int mustBeDivisableBy128 = 128;
                    uint desiredLen = (uint) FindClosestLargerNumber((int)tableLen, mustBeDivisableBy128);
                    uint requiredPadding = desiredLen - tableLen;
                    uint correctNewOffset = rowInfo.rowOffset + desiredLen;
                    if (correctNewOffset != rows_bs.ByteOffset) {
                        rows_bs.Write.ByteArray(new byte[correctNewOffset - rows_bs.ByteOffset]);
                    }
                }
            }

            // === Info ===
            // Since it contains row offsets, this should go after row generation

            // Write table header
            info_bs.Write.UInt(this.memoryVersion);
            info_bs.Write.UShort((ushort) Tables.Count);

            // Write table info
            for (ushort i = 0; i < Tables.Count; i++)
            {
                info_bs.Write.Type<TableInfo>(tableInfos[i]);
            }

            // Write field info
            for (int i = 0; i < Tables.Count; i++)
            {
                for (int x = 0; x < tableInfos[i].numFields; x++)
                {
                    info_bs.Write.Type<FieldInfo>(fieldInfos[i][x]);
                }
            }

            // Write row info
            uint tableAndFieldInfoLength = (uint) info_bs.ByteOffset;
            uint rowInfoSectionLength = (uint) (Tables.Count * 8) + 37; // Size of RowInfo + last stuff
            uint rowsSectionOffset = tableAndFieldInfoLength + rowInfoSectionLength;
            for (ushort i = 0; i < Tables.Count; i++)
            {
                rowInfos[i].rowOffset += rowsSectionOffset;
                info_bs.Write.Type<RowInfo>(rowInfos[i]);
            }

            // Write data info
            // What, this wasnt parsed in the read?
            // Gotta add it tho D:
            int dataSectionOffset = (int) (memory_info.Length + memory_rows.Length) + 37;
            info_bs.Write.Int(dataSectionOffset);
            info_bs.Write.ByteArray(new byte[33]); // padding?

            // === Inflated ===
            // Put together the data that should be compressed
            inflated_bs.Write.ByteArray(memory_info.ToArray());
            inflated_bs.Write.ByteArray(memory_rows.ToArray());
            inflated_bs.Write.ByteArray(memory_data.ToArray());

            // === Deflated ===
            // Deflate inflated
            uint uncompressedSize = (uint) memory_inflated.Length;
            deflated_bs.Write.UInt(uncompressedSize);
            deflated_bs.Write.UInt(0); // size = long?
            deflated_bs.Write.ByteArray(new byte[] {0x78, 0x01}); // deflate header
            inflated_bs.ByteOffset = 0;
            Deflate(inflated_bs, deflated_bs, SharpCompress.Compressors.Deflate.CompressionLevel.BestSpeed);
            uint payloadSize = (uint) deflated_bs.ByteOffset;

            // === Header Info Prep ===
            HeaderInfo headerInfo = new HeaderInfo();
            headerInfo.magic = 0xDA7ABA5E;
            headerInfo.patchName = this.Patch;
            headerInfo.timestamp = (ulong)Util.Time.UnixTimestampMicrosecondsFromDatetime(Timestamp);
            headerInfo.flags = this.Flags;
            headerInfo.version = this.fileVersion;
            headerInfo.payloadSize = 0; // Will be set later


            // === Obfuscate ===
            byte[] obfuscatedData = memory_deflated.ToArray();
            if (Flags.HasFlag(HeaderFlags.ObfuscatedPool)) {
                MTXor(Checksum.FFnv32(headerInfo.patchName), ref obfuscatedData);
            }
            obfuscated_bs.Write.ByteArray(obfuscatedData);

            // === Header ===
            // Should be made at the end, since it needs to hold the payload size
            headerInfo.payloadSize = (uint) memory_obfuscated.Length; // Remember!
            header_bs.Write.Type<HeaderInfo>(headerInfo);

            // === Finally, Write ===
            bs.Write.ByteArray(memory_header.ToArray());
            bs.Write.ByteArray(memory_obfuscated.ToArray());

            // === Cleanup ===
            obfuscatedData = null;
            headerInfo = null;
            tableInfos = null;
            fieldInfos = null;
            rowInfos = null;
            memory_header = null;
            memory_info = null;
            memory_rows = null;
            memory_data = null;
            memory_inflated = null;
            memory_deflated = null;
            memory_obfuscated = null;
            header_bs.Dispose();
            info_bs.Dispose();
            rows_bs.Dispose();
            data_bs.Dispose();
            inflated_bs.Dispose();
            deflated_bs.Dispose();
            obfuscated_bs.Dispose();
            header_bs = null;
            info_bs = null;
            rows_bs = null;
            data_bs = null;
            inflated_bs = null;
            deflated_bs = null;
            obfuscated_bs = null;
        }
        #endregion

        #region IO methods
        public object ReadDBType(BinaryStream bs, DBType type)
        {
            switch (type)
            {
                case DBType.Byte:
                    return bs.Read.Byte();
                case DBType.UShort:
                    return bs.Read.UShort();
                case DBType.UInt:
                    return bs.Read.UInt();
                case DBType.ULong:
                    return bs.Read.ULong();
                case DBType.SByte:
                    return bs.Read.SByte();
                case DBType.Short:
                    return bs.Read.Short();
                case DBType.Int:
                    return bs.Read.Int();
                case DBType.Long:
                    return bs.Read.Long();
                case DBType.Float:
                    return bs.Read.Float();
                case DBType.Double:
                    return bs.Read.Double();
                case DBType.Vector2:
                    return bs.Read.Type<Vector2>();
                case DBType.Vector3:
                    return bs.Read.Type<Vector3>();
                case DBType.Vector4:
                    return bs.Read.Type<Vector4>();
                case DBType.Matrix4x4:
                    return bs.Read.Type<Matrix4x4>();
                case DBType.Box3:
                    return bs.Read.Type<Box3>();
                case DBType.AsciiChar:
                    return bs.Read.Char(BinaryStream.TextEncoding.ASCII);
                case DBType.HalfMatrix4x3:
                    return bs.Read.Type<HalfMatrix4x3>();
                case DBType.Half:
                    return bs.Read.Half(); // reads half as float
                case DBType.String:
                case DBType.Blob:
                case DBType.ByteArray:
                case DBType.UShortArray:
                case DBType.UIntArray:
                case DBType.Vector2Array:
                case DBType.Vector3Array:
                case DBType.Vector4Array:
                    if (memoryVersion == 1000) {
                        return bs.Read.ULong();
                    } else { // 1002
                        return bs.Read.UInt();
                    }
                default:
                    return null;
            }

        }
        public void WriteDBType(BinaryStream bs, DBType type, object obj)
        {
            if (obj == null)
            {
                // just write blank bytes
                bs.Write.ByteArray(new byte[DBTypeLength(type)]);
                return;
            }

            switch (type)
            {
                case DBType.Byte:
                    bs.Write.Byte((byte)obj);
                    break;
                case DBType.UShort:
                    bs.Write.UShort((ushort)obj);
                    break;
                case DBType.UInt:
                    bs.Write.UInt((uint)obj);
                    break;
                case DBType.ULong:
                    bs.Write.ULong((ulong)obj);
                    break;
                case DBType.SByte:
                    bs.Write.SByte((sbyte)obj);
                    break;
                case DBType.Short:
                    bs.Write.Short((short)obj);
                    break;
                case DBType.Int:
                    bs.Write.Int((int)obj);
                    break;
                case DBType.Long:
                    bs.Write.Long((long)obj);
                    break;
                case DBType.Float:
                    bs.Write.Float((float)obj);
                    break;
                case DBType.Double:
                    bs.Write.Double((double)obj);
                    break;
                case DBType.Vector2:
                    bs.Write.Type<Vector2>((Vector2)obj);
                    break;
                case DBType.Vector3:
                    bs.Write.Type<Vector3>((Vector3)obj);
                    break;
                case DBType.Vector4:
                    bs.Write.Type<Vector4>((Vector4)obj);
                    break;
                case DBType.Matrix4x4:
                    bs.Write.Type<Matrix4x4>((Matrix4x4)obj);
                    break;
                case DBType.Box3:
                    bs.Write.Type<Box3>((Box3)obj);
                    break;
                case DBType.AsciiChar:
                    bs.Write.Char((char)obj, BinaryStream.TextEncoding.ASCII);
                    break;
                case DBType.HalfMatrix4x3:
                    bs.Write.Type<HalfMatrix4x3>((HalfMatrix4x3)obj);
                    break;
                case DBType.Half:
                     bs.Write.Half((float)obj); // write float as half
                    break;
                case DBType.String:
                case DBType.Blob:
                case DBType.ByteArray:
                case DBType.UShortArray:
                case DBType.UIntArray:
                case DBType.Vector2Array:
                case DBType.Vector3Array:
                case DBType.Vector4Array:
                    //bs.Write.UInt(GetDataKey(type, obj));
                    // TEMP: Don't expect conversion. Need to setup the hash storage for this I guess.
                    bs.Write.UInt((uint)obj);
                    break;
                default:
                    throw new NotSupportedException($"Can't write a value of type {type}");
            }

        }
        public byte[] GetDataEntry(uint key)
        {
            return uniqueEntries1002.GetValueOrDefault(key);
        }

        private object GetWriteValue(Dictionary<(int, int, int), uint> dataKeys, int table, int row, int column)
        {
            if (!IsDataType(Tables[table].Columns[column].Type))
                return Tables[table].Rows[row].Fields[column];

            // Empty data is written as null, there's no pool entry for it
            return dataKeys.TryGetValue((table, row, column), out uint key) ? key : null;
        }


        private uint GenerateDataEntryKey(uint offset, uint dataLength) {
            uint key = 0;

            // What if we just always?
            // Write length to the beginning of data and use 31 bits for offset.
            uint pos = offset;
            key = pos << 1;
            key = key | 1;
            return key;

            // FIXME: Supposed to have two different variants but it didn't work properly.
            // The key generation itself seems okay with original offsets. (sample-size: 3 entries)
            /*
            // Can we use 7 bits for the length and 24 for the offset?
            if (dataLength <= 0x7F && offset <= 0xFFFFFF) {

                //length = BitConverter.GetBytes(key)[3];
                //address = 0x7FFFFF & address;

                // Yes
                byte len = (byte)dataLength;
                key = (uint) len << 24;
                key = key | (offset << 1);
            }
            else {
                // No, write length to the beginning of data and use 31 bits for offset.
                uint pos = offset; // & 0x7FFFFFFF;
                key = pos << 1;
                key = key | 1;
            }
            return key;
            */
        }

        private byte[] TwistDataEntry(uint key, byte[] data) {
            if (data.Length == 0)
                return null;

            MersenneTwister.Xor(key, data);
            return data;
        }

        private byte[] GetDataEntry(byte[] pool, ulong key, int row)
        {
            int address = (int)(key & 0x00000000FFFFFFFFU);
            int length = (int)(key >> 32);
            if (length == 0)
                return null;

            byte[] data = pool.AsSpan(address, length).ToArray();
            MersenneTwister.Xor((uint)row, data);
            return data;
        }

        private byte[] GetDataEntry(byte[] pool, uint key)
        {
            int address;
            int length;
            if ((key & 1) > 0)
            {
                address = (int)(key >> 1);
                length = BinaryPrimitives.ReadUInt16LittleEndian(pool.AsSpan(address));
                address += 2;
            }
            else
            {
                address = (int)((key >> 1) & 0x7FFFFF);
                length = (int)(key >> 24);
            }

            if (length == 0)
                return null;

            byte[] data = pool.AsSpan(address, length).ToArray();
            if (Flags.HasFlag(HeaderFlags.Client))
            {
                MersenneTwister.Xor(key, data);
            }
            return data;
        }
        private byte[] DBTypeToBytes(DBType type, object data)
        {
            byte[] bytes = null;

            switch (type)
            {
                case DBType.String:
                    bytes = Encoding.UTF8.GetBytes((string)data);
                    break;
                case DBType.Blob:
                case DBType.ByteArray:
                    bytes = ((List<byte>)data).ToArray();
                    break;
                case DBType.UShortArray:
                    List<ushort> uShortList = (List<ushort>)data;
                    bytes = new byte[uShortList.Count * 2];
                    for (int i = 0; i < uShortList.Count; i++)
                    {
                        WriteToBufferLE(bytes, uShortList[i], i * 2);
                    }
                    break;
                case DBType.UIntArray:
                    List<uint> uIntList = (List<uint>)data;
                    bytes = new byte[uIntList.Count * 4];
                    for (int i = 0; i < uIntList.Count; i++)
                    {
                        WriteToBufferLE(bytes, uIntList[i], i * 4);
                    }
                    break;
                case DBType.Vector2Array:
                    List<Vector2> vector2List = (List<Vector2>)data;

                    bytes = new byte[vector2List.Count * 8];
                    for (int i = 0, x = 0; i < vector2List.Count; i++, x += 8)
                    {
                        WriteToBufferLE(bytes, vector2List[i].x, x);
                        WriteToBufferLE(bytes, vector2List[i].y, x + 4);
                    }
                    break;
                case DBType.Vector3Array:
                    List<Vector3> vector3List = (List<Vector3>)data;
                    bytes = new byte[vector3List.Count * 12];
                    for (int i = 0, x = 0; i < vector3List.Count; i++, x += 12)
                    {
                        WriteToBufferLE(bytes, vector3List[i].x, x);
                        WriteToBufferLE(bytes, vector3List[i].y, x + 4);
                        WriteToBufferLE(bytes, vector3List[i].z, x + 8);
                    }
                    break;
                case DBType.Vector4Array:
                    List<Vector4> vector4List = (List<Vector4>)data;
                    bytes = new byte[vector4List.Count * 16];
                    for (int i = 0, x = 0; i < vector4List.Count; i++, x += 16)
                    {
                        WriteToBufferLE(bytes, vector4List[i].x, x);
                        WriteToBufferLE(bytes, vector4List[i].y, x + 4);
                        WriteToBufferLE(bytes, vector4List[i].z, x + 8);
                        WriteToBufferLE(bytes, vector4List[i].w, x + 12);
                    }
                    break;
            }
            return bytes;
        }
        private object BytesToDBType(DBType type, byte[] data)
        {
            switch (type)
            {
                case DBType.String:
                    return Encoding.UTF8.GetString(data);

                case DBType.Blob:
                case DBType.ByteArray:
                    return new List<byte>(data);

                case DBType.UShortArray:
                    List<ushort> uShortList = new List<ushort>(data.Length / 2);
                    if (data.Length > 1)
                    {
                        for (int i = 0; i < data.Length; i += 2)
                        {
                            uShortList.Add(UShortFromBufferLE(data, i));
                        }
                    }
                    return uShortList;
                case DBType.UIntArray:
                    List<uint> uIntList = new List<uint>(data.Length / 4);
                    if (data.Length > 1)
                    {
                        for (int i = 0; i < data.Length; i += 4)
                        {
                            uIntList.Add(UIntFromBufferLE(data, i));
                        }
                    }
                    return uIntList;

                case DBType.Vector2Array:
                    List<Vector2> vector2List = new List<Vector2>(data.Length / 8);
                    if (data.Length > 1)
                    {
                        for (int i = 0; i < data.Length; i += 8)
                        {
                            Vector2 v2 = new Vector2();
                            v2.x = FloatFromBufferLE(data, i);
                            v2.y = FloatFromBufferLE(data, i + 4);
                            vector2List.Add(v2);
                        }
                    }
                    return vector2List;

                case DBType.Vector3Array:
                    List<Vector3> vector3List = new List<Vector3>(data.Length / 12);
                    if (data.Length > 1)
                    {
                        for (int i = 0; i < data.Length; i += 12)
                        {
                            Vector3 v3 = new Vector3();
                            v3.x = FloatFromBufferLE(data, i);
                            v3.y = FloatFromBufferLE(data, i + 4);
                            v3.z = FloatFromBufferLE(data, i + 8);
                            vector3List.Add(v3);
                        }
                    }
                    return vector3List;

                case DBType.Vector4Array:
                    List<Vector4> vector4List = new List<Vector4>(data.Length / 16);
                    if (data.Length > 1)
                    {
                        for (int i = 0; i < data.Length; i += 16)
                        {
                            Vector4 v4 = new Vector4();
                            v4.x = FloatFromBufferLE(data, i);
                            v4.y = FloatFromBufferLE(data, i + 4);
                            v4.z = FloatFromBufferLE(data, i + 8);
                            v4.w = FloatFromBufferLE(data, i + 12);
                            vector4List.Add(v4);
                        }
                    }
                    return vector4List;
            }
            return null;
        }
        #endregion

        #region Indexers

        public Table this[int index]
        {
            get
            {
                return Tables[index];
            }
            set
            {
                Tables[index] = value;
            }
        }
        public int GetIndexByName(string name)
        {
            return GetIndexById(Checksum.FFnv32(name));
        }
        public int GetIndexById(uint id)
        {
            for (int i = 0; i < Tables.Count; i++)
            {
                if (Tables[i].Id == id)
                {
                    return i;
                }
            }
            return -1;
        }
        public Table GetTableByName(string name)
        {
            int index = GetIndexByName(name);
            return index >= 0 ? Tables[index] : throw new KeyNotFoundException($"No table named {name}");
        }
        public Table GetTableById(uint id)
        {
            int index = GetIndexById(id);
            return index >= 0 ? Tables[index] : throw new KeyNotFoundException($"No table with id {id}");
        }

        public IEnumerator<Table> GetEnumerator()
        {
            return ((IEnumerable<Table>)Tables).GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<Table>)Tables).GetEnumerator();
        }
        #endregion

        #region Enums
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
            Box3 = 17,
            Vector2Array = 18,
            Vector3Array = 19,
            Vector4Array = 20,
            AsciiChar = 21,

            // Present by beta-1475
            ByteArray = 22,
            UShortArray = 23,
            UIntArray = 24,

            // Present by beta-1869
            HalfMatrix4x3 = 25,
            Half = 26,
        }
        private static byte[] dbTypeLookup1000 = new byte[]
        {
            0,
            1,
            2,
            4,
            8,
            1,
            2,
            4,
            8,
            4,
            8,
            8,
            8,
            12,
            16,
            64,
            8,
            24,
            8,
            8,
            8,
            1,
            8,
            8,
            8,
            24,
            2
        };
        private static byte[] dbTypeLookup1002 = new byte[]
        {
            0,
            1,
            2,
            4,
            8,
            1,
            2,
            4,
            8,
            4,
            8,
            4,
            8,
            12,
            16,
            64,
            4,
            24,
            4,
            4,
            4,
            1,
            4,
            4,
            4,
            24,
            2
        };

        public static byte DBTypeLength(DBType type, uint memoryVersion)
        {
            if (memoryVersion == 1000) {
                return dbTypeLookup1000[(byte)type];
            } else {
                return dbTypeLookup1002[(byte)type];
            }
        }

        public byte DBTypeLength(DBType type)
        {
            return DBTypeLength(type, this.memoryVersion);
        }

        private static DBType[] dataTypes = new DBType[]
        {
                DBType.String,
                DBType.Blob,
                DBType.Vector2Array,
                DBType.Vector3Array,
                DBType.Vector4Array,
                DBType.ByteArray,
                DBType.UShortArray,
                DBType.UIntArray
        };

        public static bool IsDataType(DBType type)
        {
            for (int i = 0; i < dataTypes.Length; i++)
            {
                if (type == dataTypes[i])
                {
                    return true;
                }
            }
            return false;
        }
        #endregion

        #region Subclasses
        public class Table : IEnumerable<Row>
        {
            public uint Id;
            public List<Column> Columns;
            public List<Column> NullableColumn;
            public List<Row> Rows;

            public int GetColumnIndexByName(string name)
            {
                return GetColumnIndexById(Checksum.FFnv32(name));
            }
            public int GetColumnIndexById(uint id)
            {
                for (int i = 0; i < Columns.Count; i++)
                {
                    if (Columns[i].Id == id)
                    {
                        return i;
                    }
                }
                return -1;
            }
            public Column GetColumnByName(string name)
            {
                int index = GetColumnIndexByName(name);
                return index >= 0 ? Columns[index] : throw new KeyNotFoundException($"No column named {name}");
            }
            public Column GetColumnByName(uint id)
            {
                int index = GetColumnIndexById(id);
                return index >= 0 ? Columns[index] : throw new KeyNotFoundException($"No column with id {id}");
            }
            public bool IsColumnNullable(Column column)
            {
                return NullableColumn.Contains(column);
            }

            public Row this[int index]
            {
                get
                {
                    return Rows[index];
                }

                set
                {
                    Rows[index] = value;
                }
            }

            public IEnumerator<Row> GetEnumerator()
            {
                return ((IEnumerable<Row>)Rows).GetEnumerator();
            }
            IEnumerator IEnumerable.GetEnumerator()
            {
                return ((IEnumerable<Row>)Rows).GetEnumerator();
            }
        }
        public class Row : IEnumerable<object>
        {
            public List<object> Fields;
            public Row()
            {
                Fields = new List<object>();
            }
            public Row(int initialFields)
            {
                Fields = new List<object>(initialFields);
            }
            public object this[int index]
            {
                get
                {
                    return Fields[index];
                }
                set
                {
                    Fields[index] = value;
                }
            }

            public IEnumerator<object> GetEnumerator()
            {
                return ((IEnumerable<object>)Fields).GetEnumerator();
            }
            IEnumerator IEnumerable.GetEnumerator()
            {
                return ((IEnumerable<object>)Fields).GetEnumerator();
            }
        }
        public class Column
        {
            public uint Id;
            public DBType Type;
            public int Padding = 0;
        }
        #endregion


        private class ByteArrayComparer : IEqualityComparer<byte[]>
        {
            public bool Equals(byte[] x, byte[] y) => x.AsSpan().SequenceEqual(y);

            public int GetHashCode(byte[] obj)
            {
                HashCode hash = new HashCode();
                hash.AddBytes(obj);
                return hash.ToHashCode();
            }
        }

        #region Sdb file structs
        [Flags]
        public enum HeaderFlags : uint
        {
            ObfuscatedPool          = 1U << 0, // The whole payload is MT XORed
            BigEndian               = 1U << 1,
            Compressed              = 1U << 2,
            Client                  = 1U << 3, // Every pool entry is MT XORed with its key
            Server                  = 1u << 4,
        }
        private class HeaderInfo : ReadWrite
        {
            public uint magic;
            public uint version;
            public uint payloadSize;
            public HeaderFlags flags;
            public ulong timestamp;
            public string patchName;

            public void Read(BinaryStream bs)
            {
                magic = bs.Read.UInt();
                version = bs.Read.UInt();
                payloadSize = bs.Read.UInt();
                flags = (HeaderFlags)bs.Read.UInt();
                timestamp = bs.Read.ULong();
                patchName = bs.Read.String(104).Trim().Split('\0')[0];
            }

            public void Write(BinaryStream bs)
            {
                bs.Write.UInt(magic);
                bs.Write.UInt(version);
                bs.Write.UInt(payloadSize);
                bs.Write.UInt((uint)flags);
                bs.Write.ULong(timestamp);
                bs.Write.String(patchName);
                bs.Write.ByteArray(new byte[104 - patchName.Length]);
            }
        }
        private class TableInfo : ReadWrite
        {
            public uint id;
            public ushort numBytes; // data is aligned to 4 bytes, so this is always divisible by 4 (numUsedBytes + nullableBitfields)
            public ushort numFields;
            public ushort numUsedBytes; // actual number of bytes used for row data
            public byte nullableBitfields;  // if this is 1 we can have up to 8 nullable fields, 2 = 16 fields and so on
                                            // stored as a bitfield after the row data

            public void Read(BinaryStream bs)
            {
                id = bs.Read.UInt();
                numBytes = bs.Read.UShort();
                numFields = bs.Read.UShort();
                numUsedBytes = bs.Read.UShort();
                nullableBitfields = bs.Read.Byte();
            }

            public void Write(BinaryStream bs)
            {
                bs.Write.UInt(id);
                bs.Write.UShort(numBytes);
                bs.Write.UShort(numFields);
                bs.Write.UShort(numUsedBytes);
                bs.Write.Byte(nullableBitfields);
            }

            public override string ToString()
            {
                return $"[Id: {id}, NumBytes: {numBytes}, NumUsedBytes: {numUsedBytes}, nullableBitfields: {nullableBitfields}]";
            }
        }
        private class FieldInfo : ReadWrite
        {
            public uint id;
            public ushort start;
            public byte nullableIndex; // bit N from the bitfield stored after the row data. | default is 255 / not nullable
            public byte type;

            public void Read(BinaryStream bs)
            {
                id = bs.Read.UInt();
                start = bs.Read.UShort();
                nullableIndex = bs.Read.Byte();
                type = bs.Read.Byte();
            }

            public void Write(BinaryStream bs)
            {
                bs.Write.UInt(id);
                bs.Write.UShort(start);
                bs.Write.Byte(nullableIndex);
                bs.Write.Byte(type);
            }

            public override string ToString()
            {
                return $"[Id: {id}, Start: {start}, NullableBitIndex: {nullableIndex}, Type: {(DBType)type}]";
            }
        }
        private class RowInfo : ReadWrite
        {
            public uint rowOffset;
            public uint rowCount;

            public void Read(BinaryStream bs)
            {
                rowOffset = bs.Read.UInt();
                rowCount = bs.Read.UInt();
            }

            public void Write(BinaryStream bs)
            {
                bs.Write.UInt(rowOffset);
                bs.Write.UInt(rowCount);
            }

            public override string ToString()
            {
                return $"[Offset: {rowOffset}, Count: {rowCount}]";
            }
        }
        #endregion
    }
}
