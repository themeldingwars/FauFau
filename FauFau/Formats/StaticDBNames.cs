using System;
using System.Collections.Generic;
using System.IO;
using FauFau.Util;

namespace FauFau.Formats
{
    // Names for the FNV hashes that a StaticDB stores instead of table and column names.
    // The first name added for a hash wins, later different names for the same hash are kept as collisions.
    public sealed class StaticDBNames
    {
        private readonly Dictionary<uint, string> names = new();
        private readonly Dictionary<uint, List<string>> collisions = new();

        public int Count => names.Count;

        // Returns false if the hash already had a name
        public bool Add(string name)
        {
            uint hash = Checksum.FFnv32(name);
            if (names.TryAdd(hash, name))
                return true;

            if (names[hash] != name)
            {
                if (!collisions.TryGetValue(hash, out List<string> others))
                    collisions.Add(hash, others = new List<string>());

                if (!others.Contains(name))
                    others.Add(name);
            }

            return false;
        }

        public void AddRange(IEnumerable<string> names)
        {
            foreach (string name in names)
                Add(name);
        }

        // One name per line, like the fields.txt of SDBrowser
        public void AddFromFile(string path)
        {
            foreach (string line in File.ReadLines(path))
            {
                if (line.Length != 0)
                    Add(line);
            }
        }

        public bool TryGetName(uint hash, out string name) => names.TryGetValue(hash, out name);

        // The name, or the hash as hex if it's unknown
        public string GetName(uint hash) => names.TryGetValue(hash, out string name) ? name : FormatHash(hash);

        // Every name added for the hash, the one GetName returns first
        public IReadOnlyList<string> GetCandidates(uint hash)
        {
            if (!names.TryGetValue(hash, out string name))
                return Array.Empty<string>();

            if (!collisions.TryGetValue(hash, out List<string> others))
                return new[] { name };

            List<string> all = new List<string>(others.Count + 1) { name };
            all.AddRange(others);
            return all;
        }

        public static string FormatHash(uint hash) => "0x" + hash.ToString("X8");
    }
}
