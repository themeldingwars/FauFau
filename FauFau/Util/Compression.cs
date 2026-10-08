using System.IO;
using Bitter;
using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;

namespace FauFau.Util
{
    // Raw deflate and gzip between binary streams, reading from the source offset to its end unless start or length say otherwise
    public static class Compression
    {
        public static void Deflate(BinaryStream source, BinaryStream destination, CompressionLevel level = CompressionLevel.Default, int start = -1, int length = -1)
        {
            using (MemoryStream payload = new MemoryStream(ReadPayload(source, start, length)))
            using (MemoryStream deflated = new MemoryStream())
            using (DeflateStream ds = new DeflateStream(payload, CompressionMode.Compress, level))
            {
                ds.CopyTo(deflated);
                destination.Write.ByteArray(deflated.ToArray());
            }
        }

        // Without a target size, inflates until the end of the deflate stream
        public static void Inflate(BinaryStream source, BinaryStream destination, int targetSize = -1, int start = -1, int length = -1)
        {
            using (MemoryStream payload = new MemoryStream(ReadPayload(source, start, length)))
            using (DeflateStream ds = new DeflateStream(payload, CompressionMode.Decompress))
            {
                destination.Write.ByteArray(ReadAll(ds, targetSize));
            }
        }

        public static void Gzip(BinaryStream source, BinaryStream destination, CompressionLevel level = CompressionLevel.Default, int start = -1, int length = -1)
        {
            byte[] payload = ReadPayload(source, start, length);

            using (MemoryStream memory = new MemoryStream())
            {
                using (GZipStream gzip = new GZipStream(memory, CompressionMode.Compress, level, new SharpCompress.Readers.ReaderOptions()))
                {
                    gzip.Write(payload, 0, payload.Length);
                }
                destination.Write.ByteArray(memory.ToArray());
            }
        }

        public static void Gunzip(BinaryStream source, BinaryStream destination, int start = -1, int length = -1)
        {
            using (MemoryStream payload = new MemoryStream(ReadPayload(source, start, length)))
            using (GZipStream gzip = new GZipStream(payload, CompressionMode.Decompress, CompressionLevel.Default, new SharpCompress.Readers.ReaderOptions()))
            {
                destination.Write.ByteArray(ReadAll(gzip, -1));
            }
        }

        private static byte[] ReadPayload(BinaryStream source, int start, int length)
        {
            if (start > 0)
                source.ByteOffset = start;

            return source.Read.ByteArray(length > 0 ? length : (int)(source.Length - source.ByteOffset));
        }

        private static byte[] ReadAll(Stream stream, int size)
        {
            if (size >= 0)
            {
                byte[] data = new byte[size];
                stream.ReadAtLeast(data, size, false);
                return data;
            }

            using (MemoryStream output = new MemoryStream())
            {
                stream.CopyTo(output);
                return output.ToArray();
            }
        }
    }
}
