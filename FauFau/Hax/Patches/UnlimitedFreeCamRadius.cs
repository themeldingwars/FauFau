namespace FauFau.Hax.Patches
{
    public class UnlimitedFreeCamRadius : BasePatch
    {
        public override string Name       => "Unlimited Freecam Radius";
        public override string Desc       => "Removes the annoying bubble when spectating in a replay and allows you to fly free!";
        private const string Pattern      =  @"speccam.freefly.addRadius";
        private readonly byte[] PatchData = new byte[] { 0x39, 0x39, 0x39, 0x39, 0x39 };

        public override PatchResult Apply(Patcher Patchy)
        {
            var offset       = Patchy.GetSimpleOffset(Pattern);
            var replaceStart = offset + Pattern.Length + 6;
            var bk           = Patchy.PatchData(replaceStart, PatchData);

            var result = new PatchResult()
            {
                Success           = true,
                OverwrittenBackup = new PatchedDataBackup[] { bk }
            };

            return result;
        }
    }
}
