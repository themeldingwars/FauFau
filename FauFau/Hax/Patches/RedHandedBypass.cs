using System.Collections.Generic;

namespace FauFau.Hax.Patches
{
    public class RedHandedBypass : BasePatch
    {
        public override string Name => "Redhanded Bypass";
        public override string Desc => "Bypasses the Red handed service, this was preventing the game from launching on some systems now";
        private const byte NOP      = 0x90;

        private static readonly byte[] Pattern1 = { 0xFF, 0x15, 0xC0, 0x43, 0xAC, 0x01, 0x85, 0xC0, 0x74, 0x13 };
        private static readonly byte[] Pattern2 = { 0xEE, 0x6F, 0x00, 0xE8, 0x08, 0x17, 0xEE, 0x00, 0x3D, 0xF2, 0x7F, 0x3B, 0x1C, 0x74 };
        private static readonly byte[] Pattern3 = { 0xB7, 0xED, 0x01, 0x57, 0x8B, 0xC8, 0xE8, 0xE1, 0x00, 0x00, 0x00, 0x8B, 0xD8, 0x85, 0xDB };

        // The patterns as Apply leaves them
        private static readonly byte[] Patched1 = { 0xFF, 0x15, 0xC0, 0x43, 0xAC, 0x01, 0x85, 0xC0, NOP, NOP };
        private static readonly byte[] Patched2 = { 0xEE, 0x6F, 0x00, 0xE8, 0x08, 0x17, 0xEE, 0x00, 0x3D, 0xF2, 0x7F, 0x3B, 0x1C, 0x75 };
        private static readonly byte[] Patched3 = { 0xB7, 0xED, 0x01, 0x57, 0x8B, 0xC8, 0xE8, 0xE1, 0x00, 0x00, 0x00, 0x8B, 0xD8, 0x85, 0xDB, NOP, NOP };

        // This is a bad translation of the redhanded bypass, most likely will only work for the latest client version
        // Prob would have been just as well hardcoding the offsets >,>
        // Ah well will do for now, can try and work out the offset from function sigs later
        public override PatchResult Apply(Patcher Patchy)
        {
            var found1 = Patchy.GetSimpleOffset(Pattern1);
            var found2 = Patchy.GetSimpleOffset(Pattern2);
            var found3 = Patchy.GetSimpleOffset(Pattern3);
            if (found1 < 0 || found2 < 0 || found3 < 0)
            {
                return new PatchResult() { Success = false, Message = "Not all patterns were found, the client version is probably not supported" };
            }

            var applyiedPatches = new List<PatchedDataBackup>();
            applyiedPatches.Add(Patchy.PatchData(found1 + 8, new byte[] { NOP, NOP }));
            applyiedPatches.Add(Patchy.PatchData(found2 + Pattern2.Length - 1, new byte[] { 0x75 }));
            applyiedPatches.Add(Patchy.PatchData(found3 + Pattern3.Length, new byte[] { NOP, NOP }));

            var result = new PatchResult()
            {
                Success           = true,
                OverwrittenBackup = applyiedPatches.ToArray()
            };

            return result;
        }

        public override bool IsApplied(Patcher Patchy)
        {
            return Patchy.GetSimpleOffset(Patched1) >= 0 && Patchy.GetSimpleOffset(Patched2) >= 0 && Patchy.GetSimpleOffset(Patched3) >= 0;
        }
    }
}
