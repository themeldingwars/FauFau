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
    // A class for applying and mangine patches on the Firefall client exe across multiple versions
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

            if (!ApplyiedPatches.ContainsKey(Patch.ID))
            {
                ApplyiedPatches.Add(Patch.ID, result);
            }

            return result;
        }

        public long GetSimpleOffset(string Pattern)
        {
            var patternAsBytes = Encoding.ASCII.GetBytes(Pattern);
            var searchPattern  = BitConverter.ToString(patternAsBytes).Replace("-", " ");
            var sig            = PatternFinder.Pattern.Transform(searchPattern);
            PatternFinder.Pattern.FindAll(FileData, sig, out List<long> offsets);
            var offset = offsets.Count > 0 ? offsets[0] : -1;

            return offset;
        }

        public long GetSimpleOffset(byte[] Pattern)
        {
            var searchPattern = BitConverter.ToString(Pattern).Replace("-", " ");
            var sig           = PatternFinder.Pattern.Transform(searchPattern);
            PatternFinder.Pattern.FindAll(FileData, sig, out List<long> offsets);
            var offset = offsets.Count > 0 ? offsets[0] : -1;

            return offset;
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

        public void RollBackPatches(PatchedDataBackup[] BackedUpPatches)
        {
            foreach (var patch in BackedUpPatches)
            {
                PatchData(patch.Offset, patch.Data);
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
