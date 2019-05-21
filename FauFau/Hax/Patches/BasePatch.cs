using System;
using System.Collections.Generic;
using System.Text;

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
    }
}
