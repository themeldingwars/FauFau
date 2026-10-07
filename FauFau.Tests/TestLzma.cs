using System.IO;
using SharpCompress.Compressors.LZMA;

namespace FauFau.Tests
{
    public static class TestLzma
    {
        public static (byte[] Properties, byte[] Stream) Compress(byte[] data)
        {
            MemoryStream output = new MemoryStream();
            byte[] properties;
            using (LzmaStream lzma = LzmaStream.Create(new LzmaEncoderProperties(), false, output))
            {
                lzma.Write(data, 0, data.Length);
                properties = lzma.Properties;
            }
            return (properties, output.ToArray());
        }
    }
}
