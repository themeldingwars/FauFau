using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace FauFau.Formats
{
    // Strings of dblocalization::LocalizedText can start with a parameter header: a count, then per parameter its index and a
    // big endian 16 bit offset into the text after the header, where the parameter goes. The offsets count UTF-8 bytes,
    // except in a few Korean strings of 1962 that count characters.
    public static class LocalizedText
    {
        private const int ParameterSize = 3;

        // Puts the parameters back into the text as {index}, a string without header comes back as it is
        public static string Decode(ReadOnlySpan<byte> value)
        {
            if (!value.IsEmpty && value[^1] == 0)
                value = value.Slice(0, value.Length - 1);

            if (value.IsEmpty || value[0] == 0 || value[0] >= 32)
                return Encoding.UTF8.GetString(value);

            int count = value[0];
            int headerLength = 1 + count * ParameterSize;
            if (value.Length < headerLength)
                return StripControl(Encoding.UTF8.GetString(value.Slice(1)));

            ReadOnlySpan<byte> header = value.Slice(1, count * ParameterSize);
            ReadOnlySpan<byte> text = value.Slice(headerLength);

            // Sorted by offset, so the text is copied in one pass
            Span<(int Offset, int Index)> parameters = count <= 32 ? stackalloc (int, int)[count] : new (int, int)[count];
            bool byteOffsets = true;
            for (int i = 0; i < count; i++)
            {
                int offset = BinaryPrimitives.ReadUInt16BigEndian(header.Slice(i * ParameterSize + 1));
                parameters[i] = (offset, header[i * ParameterSize]);

                // A byte offset never points into a character, the Korean strings with character offsets do
                byteOffsets &= offset == text.Length || (offset < text.Length && (text[offset] & 0xC0) != 0x80);
            }
            parameters.Sort();

            string decoded = byteOffsets ? null : Encoding.UTF8.GetString(text);
            int length = byteOffsets ? text.Length : decoded.Length;
            if (parameters[^1].Offset > length)
                return decoded ?? Encoding.UTF8.GetString(text);

            // UTF-8 has at least one byte per char, and a parameter takes at most five chars like {255}
            int capacity = text.Length + count * 5;
            char[] rented = null;
            Span<char> result = capacity <= 512 ? stackalloc char[512] : (rented = ArrayPool<char>.Shared.Rent(capacity));
            int written = 0;
            int position = 0;
            foreach ((int offset, int index) in parameters)
            {
                written += Copy(text, decoded, position, offset - position, result.Slice(written));
                result[written++] = '{';
                index.TryFormat(result.Slice(written), out int digits);
                written += digits;
                result[written++] = '}';
                position = offset;
            }
            written += Copy(text, decoded, position, length - position, result.Slice(written));

            string formatted = new string(result.Slice(0, written));
            if (rented != null)
                ArrayPool<char>.Shared.Return(rented);

            return formatted;
        }

        // Byte offsets slice the UTF-8 text, character offsets the decoded string
        private static int Copy(ReadOnlySpan<byte> text, string decoded, int start, int length, Span<char> destination)
        {
            if (decoded == null)
                return Encoding.UTF8.GetChars(text.Slice(start, length), destination);

            decoded.AsSpan(start, length).CopyTo(destination);
            return length;
        }

        // For values that were decoded to a string already, like the cells of StaticDB. The header only survives that while
        // its bytes are below 0x80, which they are in 1962.
        public static string Decode(string value)
        {
            return value == null ? null : Decode(Encoding.UTF8.GetBytes(value));
        }

        private static string StripControl(string text)
        {
            StringBuilder result = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c >= 32 || c == '\n' || c == '\r' || c == '\t')
                    result.Append(c);
            }
            return result.ToString();
        }
    }
}
