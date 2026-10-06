using FauFau.Hax.Patches;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using PatchedStore = System.Collections.Generic.Dictionary<string, FauFau.Hax.PatchResult>;

namespace FauFau.Hax
{
    // A class for applying and managing patches on the Firefall client exe across multiple versions
    public class Patcher
    {
        public string Path                  = null;
        public byte[] FileData              = null;
        public PatchedStore ApplyiedPatches = new PatchedStore();

        public Patcher(string FilePath)
        {
            Path     = FilePath;
            FileData = File.ReadAllBytes(FilePath);
        }

        public static BasePatch[] GetPatchList()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            var types         = assembly.GetTypes().Where(t => t.BaseType == typeof(BasePatch));

            var patches = new List<BasePatch>();
            foreach (var patchType in types)
            {
                var patch = Activator.CreateInstance(patchType) as BasePatch;
                patches.Add(patch);
            }

            return patches.ToArray();
        }

        public PatchResult ApplyPatch(BasePatch Patch)
        {
            var result = Patch.Apply(this);

            if (result.Success && !ApplyiedPatches.ContainsKey(Patch.ID))
            {
                ApplyiedPatches.Add(Patch.ID, result);
            }

            return result;
        }

        public long GetSimpleOffset(string Pattern)
        {
            return new BytePattern(Encoding.ASCII.GetBytes(Pattern)).Find(FileData);
        }

        public long GetSimpleOffset(byte[] Pattern)
        {
            return new BytePattern(Pattern).Find(FileData);
        }

        public PatchedDataBackup PatchData(long Offset, byte[] Data)
        {
            var bk = new PatchedDataBackup()
            {
                Offset = Offset,
                Data   = new byte[Data.Length]
            };

            Array.Copy(FileData, Offset, bk.Data, 0, Data.Length);
            Array.Copy(Data, 0, FileData, Offset, Data.Length);

            return bk;
        }

        // Roll back in reverse order, so overlapping patches restore the original data
        public void RollBackPatches(PatchedDataBackup[] BackedUpPatches)
        {
            for (int i = BackedUpPatches.Length - 1; i >= 0; i--)
            {
                PatchData(BackedUpPatches[i].Offset, BackedUpPatches[i].Data);
            }
        }

        public void Save(string Name = null)
        {
            var dir      = System.IO.Path.GetDirectoryName(Path);
            var savePath = System.IO.Path.Combine(dir, "Firefall Client - TMW Patched.exe");

            if (Name != null)
            {
                savePath = System.IO.Path.Combine(dir, Name);
            }

            File.WriteAllBytes(savePath, FileData);
        }
    }

    public struct PatchResult
    {
        public bool Success;
        public string Message;
        public PatchedDataBackup[] OverwrittenBackup;
    }

    public struct PatchedDataBackup
    {
        public long Offset;
        public byte[] Data;
    }
}
