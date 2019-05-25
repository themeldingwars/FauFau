using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;
using System.IO;
using Bitter;
using static Bitter.BinaryUtil;
using System;
using System.Runtime.Serialization;
using System.Text;
using System.Runtime.Serialization.Json;

namespace FauFau.Util
{
    public static class Common
    {

        public static void MTXor(uint seed, ref byte[] data)
        {
            MersenneTwister mt = new MersenneTwister(seed);
            uint l = (uint)data.Length;
            uint x = l >> 2;
            uint y = l & 3;
            byte[] xor = new byte[l];
            for (int i = 0; i < x; i++)
            {
                WriteToBufferLE(ref xor, mt.Next(), i * 4);
            }
            int z = (int)x * 4;
            for (uint i = 0; i < y; i++)
            {
                xor[z + i] = (byte)mt.Next();
            }
            for (int i = 0; i < l; i++)
            {
                data[i] ^= xor[i];
            }
            mt = null;
        }

        public static void MTXor(uint seed, BinaryStream source, BinaryStream destination, int start = -1, int length = -1)
        {
            MersenneTwister mt = new MersenneTwister(seed);
            if (start > 0) { source.ByteOffset = start; }
            uint l = (uint)(length > 0 ? length : (source.Length - source.ByteOffset));
            uint x = l >> 2;
            uint y = l & 3;

            byte[] data = source.Read.ByteArray((int)l);
            byte[] xor = new byte[l];

            for (int i = 0; i < x; i++)
            {
                WriteToBufferLE(ref xor, mt.Next(), i * 4);
            }
            int z = (int)x * 4;
            for (uint i = 0; i < y; i++)
            {
                xor[z + i] = (byte)mt.Next();
            }
            for (int i = 0; i < l; i++)
            {
                data[i] ^= xor[i];
            }
            destination.Write.ByteArray(data);
            mt = null;
        }

        public static void MTXorOldest(uint seed, BinaryStream source, BinaryStream destination, int start = -1, int length = -1)
        {
            MersenneTwister mt = new MersenneTwister(seed);
            if (start > 0) { source.ByteOffset = start; }
            uint l = (uint)(length > 0 ? length : (source.Length - source.ByteOffset));
            uint x = l >> 2;
            uint y = l & 3;

            for (uint i = 0; i < x; i++)
            {
                destination.Write.UInt(source.Read.UInt() ^ mt.Next());
            }
            for (uint i = 0; i < y; i++)
            {
                destination.Write.Byte((byte)(source.Read.Byte() ^ (byte)mt.Next()));
            }
            mt = null;
        }

        public static void Inflate(byte[] source, ref byte[] destination, CompressionLevel level = CompressionLevel.Default, int targetSize = -1, int sourceStart = -1, int length = -1, int destinationStart = 0)
        {
            MemoryStream ms = new MemoryStream(source);
            if (sourceStart > 0) { ms.Position = sourceStart; }
            DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress, level);

            ds.Read(destination, destinationStart, targetSize == -1 ? destination.Length : targetSize);
            ds.Dispose();
            ms.Dispose();
        }
        public static void Inflate(BinaryStream source, BinaryStream destination, int targetSize, CompressionLevel level = CompressionLevel.Default, int start = -1, int length = -1)
        {
            if (start > 0) { source.ByteOffset = start; }
            uint l = (uint)(length > 0 ? length : (source.Length - source.ByteOffset));



            using (MemoryStream payload = new MemoryStream(source.Read.ByteArray((int)l)))
            using (DeflateStream ds = new DeflateStream(payload, CompressionMode.Decompress, level))
            {
                byte[] tmp = new byte[targetSize];
                ds.Read(tmp, 0, targetSize);
                destination.Write.ByteArray(tmp);
            }
        }

        public static void UnGzipUnknownTargetSize(BinaryStream source, BinaryStream destination, CompressionLevel level = CompressionLevel.Default, int start = -1, int length = -1)
        {
            if (start > 0) { source.ByteOffset = start; }
            uint l = (uint)(length > 0 ? length : (source.Length - source.ByteOffset));

            using (MemoryStream payload = new MemoryStream(source.Read.ByteArray((int)l)))
            using (MemoryStream inflated = new MemoryStream())
            using (GZipStream ds = new GZipStream(payload, CompressionMode.Decompress, level))
            {
                ds.CopyTo(inflated);
                destination.Write.ByteArray(inflated.ToArray());
            }
        }

        public static void InflateUnknownTargetSize(BinaryStream source, BinaryStream destination, CompressionLevel level = CompressionLevel.Default, int start = -1, int length = -1)
        {
            if (start > 0) { source.ByteOffset = start; }
            uint l = (uint)(length > 0 ? length : (source.Length - source.ByteOffset));

            using (MemoryStream payload = new MemoryStream(source.Read.ByteArray((int)l)))
            using (MemoryStream inflated = new MemoryStream())
            using (DeflateStream ds = new DeflateStream(payload, CompressionMode.Decompress, level))
            {
                ds.CopyTo(inflated);
                destination.Write.ByteArray(inflated.ToArray());
            }
        }
        public static void Deflate(BinaryStream source, BinaryStream destination, CompressionLevel level = CompressionLevel.Default, int start = -1, int length = -1)
        {
            if (start > 0) { source.ByteOffset = start; }
            uint l = (uint)(length > 0 ? length : (source.Length - source.ByteOffset));

            using (MemoryStream payload = new MemoryStream(source.Read.ByteArray((int)l)))
            using (MemoryStream deflated = new MemoryStream())
            using (DeflateStream ds = new DeflateStream(payload, CompressionMode.Compress, level))
            {
                ds.CopyTo(deflated);
                destination.Write.ByteArray(deflated.ToArray());
            }
        }

        public static void Gzip(BinaryStream source, BinaryStream destination, CompressionLevel level = CompressionLevel.Default, int start = -1, int length = -1)
        {
            if (start > 0) { source.ByteOffset = start; }
            uint l = (uint)(length > 0 ? length : (source.Length - source.ByteOffset));

            byte[] payload = source.Read.ByteArray((int)l);

            using (MemoryStream memory = new MemoryStream())
            {
                using (GZipStream gzip = new GZipStream(memory, CompressionMode.Compress, level))
                {
                    gzip.Write(payload, 0, payload.Length);
                }
                destination.Write.ByteArray(memory.ToArray());
            }
        }


        [DataContract]
        public class JsonWrapper<T>
        {
            public static T FromString(string json)
            {
                return ReadJsonString<T>(json);
            }
            public static T Read(string file)
            {
                return ReadJsonFile<T>(file);
            }
            public static void Write(T language, string file)
            {
                WriteJsonFile(language, file);
            }
            public void Write(string file)
            {
                WriteJsonFile(this, file);
            }
        }

        public static T ReadJsonString<T>(string json)
        {
            using (MemoryStream ms = new MemoryStream(Encoding.Unicode.GetBytes(json)))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
                return (T)serializer.ReadObject(ms);
            }
        }

        public static T ReadJsonFile<T>(string file)
        {
            return ReadJsonString<T>(File.ReadAllText(file, Encoding.UTF7));
        }

        public static void WriteJsonFile<T>(this T serializeable, string file)
        {
            using (FileStream fs = new FileStream(file, FileMode.Create))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(serializeable.GetType());
                serializer.WriteObject(fs, serializeable);
            }
        }

        public static bool IsDebugging()
        {
            bool debugging = false;
            isDebugging(ref debugging);
            return debugging;
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private static void isDebugging(ref bool debugging)
        {
            debugging = true;
        }

        public static bool ExecuteAsAdmin(string arguments)
        {
            return Execute(System.Reflection.Assembly.GetEntryAssembly().Location, arguments, null, true);
        }
        public static bool Execute(string exe, string arguments = "", string workingDir = null, bool admin = false)
        {
            try
            {
                FileInfo info = new FileInfo(exe);

                if (!info.Exists)
                {
                    return false;
                }

                System.Diagnostics.Process proc = new System.Diagnostics.Process();
                proc.StartInfo.WorkingDirectory = workingDir != null ? workingDir : info.Directory.FullName;
                proc.StartInfo.FileName = info.FullName;
                proc.StartInfo.Arguments = arguments;
                proc.StartInfo.UseShellExecute = true;

                if (admin == true)
                {
                    proc.StartInfo.Verb = "runas";
                }

                proc.Start();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return false;
            }
        }


        public static void SynchronizedInvoke(this System.ComponentModel.ISynchronizeInvoke sync, Action action)
        {
            if (!sync.InvokeRequired)
            {
                action();
                return;
            }
            sync.Invoke(action, new object[] { });
        }
        public static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                foreach (string file in Directory.GetFiles(path))
                {
                    File.Delete(file);
                }
                foreach (string directory in Directory.GetDirectories(path))
                {
                    DeleteDirectory(directory);
                }
                Directory.Delete(path);
            }
        }

    }
}
