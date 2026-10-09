using System;
using System.IO;
using System.Text;
using FauFau.Formats;
using FauFau.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class LegacyStaticDBTests
    {
        private const uint RowId = 4242;

        private static byte[] Scramble(string text, uint seed)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            MersenneTwister random = new MersenneTwister(seed);
            for (int i = 0; i < bytes.Length; i++)
            {
                int key = (byte)random.Next();
                int sum = bytes[i] + key;
                bytes[i] = (byte)(sum > 255 ? sum + 1 : sum);
            }
            return bytes;
        }

        // The float column is nullable and empty in the second row
        private static byte[] CreateFile(bool scrambled)
        {
            const uint patch = 1265;
            using MemoryStream table = new MemoryStream();
            using (BinaryWriter write = new BinaryWriter(table, Encoding.UTF8, true))
            {
                void Text(string value) => write.Write(Encoding.UTF8.GetBytes(value + "\0"));
                void Cell(string value) => write.Write(scrambled ? Scramble(value, RowId) : Encoding.UTF8.GetBytes(value));

                write.Write(0x1234u);
                write.Write((byte)3);
                foreach ((byte type, string name, int nullable) in new[] { ((byte)3, "id", -1), ((byte)9, "value", 0), ((byte)11, "name", -1) })
                {
                    write.Write(type);
                    write.Write(Checksum.FFnv32(name));
                    Text("type");
                    Text(name);
                    Text("");
                    write.Write(0u);
                    write.Write(nullable);
                }
                write.Write(12);
                write.Write(13);
                write.Write(1u);
                Text("dbtest::Thing");
                Text("id");
                Text("");
                Text("");
                write.Write(0u);

                write.Write(2u);
                write.Write(RowId);
                write.Write((byte)1);
                write.Write(1u);
                write.Write(1.5f);
                Cell("Thumper");
                write.Write((byte)0);

                write.Write(RowId);
                write.Write((byte)0);
                write.Write(2u);
                Cell("Höhle");
                write.Write((byte)0);
            }

            using MemoryStream file = new MemoryStream();
            using (BinaryWriter write = new BinaryWriter(file, Encoding.UTF8, true))
            {
                int headerLength = scrambled ? 24 : 16;
                write.Write(5u);
                write.Write(scrambled ? LegacyStaticDB.ScrambledMagic : LegacyStaticDB.PlainMagic);
                write.Write(1328483834u);
                write.Write(0u);
                if (scrambled)
                {
                    write.Write(patch);
                    write.Write(2u);
                }
                write.Write(1);
                write.Write(headerLength + 8);

                byte[] data = table.ToArray();
                if (scrambled)
                    MersenneTwister.Xor(patch, data);

                write.Write(data);
            }
            return file.ToArray();
        }

        private static LegacyStaticDB Read(byte[] bytes)
        {
            LegacyStaticDB sdb = new LegacyStaticDB();
            sdb.Read(bytes);
            return sdb;
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void Read_ReadsSchemaAndRows(bool scrambled)
        {
            LegacyStaticDB sdb = Read(CreateFile(scrambled));

            LegacyStaticDB.Table table = sdb.GetTableByName("dbtest::Thing");
            table.Id.ShouldBe(0x1234U);
            table.PrimaryKey.ShouldBe("id");
            table.Columns[1].Type.ShouldBe(LegacyStaticDB.DBType.Float);
            table.Columns[2].Name.ShouldBe("name");
            table.Rows[0].Values.ShouldBe(new object[] { 1U, 1.5f, "Thumper" });
            table.Rows[1].Values.ShouldBe(new object[] { 2U, null, "Höhle" });
        }

        [TestMethod]
        public void Read_Scrambled_ReadsHeader()
        {
            LegacyStaticDB sdb = Read(CreateFile(true));

            sdb.Patch.ShouldBe(1265U);
            sdb.ScramblerVersion.ShouldBe(2U);
            sdb.TimeStamp.ShouldBe(new DateTime(2012, 2, 5, 23, 17, 14, DateTimeKind.Utc));
        }

        [TestMethod]
        public void Read_WrongMagic_Throws()
        {
            byte[] bytes = CreateFile(false);
            bytes[4] = 0;

            Should.Throw<InvalidDataException>(() => Read(bytes));
        }

        [TestMethod]
        public void Descramble_UndoesScramble()
        {
            byte[] bytes = Scramble("Calamity's Edge", 77);

            LegacyStaticDB.Descramble(bytes, 77);

            Encoding.UTF8.GetString(bytes).ShouldBe("Calamity's Edge");
        }
    }
}
