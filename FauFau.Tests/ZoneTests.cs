using System;
using System.IO;
using System.Text;
using Bitter;
using FauFau.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class ZoneTests
    {
        private static BinaryStream CreateHeader(string name)
        {
            BinaryStream stream = new BinaryStream(new MemoryStream());
            stream.Write.String("ZONE");
            stream.Write.Int(8);
            stream.Write.Long(1461290437107);
            stream.Write.Int(name.Length + 1);
            stream.Write.ByteArray(Encoding.ASCII.GetBytes(name + "\0"));
            stream.Write.UInt(0xDEADBEEF);
            stream.ByteOffset = 0;
            return stream;
        }

        [TestMethod]
        public void Read_Header_ReadsMillisecondTimestampAndName()
        {
            BinaryStream stream = CreateHeader("New Eden");

            Zone zone = new Zone();

            zone.Read(stream);
            uint next = stream.Read.UInt();

            zone.Magic.ShouldBe("ZONE");
            zone.Version.ShouldBe(8);
            zone.TimeStamp.ShouldBe(new DateTime(2016, 4, 22, 2, 0, 37, 107, DateTimeKind.Utc));
            zone.Name.ShouldBe("New Eden");
            next.ShouldBe(0xDEADBEEF);
        }
    }
}
