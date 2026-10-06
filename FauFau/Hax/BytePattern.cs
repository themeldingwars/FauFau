using System;
using System.Collections.Generic;
using System.Globalization;

namespace FauFau.Hax
{
    // A byte pattern like "8B ?? 4? 90", where ? matches any nibble
    public sealed class BytePattern
    {
        private readonly byte[] values;
        private readonly byte[] masks;
        private readonly int anchor;
        private readonly bool exact;

        public int Length => values.Length;

        public BytePattern(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty)
                throw new ArgumentException("A pattern needs at least one byte", nameof(bytes));

            values = bytes.ToArray();
            masks = new byte[values.Length];
            masks.AsSpan().Fill(0xFF);
            anchor = 0;
            exact = true;
        }

        private BytePattern(byte[] values, byte[] masks)
        {
            this.values = values;
            this.masks = masks;
            anchor = Array.IndexOf(masks, (byte)0xFF);
            exact = Array.TrueForAll(masks, mask => mask == 0xFF);
        }

        public static BytePattern Parse(string pattern)
        {
            string[] tokens = pattern.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                throw new FormatException("A pattern needs at least one byte");

            byte[] values = new byte[tokens.Length];
            byte[] masks = new byte[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i] == "?" ? "??" : tokens[i];
                if (token.Length != 2)
                    throw new FormatException($"'{tokens[i]}' isn't a byte, use two hex digits or ? for each nibble");

                for (int n = 0; n < 2; n++)
                {
                    if (token[n] == '?')
                        continue;

                    if (!byte.TryParse(token.AsSpan(n, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte nibble))
                        throw new FormatException($"'{tokens[i]}' isn't a byte, use two hex digits or ? for each nibble");

                    int shift = n == 0 ? 4 : 0;
                    values[i] |= (byte)(nibble << shift);
                    masks[i] |= (byte)(0xF << shift);
                }
            }

            return new BytePattern(values, masks);
        }

        public bool IsMatch(ReadOnlySpan<byte> data)
        {
            if (data.Length < values.Length)
                return false;

            for (int i = 0; i < values.Length; i++)
            {
                if ((data[i] & masks[i]) != values[i])
                    return false;
            }
            return true;
        }

        // Returns the offset of the first match at or after start, or -1
        public int Find(ReadOnlySpan<byte> data, int start = 0)
        {
            if (exact)
            {
                int found = data.Slice(start).IndexOf(values);
                return found < 0 ? -1 : start + found;
            }

            int last = data.Length - values.Length;
            int position = start;
            while (position <= last)
            {
                // Jump to the next candidate by searching for the first byte without wildcards
                int found = anchor < 0 ? 0 : data.Slice(position + anchor, last - position + 1).IndexOf(values[anchor]);
                if (found < 0)
                    return -1;

                position += found;
                if (IsMatch(data.Slice(position)))
                    return position;

                position++;
            }
            return -1;
        }

        // Returns every match, matches don't overlap
        public List<int> FindAll(ReadOnlySpan<byte> data)
        {
            List<int> offsets = new List<int>();
            int position = Find(data);
            while (position >= 0)
            {
                offsets.Add(position);
                position = Find(data, position + values.Length);
            }
            return offsets;
        }
    }
}
