using System;
using System.IO;

namespace FauFau.Util
{
    // FastLZ level 1 and 2 decompression, the virtual texture index uses it for its tile tables
    public static class FastLz
    {
        public static byte[] Decompress(ReadOnlySpan<byte> source)
        {
            if (source.IsEmpty)
            {
                return Array.Empty<byte>();
            }

            // The upper 3 bits of the first byte are the level
            int level = (source[0] >> 5) + 1;
            if (level != 1 && level != 2)
            {
                throw new InvalidDataException($"FastLZ level {level} isn't supported");
            }

            MemoryStream output = new MemoryStream(source.Length * 4);
            int position = 0;
            int control = source[position++] & 31;
            while (true)
            {
                if (control < 32)
                {
                    int literals = control + 1;
                    Require(source, position, literals);
                    output.Write(source.Slice(position, literals));
                    position += literals;
                }
                else
                {
                    int length = (control >> 5) - 1;
                    int distance = (control & 31) << 8;
                    if (length == 6)
                    {
                        if (level == 1)
                        {
                            Require(source, position, 1);
                            length += source[position++];
                        }
                        else
                        {
                            byte extra;
                            do
                            {
                                Require(source, position, 1);
                                extra = source[position++];
                                length += extra;
                            } while (extra == 255);
                        }
                    }

                    Require(source, position, 1);
                    byte low = source[position++];
                    distance += low;
                    if (level == 2 && low == 255 && distance == (31 << 8) + 255)
                    {
                        Require(source, position, 2);
                        distance = ((source[position] << 8) | source[position + 1]) + 8191;
                        position += 2;
                    }

                    long reference = output.Length - distance - 1;
                    if (reference < 0)
                    {
                        throw new InvalidDataException("FastLZ match points before the start of the data");
                    }

                    // Matches can overlap the bytes they produce
                    length += 3;
                    byte[] buffer = output.GetBuffer();
                    for (int i = 0; i < length; i++)
                    {
                        if (output.Length == buffer.Length)
                        {
                            output.Capacity = buffer.Length * 2;
                            buffer = output.GetBuffer();
                        }
                        output.WriteByte(buffer[reference + i]);
                    }
                }

                if (position >= source.Length)
                {
                    break;
                }
                control = source[position++];
            }

            return output.ToArray();
        }

        private static void Require(ReadOnlySpan<byte> source, int position, int count)
        {
            if (source.Length - position < count)
            {
                throw new InvalidDataException("FastLZ data is cut off");
            }
        }
    }
}
