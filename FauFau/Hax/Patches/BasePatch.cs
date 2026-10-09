namespace FauFau.Hax.Patches
{
    public abstract class BasePatch
    {
        public string ID => GetType().Name;
        public abstract string Name { get; }
        public abstract string Desc { get; }

        public virtual PatchResult Apply(Patcher Patchy)
        {
            return new PatchResult() { Success = false, Message = "NA" };
        }

        // Whether the file already has this patch, false if the patch can't tell
        public virtual bool IsApplied(Patcher Patchy)
        {
            return false;
        }
    }
}
