using System.IO;
using System.Linq;
using System.Text;
using FauFau.Hax;
using FauFau.Hax.Patches;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class PatcherTests
    {
        private static readonly byte[] RedHanded1 = { 0xFF, 0x15, 0xC0, 0x43, 0xAC, 0x01, 0x85, 0xC0, 0x74, 0x13 };
        private static readonly byte[] RedHanded2 = { 0xEE, 0x6F, 0x00, 0xE8, 0x08, 0x17, 0xEE, 0x00, 0x3D, 0xF2, 0x7F, 0x3B, 0x1C, 0x74 };
        private static readonly byte[] RedHanded3 = { 0xB7, 0xED, 0x01, 0x57, 0x8B, 0xC8, 0xE8, 0xE1, 0x00, 0x00, 0x00, 0x8B, 0xD8, 0x85, 0xDB };

        private string path;

        [TestInitialize]
        public void Initialize()
        {
            path = Path.GetTempFileName();
        }

        [TestCleanup]
        public void Cleanup()
        {
            File.Delete(path);
        }

        private Patcher CreatePatcher(params byte[][] parts)
        {
            File.WriteAllBytes(path, parts.SelectMany(p => p).ToArray());
            return new Patcher(path);
        }

        private static byte[] Filler(int length)
        {
            return Enumerable.Repeat((byte)0xCC, length).ToArray();
        }

        [TestMethod]
        public void GetSimpleOffset_ReturnsFirstMatchOrMinusOne()
        {
            Patcher patcher = CreatePatcher(Filler(10), Encoding.ASCII.GetBytes("abcabc"));

            long text = patcher.GetSimpleOffset("abc");
            long bytes = patcher.GetSimpleOffset(new byte[] { 0x62, 0x63 });
            long missing = patcher.GetSimpleOffset("xyz");

            text.ShouldBe(10);
            bytes.ShouldBe(11);
            missing.ShouldBe(-1);
        }

        [TestMethod]
        public void PatchData_RollBack_RestoresData()
        {
            Patcher patcher = CreatePatcher(Filler(16));
            byte[] original = (byte[])patcher.FileData.Clone();

            PatchedDataBackup first = patcher.PatchData(4, new byte[] { 1, 2, 3, 4 });
            PatchedDataBackup second = patcher.PatchData(6, new byte[] { 5, 6, 7 });
            patcher.RollBackPatches(new[] { first, second });

            patcher.FileData.ShouldBe(original);
        }

        [TestMethod]
        public void UnlimitedFreeCamRadius_PatchesValueAfterName()
        {
            byte[] name = Encoding.ASCII.GetBytes("speccam.freefly.addRadius");
            Patcher patcher = CreatePatcher(Filler(8), name, Filler(3), Encoding.ASCII.GetBytes("50\0\0\0\0\0\0"), Filler(8));

            PatchResult result = patcher.ApplyPatch(new UnlimitedFreeCamRadius());

            result.Success.ShouldBeTrue();
            patcher.FileData.Skip(8 + name.Length + 3).Take(8).ShouldBe(Encoding.ASCII.GetBytes("9999999\0"));
            patcher.ApplyiedPatches.ShouldContainKey(nameof(UnlimitedFreeCamRadius));
        }

        [TestMethod]
        public void RedHandedBypass_PatchesAllThreeLocations()
        {
            Patcher patcher = CreatePatcher(Filler(4), RedHanded1, Filler(4), RedHanded2, Filler(4), RedHanded3, Filler(4));

            PatchResult result = patcher.ApplyPatch(new RedHandedBypass());

            result.Success.ShouldBeTrue();
            result.OverwrittenBackup.Length.ShouldBe(3);
            patcher.FileData[4 + 8].ShouldBe((byte)0x90);
            patcher.FileData[4 + 9].ShouldBe((byte)0x90);
            patcher.FileData[18 + 13].ShouldBe((byte)0x75);
            patcher.FileData[36 + 15].ShouldBe((byte)0x90);
            patcher.FileData[36 + 16].ShouldBe((byte)0x90);
        }

        [TestMethod]
        public void RedHandedBypass_IsApplied_OnlyAfterApply()
        {
            Patcher patcher = CreatePatcher(Filler(4), RedHanded1, Filler(4), RedHanded2, Filler(4), RedHanded3, Filler(4));
            RedHandedBypass patch = new RedHandedBypass();

            bool before = patch.IsApplied(patcher);
            patcher.ApplyPatch(patch);
            bool after = patch.IsApplied(patcher);

            before.ShouldBeFalse();
            after.ShouldBeTrue();
        }

        [TestMethod]
        public void UnlimitedFreeCamRadius_IsApplied_OnlyAfterApply()
        {
            Patcher patcher = CreatePatcher(Encoding.ASCII.GetBytes("speccam.freefly.addRadius"), Filler(3), Encoding.ASCII.GetBytes("50\0\0\0\0\0\0"));
            UnlimitedFreeCamRadius patch = new UnlimitedFreeCamRadius();

            bool before = patch.IsApplied(patcher);
            patcher.ApplyPatch(patch);
            bool after = patch.IsApplied(patcher);

            before.ShouldBeFalse();
            after.ShouldBeTrue();
        }

        [TestMethod]
        public void Patches_MissingPattern_FailWithoutChangingData()
        {
            Patcher patcher = CreatePatcher(Filler(4), RedHanded1, Filler(4), RedHanded2, Filler(64));
            byte[] original = (byte[])patcher.FileData.Clone();

            PatchResult redHanded = patcher.ApplyPatch(new RedHandedBypass());
            PatchResult freeCam = patcher.ApplyPatch(new UnlimitedFreeCamRadius());

            redHanded.Success.ShouldBeFalse();
            freeCam.Success.ShouldBeFalse();
            patcher.FileData.ShouldBe(original);
            patcher.ApplyiedPatches.ShouldBeEmpty();
        }

        [TestMethod]
        public void Save_WritesNextToSource()
        {
            Patcher patcher = CreatePatcher(Filler(8));
            patcher.PatchData(0, new byte[] { 1 });
            string name = Path.GetFileName(path) + ".patched";
            string saved = Path.Combine(Path.GetDirectoryName(path), name);

            byte[] written;
            try
            {
                patcher.Save(name);
                written = File.ReadAllBytes(saved);
            }
            finally
            {
                File.Delete(saved);
            }

            written.ShouldBe(patcher.FileData);
        }

        [TestMethod]
        public void GetPatchList_FindsAllPatches()
        {
            BasePatch[] patches = Patcher.GetPatchList();

            patches.Select(p => p.ID).ShouldBe(new[] { nameof(RedHandedBypass), nameof(UnlimitedFreeCamRadius) }, ignoreOrder: true);
        }
    }
}
