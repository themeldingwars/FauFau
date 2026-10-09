using System;
using System.Text;

namespace FauFau.Util
{
    public static class Checksum
    {
        public static uint FFnv32(string str)
        {
            return FFnv32(Encoding.ASCII.GetBytes(str));
        }
        public static uint FFnv32(byte[] array)
        {
            return FFnv32((ReadOnlySpan<byte>)array);
        }

        public static uint FFnv32(ReadOnlySpan<byte> data)
        {
            unchecked
            {
                uint hash = 0x811C9DC5U;
                for (var i = 0; i < data.Length; i++)
                {
                    hash = 0x1000193U * (hash ^ data[i]);
                }
                hash = 9U * (8193U * hash ^ ((8193U * hash) >> 7));
                return 33U * (hash ^ (hash >> 17));
            }
        }

        public static uint Crc32(ReadOnlySpan<byte> data)
        {
            return System.IO.Hashing.Crc32.HashToUInt32(data);
        }
    }
}
