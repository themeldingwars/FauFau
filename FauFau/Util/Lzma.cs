using SharpCompress.Compressors.LZMA;
using System;
using System.IO;

namespace FauFau.Util
{
    // Raw LZMA streams without a header, as Firefall stores them next to their 5 property bytes
    internal static class Lzma
    {
        public static byte[] Decompress(byte[] properties, byte[] data, int offset, int count, int size)
        {
            byte[] output = new byte[size];
            using Stream lzma = LzmaStream.Create(properties, new MemoryStream(data, offset, count, false), -1, size, false);
            ReadAll(lzma, output);
            return output;
        }

        // Returns how many bytes the stream used, for formats that put streams back to back without lengths
        public static int DecompressInto(byte[] properties, byte[] data, int offset, int count, Span<byte> output)
        {
            // The decoder buffers its input, so it gets one byte per read and doesn't take more than the stream
            ByteByByteStream input = new ByteByByteStream(new MemoryStream(data, offset, count, false));
            using Stream lzma = LzmaStream.Create(properties, input, -1, output.Length, false);
            ReadAll(lzma, output);
            return (int)input.Used;
        }

        private static void ReadAll(Stream stream, Span<byte> output)
        {
            int read = stream.ReadAtLeast(output, output.Length, false);
            if (read != output.Length)
            {
                throw new InvalidDataException($"LZMA data is cut off, got {read} of {output.Length} bytes");
            }
        }

        private class ByteByByteStream : Stream
        {
            private readonly Stream source;

            public long Used { get; private set; }

            public ByteByByteStream(Stream source)
            {
                this.source = source;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (count == 0)
                {
                    return 0;
                }
                int read = source.Read(buffer, offset, 1);
                Used += read;
                return read;
            }

            public override int ReadByte()
            {
                int value = source.ReadByte();
                if (value >= 0)
                {
                    Used++;
                }
                return value;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => Used; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
