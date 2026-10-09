using System.IO;
using FauFau.Formats;
using FauFau.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class StaticDBNamesTests
    {
        // Two names with the same FFnv32 hash
        private const string Colliding1 = "name139599";
        private const string Colliding2 = "name322382";

        [TestMethod]
        public void GetName_KnownHash_ReturnsName()
        {
            StaticDBNames names = new StaticDBNames();
            names.Add("dbzonemetadata::ZoneRecord");

            string name = names.GetName(0x69CE585DU);

            name.ShouldBe("dbzonemetadata::ZoneRecord");
        }

        [TestMethod]
        public void GetName_UnknownHash_ReturnsHex()
        {
            StaticDBNames names = new StaticDBNames();

            string name = names.GetName(0x05F38D1EU);

            name.ShouldBe("0x05F38D1E");
        }

        [TestMethod]
        public void Add_Collision_KeepsFirstName()
        {
            StaticDBNames names = new StaticDBNames();

            bool first = names.Add(Colliding1);
            bool second = names.Add(Colliding2);

            first.ShouldBeTrue();
            second.ShouldBeFalse();
            names.Count.ShouldBe(1);
            names.GetName(Checksum.FFnv32(Colliding2)).ShouldBe(Colliding1);
        }

        [TestMethod]
        public void GetCandidates_ReturnsEveryDistinctName()
        {
            StaticDBNames names = new StaticDBNames();
            names.AddRange(new[] { Colliding1, Colliding2, Colliding2, Colliding1 });

            var candidates = names.GetCandidates(Checksum.FFnv32(Colliding1));

            candidates.ShouldBe(new[] { Colliding1, Colliding2 });
        }

        [TestMethod]
        public void GetCandidates_UnknownHash_IsEmpty()
        {
            StaticDBNames names = new StaticDBNames();

            var candidates = names.GetCandidates(1);

            candidates.ShouldBeEmpty();
        }

        [TestMethod]
        public void AddFromFile_SkipsEmptyLines()
        {
            string path = Path.GetTempFileName();
            File.WriteAllLines(path, new[] { "id", "", "name" });
            StaticDBNames names = new StaticDBNames();

            try
            {
                names.AddFromFile(path);
            }
            finally
            {
                File.Delete(path);
            }

            names.Count.ShouldBe(2);
            names.TryGetName(Checksum.FFnv32("name"), out string name).ShouldBeTrue();
            name.ShouldBe("name");
        }
    }
}
