using System;
using System.IO;

namespace FauFau.Util
{
    // FastLZ level 1 and 2 decompression, the virtual texture index uses it for its tile tables
    public static class FastLz
    {
        public static byte[] Decompress(ReadOnlySpan<byte> source, int sizeHint = 0)
        {
            byte[] output = new byte[System.Math.Max(sizeHint, source.Length * 4)];
            int length;
            while ((length = TryDecompress(source, output)) < 0)
            {
                output = new byte[output.Length * 2];
            }

            if (length != output.Length)
            {
                Array.Resize(ref output, length);
            }
            return output;
        }

        // Throws unless the data decodes to exactly the length of destination
        public static void Decompress(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            int length = TryDecompress(source, destination);
            if (length != destination.Length)
            {
                throw new InvalidDataException($"FastLZ data doesn't decode to the expected {destination.Length} bytes");
            }
        }

        // Returns the decoded length, or -1 when the output is too small
        private static int TryDecompress(ReadOnlySpan<byte> source, Span<byte> output)
        {
            if (source.IsEmpty)
            {
                return 0;
            }

            // The upper 3 bits of the first byte are the level
            int level = (source[0] >> 5) + 1;
            if (level != 1 && level != 2)
            {
                throw new InvalidDataException($"FastLZ level {level} isn't supported");
            }

            int length = 0;
            int position = 0;
            int control = source[position++] & 31;
            while (true)
            {
                if (control < 32)
                {
                    int literals = control + 1;
                    Require(source, position, literals);
                    if (length + literals > output.Length)
                    {
                        return -1;
                    }
                    source.Slice(position, literals).CopyTo(output.Slice(length));
                    position += literals;
                    length += literals;
                }
                else
                {
                    int matchLength = (control >> 5) - 1;
                    int distance = (control & 31) << 8;
                    if (matchLength == 6)
                    {
                        if (level == 1)
                        {
                            Require(source, position, 1);
                            matchLength += source[position++];
                        }
                        else
                        {
                            byte extra;
                            do
                            {
                                Require(source, position, 1);
                                extra = source[position++];
                                matchLength += extra;
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

                    int reference = length - distance - 1;
                    if (reference < 0)
                    {
                        throw new InvalidDataException("FastLZ match points before the start of the data");
                    }

                    matchLength += 3;
                    if (length + matchLength > output.Length)
                    {
                        return -1;
                    }

                    // A match can overlap the bytes it produces, those have to be copied byte by byte
                    if (reference + matchLength <= length)
                    {
                        output.Slice(reference, matchLength).CopyTo(output.Slice(length));
                    }
                    else
                    {
                        for (int i = 0; i < matchLength; i++)
                        {
                            output[length + i] = output[reference + i];
                        }
                    }
                    length += matchLength;
                }

                if (position >= source.Length)
                {
                    return length;
                }
                control = source[position++];
            }
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
