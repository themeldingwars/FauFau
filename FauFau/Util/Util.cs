using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;
using System.IO;
using Bitter;
using static Bitter.BinaryUtil;


namespace FauFau.Util
{
    public static class Util
    {

        public static void MTXor(uint seed, ref byte[] data)
        {
            MersenneTwister mt = new MersenneTwister(seed);
            uint l = (uint)data.Length;
            uint x = l >> 2;
            uint y = l & 3;
            byte[] xor = new byte[l];
            for (int i = 0; i < x; i++)
            {
                WriteToBufferLE(ref xor, mt.Next(), i * 4);
            }
            int z = (int)x * 4;
            for (uint i = 0; i < y; i++)
            {
                xor[z + i] = (byte)mt.Next();
            }
            for (int i = 0; i < l; i++)
            {
                data[i] ^= xor[i];
            }
            mt = null;
        }

        public static void MTXor(uint seed, BinaryStream source, BinaryStream destination, int start = -1, int length = -1)
        {
            MersenneTwister mt = new MersenneTwister(seed);
            if (start > 0) { source.ByteOffset = start; }
            uint l = (uint)(length > 0 ? length : (source.Length - source.ByteOffset));
            uint x = l >> 2;
            uint y = l & 3;

            byte[] data = source.Read.ByteArray((int)l);
            byte[] xor = new byte[l];

            for (int i = 0; i < x; i++)
            {
                WriteToBufferLE(ref xor, mt.Next(), i * 4);
            }
            int z = (int)x * 4;
            for (uint i = 0; i < y; i++)
            {
                xor[z + i] = (byte)mt.Next();
            }
            for (int i = 0; i < l; i++)
            {
                data[i] ^= xor[i];
            }
            destination.Write.ByteArray(data);
            mt = null;
        }

        public static void MTXorOldest(uint seed, BinaryStream source, BinaryStream destination, int start = -1, int length = -1)
        {
            MersenneTwister mt = new MersenneTwister(seed);
            if (start > 0) { source.ByteOffset = start; }
            uint l = (uint)(length > 0 ? length : (source.Length - source.ByteOffset));
            uint x = l >> 2;
            uint y = l & 3;

            for (uint i = 0; i < x; i++)
            {
                destination.Write.UInt(source.Read.UInt() ^ mt.Next());
            }
            for (uint i = 0; i < y; i++)
            {
                destination.Write.Byte((byte)(source.Read.Byte() ^ (byte)mt.Next()));
            }
            mt = null;
        }

        public static void Inflate(byte[] source, ref byte[] destination, CompressionLevel level = CompressionLevel.Default, int targetSize = -1, int sourceStart = -1, int length = -1, int destinationStart = 0)
        {
            MemoryStream ms = new MemoryStream(source);
            if (sourceStart > 0) { ms.Position = sourceStart; }
            DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress, level);

            ds.Read(destination, destinationStart, targetSize == -1 ? destination.Length : targetSize);
            ds.Dispose();
            ms.Dispose();
        }
        public static void Inflate(BinaryStream source, BinaryStream destination, int targetSize, CompressionLevel level = CompressionLevel.Default, int start = -1, int length = -1)
        {
            if (start > 0) { source.ByteOffset = start; }
            uint l = (uint)(length > 0 ? length : (source.Length - source.ByteOffset));



            using (MemoryStream payload = new MemoryStream(source.Read.ByteArray((int)l)))
            using (DeflateStream ds = new DeflateStream(payload, CompressionMode.Decompress, level))
            {
                byte[] tmp = new byte[targetSize];
                ds.Read(tmp, 0, targetSize);
                destination.Write.ByteArray(tmp);
            }
        }

        public static void InflateUnknownTargetSize(BinaryStream source, BinaryStream destination, CompressionLevel level = CompressionLevel.Default, int start = -1, int length = -1)
        {
            if (start > 0) { source.ByteOffset = start; }
            uint l = (uint)(length > 0 ? length : (source.Length - source.ByteOffset));

            using (MemoryStream payload = new MemoryStream(source.Read.ByteArray((int)l)))
            using (MemoryStream inflated = new MemoryStream())
            using (DeflateStream ds = new DeflateStream(payload, CompressionMode.Decompress, level))
            {
                ds.CopyTo(inflated);
                destination.Write.ByteArray(inflated.ToArray());
            }
        }
        public static void Deflate(BinaryStream source, BinaryStream destination, CompressionLevel level = CompressionLevel.Default, int start = -1, int length = -1)
        {
            if (start > 0) { source.ByteOffset = start; }
            uint l = (uint)(length > 0 ? length : (source.Length - source.ByteOffset));

            using (MemoryStream payload = new MemoryStream(source.Read.ByteArray((int)l)))
            using (MemoryStream deflated = new MemoryStream())
            using (DeflateStream ds = new DeflateStream(payload, CompressionMode.Compress, level))
            {
                ds.CopyTo(deflated);
                destination.Write.ByteArray(deflated.ToArray());
            }
        }


    }
}
