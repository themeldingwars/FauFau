using FauFau.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class EntityGuidTests
    {
        [TestMethod]
        public void Parse_PlayerGuid_SplitsParts()
        {
            EntityGuid guid = EntityGuid.Parse(0x1F54FA9E38037701);

            guid.ServerId.ShouldBe((byte)31);
            guid.Timestamp.ShouldBe(1425710592U);
            guid.Counter.ShouldBe(3670903U);
            guid.Type.ShouldBe((byte)0x01);
            guid.Full.ShouldBe(0x1F54FA9E38037701UL);
        }

        [TestMethod]
        public void Constructor_BuildsFull()
        {
            EntityGuid guid = new EntityGuid(31, 1425710592, 3670903, 0x01);

            guid.Full.ShouldBe(0x1F54FA9E38037701UL);
            guid.ShouldBe(EntityGuid.Parse(0x1F54FA9E38037701));
        }

        [TestMethod]
        public void Constructor_DropsBitsThatDontFit()
        {
            EntityGuid guid = new EntityGuid(1, 0x123456FF, 0x01ABCDEF, 2);

            guid.Timestamp.ShouldBe(0x12345600U);
            guid.Counter.ShouldBe(0x00ABCDEFU);
            guid.Full.ShouldBe(0x01123456ABCDEF02UL);
        }

        [TestMethod]
        [DataRow(0x7F3CB864AB349201UL, true, false)]
        [DataRow(0xFF000000000F0100UL, false, true)]
        [DataRow(0x0B56AA25E40391FDUL, false, false)]
        public void Kind_FollowsServerId(ulong full, bool v1, bool mapEntity)
        {
            EntityGuid guid = EntityGuid.Parse(full);

            guid.IsV1.ShouldBe(v1);
            guid.IsMapEntity.ShouldBe(mapEntity);
        }

        [TestMethod]
        public void ToString_IsHex()
        {
            EntityGuid guid = EntityGuid.Parse(0x0B56AA25E40391FD);

            string text = guid.ToString();

            text.ShouldBe("0x0B56AA25E40391FD");
        }
    }
}
