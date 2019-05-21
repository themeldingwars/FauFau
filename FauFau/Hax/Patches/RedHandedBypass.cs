using System.Collections.Generic;

namespace FauFau.Hax.Patches
{
    public class RedHandedBypass : BasePatch
    {
        public override string Name => "Redhanded Bypass";
        public override string Desc => "Bypasses the Red handed service, this was preventing the game from launching on some systems now";
        private const byte NOP      = 0x90;

        // This is a bad translation of the redhanded bypass, most likey will only work for the latest client version
        // Prob would have been just as well hardcoding the offsets >,>
        // Ah well will do for now, can try and work out the offset from function sigs later
        public override PatchResult Apply(Patcher Patchy)
        {
            var applyiedPatches = new List<PatchedDataBackup>();

            var offset1 = Patchy.GetSimpleOffset(new byte[] { 0xFF, 0x15, 0xC0, 0x43, 0xAC, 0x01, 0x85, 0xC0, 0x74, 0x13 }) + 8;
            applyiedPatches.Add(Patchy.PatchData(offset1, new byte[] { NOP, NOP }));

            var pattern2 = new byte[] { 0xEE, 0x6F, 0x00, 0xE8, 0x08, 0x17, 0xEE, 0x00, 0x3D, 0xF2, 0x7F, 0x3B, 0x1C, 0x74 };
            var offset2  = Patchy.GetSimpleOffset(pattern2) + pattern2.Length - 1;
            applyiedPatches.Add(Patchy.PatchData(offset2, new byte[] { 0x75 }));

            var pattern3 = new byte[] { 0xB7, 0xED, 0x01, 0x57, 0x8B, 0xC8, 0xE8, 0xE1, 0x00, 0x00, 0x00, 0x8B, 0xD8, 0x85, 0xDB };
            var offset3 = Patchy.GetSimpleOffset(pattern3) + pattern3.Length;
            applyiedPatches.Add(Patchy.PatchData(offset3, new byte[] { 0x75 }));

            var result = new PatchResult()
            {
                Success           = true,
                OverwrittenBackup = applyiedPatches.ToArray()
            };

            return result;
        }
    }
}
