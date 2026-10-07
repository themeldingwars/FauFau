using Bitter;
using FauFau.Util;
using System;
using System.Collections.Generic;
using System.IO;

namespace FauFau.Formats
{
    // The smallest mips of the textures (vt/lowmiptextures.pak), the client draws them until the full texture is streamed in
    public class LowMipTexturePak : BinaryWrapper
    {
        private const uint DdsHeaderSize = 124;
        private const uint DdsPitchFlag = 0x8;

        public string Magic = "LMTD";
        public List<Entry> Entries = new ();

        // The LZMA compressed mips of all entries, back to back
        public byte[] Data = Array.Empty<byte>();

        public override void Read(BinaryStream bs)
        {
            Bitter.BinaryReader Read = bs.Read;

            Magic = Read.String(4);
            if (Magic != "LMTD")
            {
                throw new InvalidDataException($"Not a low mip texture pak, the magic is {Magic}");
            }

            int indexSize = Read.Int();
            int compressedIndexSize = Read.Int();
            byte[] properties = Read.ByteArray(5);
            byte[] compressedIndex = Read.ByteArray(compressedIndexSize - 5);
            byte[] index = Lzma.Decompress(properties, compressedIndex, 0, compressedIndex.Length, indexSize);
            Data = Read.ByteArray((int)(bs.Length - bs.ByteOffset));

            BinaryStream indexStream = new BinaryStream(new MemoryStream(index));
            Bitter.BinaryReader ReadIndex = indexStream.Read;
            int count = ReadIndex.Int();
            Entries = new List<Entry>(count);
            for (int i = 0; i < count; i++)
            {
                Entry entry = new Entry
                {
                    Path = ReadNullTerminatedString(indexStream),
                    Flags = ReadIndex.UInt(),
                    Width = ReadIndex.UInt(),
                    Height = ReadIndex.UInt(),
                    Depth = ReadIndex.UInt(),
                    PitchOrLinearSize = ReadIndex.UInt(),
                    MipMapCount = ReadIndex.UInt(),
                    PixelFormat = ReadIndex.ByteArray(32),
                    Caps = ReadIndex.ByteArray(16),
                    FirstMip = ReadIndex.UInt(),
                    DataLength = ReadIndex.UInt(),
                    DataOffset = ReadIndex.UInt(),
                    CompressedLength = ReadIndex.UInt(),
                };
                if ((long)entry.DataOffset + entry.CompressedLength > Data.Length)
                {
                    throw new InvalidDataException($"The data of {entry.Path} is past the end of the pak");
                }
                Entries.Add(entry);
            }
        }

        private static string ReadNullTerminatedString(BinaryStream bs)
        {
            List<byte> bytes = new ();
            byte b;
            while ((b = bs.Read.Byte()) != 0)
            {
                bytes.Add(b);
            }
            return System.Text.Encoding.ASCII.GetString(bytes.ToArray());
        }

        // Only the stored mips, the first one is FirstMip
        public byte[] GetData(Entry entry)
        {
            byte[] properties = Data.AsSpan((int)entry.DataOffset, 5).ToArray();
            return Lzma.Decompress(properties, Data, (int)entry.DataOffset + 5, (int)entry.CompressedLength - 5, (int)entry.DataLength);
        }

        // Only has the stored mips, the larger ones aren't in the pak
        public byte[] CreateDds(Entry entry)
        {
            byte[] data = GetData(entry);
            int shift = (int)entry.FirstMip;
            uint pitchOrLinearSize = (entry.Flags & DdsPitchFlag) != 0
                ? entry.PitchOrLinearSize >> shift
                : entry.PitchOrLinearSize >> (2 * shift);

            BinaryStream bs = new BinaryStream(new MemoryStream());
            Bitter.BinaryWriter Write = bs.Write;
            Write.String("DDS ");
            Write.UInt(DdsHeaderSize);
            Write.UInt(entry.Flags);
            Write.UInt(System.Math.Max(1, entry.Height >> shift));
            Write.UInt(System.Math.Max(1, entry.Width >> shift));
            Write.UInt(System.Math.Max(1, pitchOrLinearSize));
            Write.UInt(entry.Depth);
            Write.UInt(entry.MipMapCount - entry.FirstMip);
            Write.ByteArray(new byte[11 * 4]);
            Write.ByteArray(entry.PixelFormat);
            Write.ByteArray(entry.Caps);
            Write.UInt(0);
            Write.ByteArray(data);

            bs.ByteOffset = 0;
            return bs.Read.ByteArray((int)bs.Length);
        }

        // The DDS header fields of the full texture, without the reserved ones
        public class Entry
        {
            // Like "00118000\00118090.dds"
            public string Path;
            public uint Flags;
            public uint Width;
            public uint Height;
            public uint Depth;
            public uint PitchOrLinearSize;
            public uint MipMapCount;
            public byte[] PixelFormat;
            public byte[] Caps;

            // The index of the largest stored mip
            public uint FirstMip;
            public uint DataLength;
            public uint DataOffset;
            public uint CompressedLength;

            public string FourCC => System.Text.Encoding.ASCII.GetString(PixelFormat, 8, 4).TrimEnd('\0');
        }
    }
}
