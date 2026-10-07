using Bitter;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace FauFau.Formats
{
    // Color zone index maps (.czi) and patterns (.czip) mark the areas of a texture players can recolor
    public class Czi : BinaryWrapper
    {
        public string Magic = "CZIM";
        public uint Version = 3;
        public uint Width;
        public uint Height;

        // Patterns come from the patterns tab of the DDS exporter and use 2 bits per pixel
        public bool IsPattern;
        public byte PatternUsage;

        // Smallest mip first, so the last one has the full size
        public List<byte[]> Mips = new ();

        public override void Read(BinaryStream bs)
        {
            Bitter.BinaryReader Read = bs.Read;

            Magic = Read.String(4);
            if (Magic != "CZIM")
            {
                throw new InvalidDataException($"Not a color zone index map, the magic is {Magic}");
            }
            Version = Read.UInt();
            if (Version < 1 || Version > 3)
            {
                throw new NotSupportedException($"Color zone index map version {Version} isn't supported, only 1 to 3");
            }

            Width = Read.UInt();
            Height = Read.UInt();
            IsPattern = Version >= 2 && Read.Byte() != 0;
            PatternUsage = Version >= 3 ? Read.Byte() : (byte)0;
            if (Version == 2 && IsPattern)
            {
                throw new NotSupportedException("Version 2 patterns aren't supported, the client rejects them too");
            }

            uint mipCount = Read.UInt();
            (uint Offset, uint Size)[] mipInfos = new (uint, uint)[mipCount];
            for (int i = 0; i < mipCount; i++)
            {
                mipInfos[i] = (Read.UInt(), Read.UInt());
            }

            Mips = new List<byte[]>((int)mipCount);
            foreach ((uint _, uint size) in mipInfos)
            {
                int compressedSize = Read.Int();
                byte[] compressed = Read.ByteArray(compressedSize);
                byte[] mip = new byte[size];
                using (ZLibStream zlib = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress))
                {
                    int read = zlib.ReadAtLeast(mip, mip.Length, false);
                    if (read != mip.Length)
                    {
                        throw new InvalidDataException($"A mip has {read} instead of {mip.Length} bytes");
                    }
                }
                Mips.Add(mip);
            }
        }

        public override void Write(BinaryStream bs)
        {
            Bitter.BinaryWriter Write = bs.Write;

            Write.String(Magic);
            Write.UInt(Version);
            Write.UInt(Width);
            Write.UInt(Height);
            if (Version >= 2)
            {
                Write.Byte((byte)(IsPattern ? 1 : 0));
            }
            if (Version >= 3)
            {
                Write.Byte(PatternUsage);
            }

            Write.UInt((uint)Mips.Count);
            uint offset = 0;
            foreach (byte[] mip in Mips)
            {
                Write.UInt(offset);
                Write.UInt((uint)mip.Length);
                offset += (uint)mip.Length;
            }

            foreach (byte[] mip in Mips)
            {
                MemoryStream compressed = new MemoryStream();
                using (ZLibStream zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
                {
                    zlib.Write(mip);
                }
                Write.Int((int)compressed.Length);
                Write.ByteArray(compressed.ToArray());
            }
        }
    }
}
