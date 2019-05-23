using Bitter;
using static FauFau.Util.Common;
using FauFau.Util.CommmonDataTypes;
using SharpCompress.Compressors.Deflate;
using System;
using System.Collections.Generic;
using System.IO;

namespace FauFau.Formats
{
    public class WorldMap : BinaryWrapper
    {
        public string Magic = "GTNO";
        public List<uint> Version = new List<uint>();
        public uint Last;
        public uint Second;
        public List<Vector3> Pos = new List<Vector3>();
 
        public override void Read(BinaryStream bs)
        {
            Bitter.BinaryReader Read = bs.Read;

            Magic = Read.String(4);
            Version = Read.UIntList(3);
            Last = Read.UInt();
            Second = Read.UInt();
            Pos = Read.TypeList<Vector3>(3);

            Read.UInt(); // _25601
            Read.Byte(); // padding
            Read.UInt(); // reminder
            uint decompressedSize = Read.UInt();
            uint compressedSize = Read.UInt();
            ushort compressionLevel = Read.UShort();

            byte[] deflated = Read.ByteArray((int)compressedSize-2);
            byte[] inflated = new byte[decompressedSize];

            Inflate(deflated, ref inflated, CompressionLevel.Default, (int)decompressedSize, 0);

            File.WriteAllBytes("outx.pcx", inflated);

            //Inflate(bs, result, (int)decompressedSize, SharpCompress.Compressors.Deflate.CompressionLevel.BestCompression, 0, (int)compressedSize-2);


        }
        public override void Write(BinaryStream bs)
        {
            base.Write(bs);
        }
    }
}
