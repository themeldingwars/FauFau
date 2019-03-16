// Copyleft freakbyte 2015, feel free to do whatever you want with this class.

namespace FauFau.Util
{
    public class MersenneTwister
    {
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
            mt = new uint[n + 1];
            mt[0] = seed & 0xffffffffU;
            for (mti = 1; mti < n; mti++)
            {
                mt[mti] = (0x6C078965U * (mt[mti - 1] ^ (mt[mti - 1] >> 30)) + mti);
                mt[mti] &= 0xffffffffU;
            }
        }
        public uint Next()
        {
            uint y = 0;
            uint[] mag01 = new uint[3];
            mag01[0] = 0x0;
            mag01[1] = 0x9908B0DF;
            mag01[2] = 0x3B9ACA00;

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
