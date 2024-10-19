using System;
using System.Buffers;
using System.Collections.Generic;
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
            unchecked
            {
                uint hash = 0x811C9DC5U;
                for (var i = 0; i < array.Length; i++)
                {
                    hash = 0x1000193U * (hash ^ array[i]);
                }
                hash = 9U * (8193U * hash ^ ((8193U * hash) >> 7));
                return 33U * (hash ^ (hash >> 17));
            }
        }
        
        public static uint Adler32(ReadOnlySpan<byte> data)
        {
            const int mod = 65521;
            uint      a   = 1, b = 0;
            foreach (var c in data) {
                a = (a + c) % mod;
                b = (b + a) % mod;
            }
            return (b << 16) | a;
        }
    }
}
