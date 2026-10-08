using System;

namespace FauFau.Util
{
    public static class Hex
    {
        public static string Encode(ReadOnlySpan<byte> input, bool upperCase = true)
        {
            string hex = Convert.ToHexString(input);
            return upperCase ? hex : hex.ToLowerInvariant();
        }

        public static bool TryEncode(ReadOnlySpan<byte> input, Span<char> output, bool upperCase = true)
        {
            if (output.Length < input.Length * 2)
                return false;

            ReadOnlySpan<char> values = upperCase ? "0123456789ABCDEF" : "0123456789abcdef";

            for (int i = 0; i < input.Length; i++)
            {
                output[i * 2]     = values[input[i] >> 4];
                output[i * 2 + 1] = values[input[i] & 0xF];
            }

            return true;
        }
    }
}
