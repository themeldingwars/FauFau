using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Bitter;
using FauFau.Util;
using FauFau.Util.CommmonDataTypes;
using static Bitter.BinaryUtil;
using static FauFau.Util.Util;

namespace FauFau.Formats
{
    public class StaticDB : BinaryWrapper, IEnumerable<StaticDB.Table>
    {

        public string Patch;
        public DateTime Timestamp;
        public uint Flags;
        public List<Table> Tables;

        private uint fileVersion = 12;
        private uint tableVersion = 1002;
        private int numThreads = Environment.ProcessorCount;

        private static Dictionary<uint, int> tableIdLookup = new Dictionary<uint, int>();
        private static Dictionary<uint, Dictionary<uint, int>> fieldIdLookup = new Dictionary<uint, Dictionary<uint, int>>();
        private static Dictionary<string, uint> stringHashLookup = new Dictionary<string, uint>();
        private static Dictionary<uint, object> dataEntryCache = new Dictionary<uint, object>();
        private static Dictionary<uint, byte[]> uniqueEntries = new Dictionary<uint, byte[]>();

        #region File read & write
        public override void Read(BinaryStream bs)
        {

            Stopwatch sw = new Stopwatch();
            sw.Start();

            // read header
            HeaderInfo headerInfo = bs.Read.Type<HeaderInfo>();

            this.Patch = headerInfo.patchName;
            this.Timestamp = new DateTime();
            this.Timestamp = Util.Time.DateTimeFromUnixTimestampMicroseconds((long)headerInfo.timestamp);
            this.Flags = headerInfo.flags;
            this.fileVersion = headerInfo.version;

            //Console.WriteLine("read: "+sw.ElapsedMilliseconds);
            sw.Restart();

            // deobfuscate

            byte[] data = bs.Read.ByteArray((int)headerInfo.payloadSize);
            MTXor(Checksum.FFnv32(headerInfo.patchName), ref data);

            //Console.WriteLine("dxor: " + sw.ElapsedMilliseconds);
            sw.Restart();

            // cleanup memory, the original stream is not needed anymore
            bs.Dispose();
            bs = null;
            GC.Collect();

            // read compression header
            // uint inflated size
            // uint padding
            // ushort 0x78 0x01 zlib deflate low/no compression  

            uint inflatedSize = UIntFromBufferLE(ref data);
            ushort ds = UShortFromBufferLE(ref data, 8);

            byte[] inflated = new byte[inflatedSize];
            Inflate(data, ref inflated, SharpCompress.Compressors.Deflate.CompressionLevel.BestSpeed, (int)inflatedSize, 10);        
            BinaryStream ibs = new BinaryStream(new MemoryStream((inflated)));
            data = null;
            

            //Console.WriteLine("infl: " + sw.ElapsedMilliseconds);
            sw.Restart();

            // cleanup memory, the deobfuscated stream is not needed anymore
            GC.Collect();

            // read table header
            this.tableVersion = ibs.Read.UInt();
            ushort indexLength = ibs.Read.UShort();

            // read table info
            TableInfo[] tableInfos = new TableInfo[indexLength];
            for (ushort i = 0; i < indexLength; i++)
            {
                tableInfos[i] = ibs.Read.Type<TableInfo>();
            }

            // read field info
            FieldInfo[][] fieldInfos = new FieldInfo[indexLength][];
            for (int i = 0; i < indexLength; i++)
            {
                fieldInfos[i] = new FieldInfo[tableInfos[i].numFields];
                for (int x = 0; x < tableInfos[i].numFields; x++)
                {
                    fieldInfos[i][x] = ibs.Read.Type<FieldInfo>();
                }
            }

            // read row info
            RowInfo[] rowInfos = new RowInfo[indexLength];
            for (ushort i = 0; i < indexLength; i++)
            {
                rowInfos[i] = ibs.Read.Type<RowInfo>();
            }


            // build tables
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

            //Console.WriteLine("tabl: " + sw.ElapsedMilliseconds);
            sw.Restart();


            // read rows
            ConcurrentQueue<int> tableRowsReadQueue = new ConcurrentQueue<int>();
            for (ushort i = 0; i < indexLength; i++)
            {
                tableRowsReadQueue.Enqueue(i);
            }

            Parallel.For(0, numThreads, new ParallelOptions { MaxDegreeOfParallelism = numThreads }, q => {

                BinaryStream dbs = new BinaryStream(new MemoryStream(inflated));

                while (tableRowsReadQueue.Count != 0)
                {
                    int i;
                    if (!tableRowsReadQueue.TryDequeue(out i)) continue;

                    TableInfo tableInfo = tableInfos[i];
                    FieldInfo[] fieldInfo = fieldInfos[i];
                    RowInfo rowInfo = rowInfos[i];

                    Tables[i].Rows = new List<Row>();

                    for (int y = 0; y < rowInfo.rowCount; y++)
                    {
                        Row row = new Row(tableInfo.numFields);
                        dbs.ByteOffset = rowInfo.rowOffset + (tableInfo.numBytes * y) + fieldInfo[0].start;
                        for (int z = 0; z < tableInfo.numFields; z++)
                        {
                            if(Tables[i].Columns[z].Padding != 0)
                            {
                                dbs.ByteOffset += Tables[i].Columns[z].Padding;
                            }
                            // just read the basic type now, unpack & decrypt later to reduce seeking
                            row.Fields.Add(ReadDBType(dbs, (DBType)fieldInfo[z].type));
                        }

                        // null out nulls again :P
                        if (tableInfo.nullableBitfields > 0)
                        {
                            byte[] nulls = dbs.Read.BitArray(tableInfo.nullableBitfields * 8);
                            for (int n = 0; n < Tables[i].NullableColumn.Count; n++)
                            {
                                if (nulls[n] == 1)
                                {
                                    int index = Tables[i].Columns.IndexOf(Tables[i].NullableColumn[n]);
                                    row[index] = null;
                                }
                            }
                        }
                        Tables[i].Rows.Add(row);
                    }
                }
            });

            inflated = null;
            //Console.WriteLine("rows: " + sw.ElapsedMilliseconds);
            sw.Restart();

            // seek to the very end of the tables/start of data
            RowInfo lri = rowInfos[rowInfos.Length - 1];
            TableInfo lti = tableInfos[tableInfos.Length - 1];
            ibs.ByteOffset = lri.rowOffset + (lri.rowCount * lti.numBytes);

            // copy the data to a new stream 
            int dataLength = (int)(ibs.Length - ibs.ByteOffset);

            byte[] dataBlock = ibs.Read.ByteArray(dataLength);

            // cleanup
            ibs.Dispose();
            ibs = null;
            GC.Collect();

            // get unique data entry keys
            HashSet<uint> uniqueKeys = new HashSet<uint>();
            ConcurrentQueue<uint> uniqueQueue = new ConcurrentQueue<uint>();
            uniqueEntries = new Dictionary<uint, byte[]>();

            for (int i = 0; i < Tables.Count; i++)
            {
                for (int x = 0; x < Tables[i].Columns.Count; x++)
                {
                    DBType type = Tables[i].Columns[x].Type;

                    if (IsDataType(type))
                    {
                        for (int y = 0; y < Tables[i].Rows.Count; y++)
                        {
                            uint? k = (uint?)Tables[i].Rows[y][x];
                            if (k != null)
                            {
                                if (!uniqueKeys.Contains((uint)k))
                                {
                                    uniqueKeys.Add((uint)k);
                                    uniqueQueue.Enqueue((uint)k);
                                }
                            }
                        }

                    }
                }
            }

            //Console.WriteLine("uniq: " + sw.ElapsedMilliseconds);
            sw.Restart();

            // unpack & decrypt unique data entries to cache
            Parallel.For(0, numThreads, new ParallelOptions { MaxDegreeOfParallelism = numThreads }, i => {

                BinaryStream dbs = new BinaryStream(new MemoryStream(dataBlock));

                while (uniqueQueue.Count != 0)
                {
                    uint key;
                    if (!uniqueQueue.TryDequeue(out key)) continue;
                    byte[] d = GetDataEntry(dbs, key);

                    lock(uniqueEntries)
                    {
                        uniqueEntries.Add(key, d);
                    }
                }
                dbs.Dispose();
            });
            dataBlock = null;

            //Console.WriteLine("upac: " + sw.ElapsedMilliseconds);
            sw.Restart();

            // copy data entires to the tables from cache
            for (int z = 0; z < Tables.Count; z++)
            {
                for (int x = 0; x < Tables[z].Columns.Count; x++)
                {
                    DBType type = Tables[z].Columns[x].Type;
                    if (IsDataType(type))
                    {
                        Parallel.For(0, Tables[z].Rows.Count, y =>
                        {
                            uint? k = (uint?)Tables[z].Rows[y][x];
                            object obj = null;
                            if (k != null)
                            {
                                if (uniqueEntries.ContainsKey((uint)k))
                                {
                                    byte[] d = uniqueEntries[(uint)k];
                                    if (d != null)
                                    {
                                        obj = BytesToDBType(type, d);
                                    }
                                }

                            }
                            Tables[z].Rows[y][x] = obj;
                        });
                    }

                }
            }


            //Console.WriteLine("assi: " + sw.ElapsedMilliseconds);
            sw.Restart();

            // cleanup :>
            uniqueKeys = null;
            uniqueQueue = null;
            //uniqueEntries = null; // dont clean up these in case you need to look up data entries post load
            headerInfo = null;
            tableInfos = null;
            fieldInfos = null;
            rowInfos = null;
            GC.Collect();
        }
        public override void Write(BinaryStream bs)
        {

            // build data section
            int qq = 0;
            int rr = 0;
            HashSet<string> has = new HashSet<string>();

            for (int tn = 0; tn < Tables.Count; tn++)
            {
                Table table = Tables[tn];
                for (int fn = 0; fn < table.Columns.Count; fn++)
                {
                    Column field = table.Columns[fn];

                    switch (field.Type)
                    {
                        case DBType.String:
                        case DBType.Blob:
                        case DBType.ByteArray:
                        case DBType.UShortArray:
                        case DBType.UIntArray:
                        case DBType.Vector2Array:
                        case DBType.Vector3Array:
                        case DBType.Vector4Array:

                            for (int rn = 0; rn < table.Rows.Count; rn++)
                            {
                                Row row = table[rn];
                                if (row[fn] == null)
                                {

                                }
                                else
                                {
                                    if(field.Type == DBType.String)
                                    {
                                        //Console.WriteLine(row[fn]);
                                    }
                                }

                            }

                            break;
                    }
                }
            }
            Console.WriteLine("qq:"+qq);


            //header.Write(bs);
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
                case DBType.String:
                    return bs.Read.UInt();
                case DBType.Vector2:
                    return bs.Read.Type<Vector2>();
                case DBType.Vector3:
                    return bs.Read.Type<Vector3>();
                case DBType.Vector4:
                    return bs.Read.Type<Vector4>();
                case DBType.Matrix4x4:
                    return bs.Read.Type<Matrix4x4>();
                case DBType.Blob:
                    return bs.Read.UInt();;
                case DBType.Box3:
                    return bs.Read.Type<Box3>();
                case DBType.Vector2Array:
                    return bs.Read.UInt();
                case DBType.Vector3Array:
                    return bs.Read.UInt();
                case DBType.Vector4Array:
                    return bs.Read.UInt();
                case DBType.AsciiChar:
                    return bs.Read.Char(BinaryStream.TextEncoding.ASCII);
                case DBType.ByteArray:
                    return bs.Read.UInt();
                case DBType.UShortArray:
                    return bs.Read.UInt();
                case DBType.UIntArray:
                    return bs.Read.UInt();
                case DBType.HalfMatrix4x3:
                    return bs.Read.Type<HalfMatrix4x3>();
                case DBType.Half:
                    return bs.Read.Half(); // reads half as float
                default:
                    return null;
            }

        }
        public void WriteDBType(BinaryStream bs, DBType type, object obj)
        {
            if(obj == null)
            {
                // just write blank bytes
                bs.Write.ByteArray(new byte[DBTypeLength(type)]);
                return;
            }
            
            switch (type)
            {
                case DBType.Byte:
                    bs.Write.Byte((byte) obj);
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
                    bs.Write.Type((Vector2)obj);
                    break;
                case DBType.Vector3:
                    bs.Write.Type((Vector3)obj);
                    break;
                case DBType.Vector4:
                    bs.Write.Type((Vector4)obj);
                    break;
                case DBType.Matrix4x4:
                    bs.Write.Type((Matrix4x4)obj);
                    break;
                case DBType.Box3:
                    bs.Write.Type((Box3)obj);
                    break;
                case DBType.AsciiChar:
                    bs.Write.Char((char)obj, BinaryStream.TextEncoding.ASCII);
                    break;
                case DBType.HalfMatrix4x3:
                    bs.Write.Type((HalfMatrix4x3)obj);
                    break;
                case DBType.Half:
                     bs.Write.Half((float) obj); // write float as half
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
                    break;
            }

        }
        public byte[] GetDataEntry(uint key)
        {

            foreach (uint k in uniqueEntries.Keys)
            {
                Console.WriteLine(k);
                break;
            }
                

            
            if (uniqueEntries.ContainsKey(key))
            {
                return uniqueEntries[key];
            }
            return null;
        }
        private byte[] GetDataEntry(BinaryStream bs, uint key)
        {
            byte[] ret = null;

            uint address = key >> 1;
            uint length;
            object obj = null;

            if ((key & 1) > 0)
            {
                bs.ByteOffset = address;
                length = bs.Read.UShort();
            }
            else
            {
                length = BitConverter.GetBytes(key)[3];
                address = 0x7FFFFF & address;
                bs.ByteOffset = address;
            }

            if (length > 0)
            {
                MersenneTwister mt = new MersenneTwister(key);
                uint x = length >> 2;
                uint y = length & 3;

                byte[] data = bs.Read.ByteArray((int)length);
                byte[] xor = new byte[length];

                for (int i = 0; i < x; i++)
                {
                    WriteToBufferLE(ref xor, mt.Next(), i * 4);
                }
                int z = (int)x * 4;
                for (uint i = 0; i < y; i++)
                {
                    xor[z + i] = (byte)mt.Next();
                }
                for (int i = 0; i < length; i++)
                {
                    data[i] ^= xor[i];
                }
                ret = data;
            }
            return ret;
        }
        private byte[] DBTypeToBytes(DBType type, object data)
        {

            FloatByteMap floatMap = new FloatByteMap();
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
                        WriteToBufferLE(ref bytes, uShortList[i], i * 2);
                    }
                    break;
                case DBType.UIntArray:
                    List<uint> uIntList = (List<uint>)data;
                    bytes = new byte[uIntList.Count * 4];
                    for (int i = 0; i < uIntList.Count; i++)
                    {
                        WriteToBufferLE(ref bytes, uIntList[i], i * 4);
                    }
                    break;
                case DBType.Vector2Array:
                    List<Vector2> vector2List = (List<Vector2>)data;

                    bytes = new byte[vector2List.Count * 8];
                    for (int i = 0, x = 0; i < vector2List.Count; i++, x += 8)
                    {
                        WriteToBufferLE(ref bytes, ref floatMap, vector2List[i].x, x);
                        WriteToBufferLE(ref bytes, ref floatMap, vector2List[i].y, x + 4);
                    }
                    break;
                case DBType.Vector3Array:
                    List<Vector3> vector3List = (List<Vector3>)data;
                    bytes = new byte[vector3List.Count * 12];
                    for (int i = 0, x = 0; i < vector3List.Count; i++, x += 12)
                    {
                        WriteToBufferLE(ref bytes, ref floatMap, vector3List[i].x, x);
                        WriteToBufferLE(ref bytes, ref floatMap, vector3List[i].y, x + 4);
                        WriteToBufferLE(ref bytes, ref floatMap, vector3List[i].z, x + 8);
                    }
                    break;
                case DBType.Vector4Array:
                    List<Vector4> vector4List = (List<Vector4>)data;
                    bytes = new byte[vector4List.Count * 16];
                    for (int i = 0, x = 0; i < vector4List.Count; i++, x += 16)
                    {
                        WriteToBufferLE(ref bytes, ref floatMap, vector4List[i].x, x);
                        WriteToBufferLE(ref bytes, ref floatMap, vector4List[i].y, x + 4);
                        WriteToBufferLE(ref bytes, ref floatMap, vector4List[i].z, x + 8);
                        WriteToBufferLE(ref bytes, ref floatMap, vector4List[i].w, x + 12);
                    }
                    break;
            }
            return bytes;
        }
        private object BytesToDBType(DBType type, byte[] data)
        {
            FloatByteMap floatMap = new FloatByteMap();

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
                            uShortList.Add(UShortFromBufferLE(ref data, i));
                        }
                    }
                    return uShortList;
                case DBType.UIntArray:
                    List<uint> uIntList = new List<uint>(data.Length / 4);
                    if (data.Length > 1)
                    {
                        for (int i = 0; i < data.Length; i += 4)
                        {
                            uIntList.Add(UIntFromBufferLE(ref data, i));
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
                            v2.x = FloatFromBufferLE(ref data, ref floatMap, i);
                            v2.y = FloatFromBufferLE(ref data, ref floatMap, i + 4);
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
                            v3.x = FloatFromBufferLE(ref data, ref floatMap, i);
                            v3.y = FloatFromBufferLE(ref data, ref floatMap, i + 4);
                            v3.z = FloatFromBufferLE(ref data, ref floatMap, i + 8);
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
                            v4.x = FloatFromBufferLE(ref data, ref floatMap, i);
                            v4.y = FloatFromBufferLE(ref data, ref floatMap, i + 4);
                            v4.z = FloatFromBufferLE(ref data, ref floatMap, i + 8);
                            v4.w = FloatFromBufferLE(ref data, ref floatMap, i + 12);
                            vector4List.Add(v4);
                        }
                    }
                    return vector4List;
            }
            return null;
        }
        #endregion

        #region Indexers

        private static uint GetHash(string str)
        {
            uint hash;
            if (stringHashLookup.ContainsKey(str))
            {
                hash = stringHashLookup[str];
            }
            else
            {
                hash = Checksum.FFnv32(str);
                stringHashLookup.Add(str, hash);
            }
            return hash;
        }

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
            return GetIndexById(GetHash(name));
        }
        public int GetIndexById(uint id)
        {
            for(int i = 0; i < Tables.Count; i++)
            {
                if(Tables[i].Id == id)
                {
                    return i;
                }
            }
            return -1;
        }
        public Table GetTableByName(string name)
        {
            return Tables[GetIndexByName(name)];
        }
        public Table GetTableById(uint id)
        {
            return Tables[GetIndexById(id)];
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
            ByteArray = 22,
            UShortArray = 23,
            UIntArray = 24,
            HalfMatrix4x3 = 25,
            Half = 26,
        }
        private static byte[] dbTypeLookup = new byte[]
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
        public static byte DBTypeLength(DBType type)
        {
            return dbTypeLookup[(byte)type];
        }

        private static DBType[] dataTypes = new DBType[]
        {
                DBType.String,
                DBType.Blob,
                DBType.ByteArray,
                DBType.UShortArray,
                DBType.UIntArray,
                DBType.Vector2Array,
                DBType.Vector3Array,
                DBType.Vector4Array
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
                return GetColumnIndexById(GetHash(name));
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
                return Columns[GetColumnIndexByName(name)];
            }
            public Column GetColumnByName(uint id)
            {
                return Columns[GetColumnIndexById(id)];
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


        #region Sdb file structs
        private class HeaderInfo : ReadWrite
        {
            public uint magic;
            public uint version;
            public uint payloadSize;
            public uint flags;
            public ulong timestamp;
            public string patchName;

            public void Read(BinaryStream bs)
            {
                magic = bs.Read.UInt();
                version = bs.Read.UInt();
                payloadSize = bs.Read.UInt();
                flags = bs.Read.UInt();
                timestamp = bs.Read.ULong();
                patchName = bs.Read.String(104).Trim().Split('\0')[0];
            }

            public void Write(BinaryStream bs)
            {
                bs.Write.UInt(magic);
                bs.Write.UInt(version);
                bs.Write.UInt(payloadSize);
                bs.Write.UInt(flags);
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
                throw new NotImplementedException();
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
        }
        #endregion

    }
}
