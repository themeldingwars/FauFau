using SharpCompress.Compressors.LZMA;
using System;
using System.IO;

namespace FauFau.Util
{
    // Raw LZMA streams without a header, as Firefall stores them next to their 5 property bytes
    internal static class Lzma
    {
        // Creating a decoder allocates its window and probability tables, so every thread keeps one around
        [ThreadStatic]
        private static Decoder decoder;

        public static byte[] Decompress(byte[] properties, byte[] data, int offset, int count, int size)
        {
            byte[] output = new byte[size];
            DecompressInto(properties, data, offset, count, output, 0, size);
            return output;
        }

        // Returns how many bytes the stream used, for formats that put streams back to back without lengths
        public static int DecompressInto(byte[] properties, byte[] data, int offset, int count, byte[] output, int outputOffset, int size)
        {
            decoder ??= new Decoder();
            decoder.SetDecoderProperties(properties);

            MemoryStream input = new MemoryStream(data, offset, count, false);
            MemoryStream target = new MemoryStream(output, outputOffset, size, true);
            decoder.Code(input, target, -1, size, null);
            if (target.Position != size)
            {
                throw new InvalidDataException($"LZMA data is cut off, got {target.Position} of {size} bytes");
            }
            return (int)input.Position;
        }
    }
}
