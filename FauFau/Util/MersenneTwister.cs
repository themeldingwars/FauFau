// Copyleft freakbyte 2015, feel free to do whatever you want with this class.

using System;
using System.Buffers.Binary;

namespace FauFau.Util
{
    public class MersenneTwister
    {
        private static readonly uint[] mag01 = [0x0, 0x9908B0DF, 0x3B9ACA00];
        
        private uint n = 624;
        private uint m = 397;
        private uint uMask = 0x80000000;
        private uint lMask = 0X7FFFFFFF;

        private uint[] mt;
        private uint mti;

        public MersenneTwister(uint seed = 5489)
        {
            Init(seed);
        }

        private void Init(uint seed = 5489)
        {
            mt ??= new uint[n + 1]; // Allocate only once
            mt[0] = seed & 0xffffffffU;
            for (mti = 1; mti < n; mti++)
            {
                mt[mti] = (0x6C078965U * (mt[mti - 1] ^ (mt[mti - 1] >> 30)) + mti);
                mt[mti] &= 0xffffffffU;
            }
        }
        
        public void Reseed(uint seed = 5489)
        {
            Init(seed);
        }

        public uint Next()
        {
            uint y = 0;

            if (mti >= n)
            {
                uint kk;
                if (mti == n + 1)
                {
                    Init();
                }
                for (kk = 0; kk < n - m; kk++)
                {
                    y = (mt[kk] & uMask) | (mt[kk + 1] & lMask);
                    mt[kk] = mt[kk + m] ^ (y >> 1) ^ mag01[y & 0x1U];
                }
                for (; kk < n - 1; kk++)
                {
                    y = (mt[kk] & uMask) | (mt[kk + 1] & lMask);
                    mt[kk] = mt[kk - 227] ^ (y >> 1) ^ mag01[y & 0x1U];
                }
                y = (mt[n - 1] & uMask) | (mt[0] & lMask);
                mt[n - 1] = mt[m - 1] ^ (y >> 1) ^ mag01[y & 0x1U];
                mti = 0;
            }

            y = mt[mti++];

            uint y1 = y1 = ((((y >> 11) ^ y) & 0xFF3A58AD) << 7) ^ (y >> 11) ^ y;
            uint y2 = ((y1 & 0xFFFFDF8C) << 15) ^ y1 ^ ((((y1 & 0xFFFFDF8C) << 15) ^ y1) >> 18);

            return y2;
        }

        // XORs data with the outputs for the seed, one per little endian word and the low byte of one per remaining byte.
        // Short data only needs the first state words, which saves most of the work of a full reseed.
        public static void Xor(uint seed, Span<byte> data)
        {
            const int N = 624;
            const int M = 397;

            int words = data.Length >> 2;
            int outputs = words + (data.Length & 3);
            if (outputs == 0)
                return;

            if (outputs > N - M)
            {
                MersenneTwister mt = new MersenneTwister(seed);
                for (int i = 0; i < words; i++)
                    BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(i * 4), BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i * 4)) ^ mt.Next());
                for (int i = words * 4; i < data.Length; i++)
                    data[i] ^= (byte)mt.Next();
                return;
            }

            Span<uint> state = stackalloc uint[outputs + M];
            state[0] = seed;
            for (int i = 1; i < state.Length; i++)
                state[i] = 0x6C078965U * (state[i - 1] ^ (state[i - 1] >> 30)) + (uint)i;

            for (int k = 0; k < outputs; k++)
            {
                uint y = (state[k] & 0x80000000) | (state[k + 1] & 0x7FFFFFFF);
                uint value = Temper(state[k + M] ^ (y >> 1) ^ ((y & 1) != 0 ? 0x9908B0DFU : 0));
                if (k < words)
                    BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(k * 4), BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(k * 4)) ^ value);
                else
                    data[words * 4 + k - words] ^= (byte)value;
            }
        }

        private static uint Temper(uint y)
        {
            uint y1 = ((((y >> 11) ^ y) & 0xFF3A58AD) << 7) ^ (y >> 11) ^ y;
            return ((y1 & 0xFFFFDF8C) << 15) ^ y1 ^ ((((y1 & 0xFFFFDF8C) << 15) ^ y1) >> 18);
        }

        public uint[] Next(uint n)
        {
            uint[] ret = new uint[n];
            for (uint i = 0; i < n; i++)
            {
                ret[i] = Next();
            }
            return ret;
        }
    }
}
