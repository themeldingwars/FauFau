using System;
using System.IO;
using FauFau.Formats;
using static FauFau.Formats.StaticDB;

namespace Tests
{
    public class SDBTests
    {
        public static string sdbPathRead = @"X:\Firefall\system\db\clientdb.sd2";
        public static string sdbPathWrite = @"X:\Firefall\system\db\clientdb.write.sd2";

        public static void TestRead()
        {
            Console.WriteLine("SDBTests.TestRead: Reading SDB: " + sdbPathRead);
            StaticDB sdb = new StaticDB();
            sdb.Read(sdbPathRead);
            Console.WriteLine("Patch: " + sdb.Patch);
            Console.WriteLine("Flags: " + sdb.Flags);
            Console.WriteLine("Created: " + sdb.Timestamp.ToString() + " UTC");
            Console.WriteLine();
        }

        public static void TestWriteCustom()
        {
            // Read from a real SDB to get the base data
            Console.WriteLine("SDBTests.TestWriteCustom: Reading SDB: " + sdbPathRead);
            StaticDB sdb = new StaticDB();
            sdb.Read(sdbPathRead);

            // Proof of concept: Modify data
            if (true) {
                Row duplicateRow(Table table, Row existing) {
                    Row row = new Row(table.Columns.Count);
                    for (int x = 0; x < table.Columns.Count; x++) {
                        Column column = table.Columns[x];
                        if (IsDataType(column.Type)) {
                            row.Fields.Add( existing.Fields[x] );
                        }
                        else {
                            row.Fields.Add( existing.Fields[x] );
                        }
                    }
                    return row;
                }

                // Add ZoneRecord
                if (true) {
                    Table table = sdb.GetTableByName("dbzonemetadata::ZoneRecord");
                    Row existing = table[0];
                    Row row = duplicateRow(table, existing);
                    table.Rows.Insert(0, row);
                    row[table.GetColumnIndexByName("localized_name_id")] = (uint) 0;
                    row[table.GetColumnIndexByName("localized_main_title_id")] = (uint) 0;
                    row[table.GetColumnIndexByName("localized_minor_title_id")] = (uint) 0;
                    row[table.GetColumnIndexByName("localized_sub_title_id")] = (uint) 0;
                    row[table.GetColumnIndexByName("name")] = (string) "Net Slum\0";
                    row[table.GetColumnIndexByName("main_title")] = (string) "Net Slum\0";
                    row[table.GetColumnIndexByName("minor_title")] = (string) "FauFau\0";
                    row[table.GetColumnIndexByName("sub_title")] = (string) "In the database, database\0";
                    row[table.GetColumnIndexByName("id")] = (uint) 11;
                    row[table.GetColumnIndexByName("level_band")] = (ushort) 0;
                    row[table.GetColumnIndexByName("zoneType")] = (byte) 0;
                    row[table.GetColumnIndexByName("prevent_sub_zone_spawns")] = (byte) 0;
                }
                
                // Add ZoneChunkLinker
                if (true) {
                    Table table = sdb.GetTableByName("dbzonemetadata::ZoneChunkLinker");
                    Row existing = table[0];
                    Row row = duplicateRow(table, existing);
                    table.Rows.Insert(0, row);
                    row[table.GetColumnIndexByName("zoneid")] = (uint) 11;
                }
            }
            
            // Write
            Console.WriteLine("SDBTests.TestWriteCustom: Writing SDB: " + sdbPathWrite);
            sdb.Write(sdbPathWrite);

            Console.WriteLine();
        }

        public static void TestReadCustom()
        {
            Console.WriteLine("SDBTests.TestReadCustom: Reading from write path: " + sdbPathWrite);
            StaticDB sdb = new StaticDB();
            sdb.Read(sdbPathWrite);
            Console.WriteLine("SDBTests.TestReadCustom: Patch: " + sdb.Patch);
            Console.WriteLine("SDBTests.TestReadCustom: Flags: " + sdb.Flags);
            Console.WriteLine("SDBTests.TestReadCustom: Created: " + sdb.Timestamp.ToString() + " UTC");
            Console.WriteLine();
        }

    }
}