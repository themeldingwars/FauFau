using System;
using System.Collections.Generic;
using System.IO;
using Bitter;
using FauFau.Util;
using SharpCompress;
using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;

namespace FauFau.Formats
{
    public class Czi : BinaryWrapper
    {
        public Header                Head;
        public List<MipInfo>         MipInfos         = new ();
        public List<CompressedBlock> CompressedBlocks = new ();

        public Czi() { }
        public Czi(string filePath)
        {
            Load(filePath);
        }

        public static Czi CreateMaskCzi(int width, int height)
        {
            var czi = new Czi();
            czi.Head = new Header
            {
                Magic   = "CZIM",
                Version = 3,
                Width = width,
                Height = height,
                Unk = 0,
                PatternFlags = 0,
                NumMipLevels = 0
            };
            
            return czi;
        }

        public void Load(string filePath)
        {
            var       data = File.ReadAllBytes(filePath);
            using var bs   = new BinaryStream(new MemoryStream(data));
            
            Read(bs);
        }

        public void Save(string path)
        {
            using var fs = new FileStream(path, FileMode.Create);
            Write(fs);
        }

        public override void Read(BinaryStream bs)
        {
            Head = bs.Read.Type<Header>();
            MipInfos = bs.Read.TypeList<MipInfo>(Head.NumMipLevels);
            CompressedBlocks = bs.Read.TypeList<CompressedBlock>(Head.NumMipLevels);
        }

        public override void Write(BinaryStream bs)
        {
            bs.Write.Type(Head);
            bs.Write.TypeList(MipInfos);
            bs.Write.TypeList(CompressedBlocks);
        }

        public byte[] GetMipDecompressed(int idx)
        {
            var mipInfo = MipInfos[idx];
            var compressedBlock = CompressedBlocks[idx];
            
            byte[] decompressedData = new byte[mipInfo.Size];
            var       msIn = new MemoryStream(compressedBlock.Data);
            using var zlib = new ZlibStream(msIn, CompressionMode.Decompress);
            zlib.ReadFully(decompressedData);
            
            return decompressedData;
        }

        public void AddMipLevel(ReadOnlySpan<byte> data)
        {
            var mipInfo = new MipInfo
            {
                Offset  = Head.NumMipLevels > 0 ? MipInfos[Head.NumMipLevels - 1].Offset + MipInfos[Head.NumMipLevels - 1].Size : 0,
                Size = data.Length
            };
            
            using var inMs  = new BinaryStream(new MemoryStream(data.ToArray()));
            using var outMs = new MemoryStream();
            using var outBs = new BinaryStream(outMs);
            outBs.Write.Byte(0x78);
            outBs.Write.Byte(0x9c);
            Common.Deflate(inMs, outBs, CompressionLevel.Default);
            outBs.ByteOrder = BinaryStream.Endianness.BigEndian;
            outBs.Write.UInt(Checksum.Adler32(data));
            outBs.ByteOrder = BinaryStream.Endianness.LittleEndian;
            outBs.Flush();

            var outData = outMs.ToArray();
            
            var block = new CompressedBlock()
            {
                Length = outData.Length,
                Data   = outData
            };
            
            MipInfos.Add(mipInfo);
            CompressedBlocks.Add(block);
            Head.NumMipLevels++;
        }

        public class Header : ReadWrite
        {
            public string Magic;
            public int    Version;
            public int    Width;
            public int    Height;
            public byte   PatternFlags;
            public byte   Unk;
            public int    NumMipLevels;
            
            public void Read(BinaryStream bs)
            {
                Magic = bs.Read.String(4);
                Version = bs.Read.Int();
                Width = bs.Read.Int();
                Height = bs.Read.Int();
                PatternFlags = bs.Read.Byte();
                Unk = bs.Read.Byte();
                NumMipLevels = bs.Read.Int();
            }
            
            public void Write(BinaryStream bs)
            {
                bs.Write.String(Magic);
                bs.Write.Int(Version);
                bs.Write.Int(Width);
                bs.Write.Int(Height);
                bs.Write.Byte(PatternFlags);
                bs.Write.Byte(Unk);
                bs.Write.Int(NumMipLevels);
            }
        }

        public class MipInfo : ReadWrite
        {
            public int Offset; // The combined size of all the previous mip infos added
            public int Size;
            
            public void Read(BinaryStream bs)
            {
                Offset = bs.Read.Int();
                Size = bs.Read.Int();
            }
            
            public void Write(BinaryStream bs)
            {
                bs.Write.Int(Offset);
                bs.Write.Int(Size);
            }
        }

        public class CompressedBlock : ReadWrite
        {
            public int Length;
            public byte[] Data;

            public void Read(BinaryStream bs)
            {
                Length = bs.Read.Int();
                Data = bs.Read.ByteArray(Length);
            }
            
            public void Write(BinaryStream bs)
            {
                bs.Write.Int(Length);
                bs.Write.ByteArray(Data);
            }
        }
    }
}
