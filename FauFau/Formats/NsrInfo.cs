using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using Bitter;

namespace FauFau.Formats
{
    // The sections and a summary of the packets of a replay, read from a stream without keeping the packets in memory.
    // Cut off and broken replays still give whatever could be read.
    public sealed class NsrInfo
    {
        private const int MaxGzipLayers = 4;
        private const int MaxHeaderSize = 64 * 1024 * 1024;

        // Version 2 replays don't say where the packets start, the meta section is read from this much data
        private const int Version2HeaderSize = 4096;

        // Null if the sections couldn't be read, Error says why
        public Nsr.DescriptionSection Description { get; private set; }
        public Nsr.IndexSection Index { get; private set; }
        public Nsr.MetaSection Meta { get; private set; }

        public int Packets { get; private set; }
        public uint FirstPacketTime { get; private set; }
        public uint LastPacketTime { get; private set; }

        // Layers of gzip around the data, 1 for a normal replay, 0 for an unpacked one
        public int GzipLayers { get; private set; }

        // Bytes of unpacked data
        public long DataSize { get; private set; }

        // The gzip stream or the data ends in the middle of a section or packet
        public bool Truncated { get; private set; }
        public string Error { get; private set; }

        public static NsrInfo Read(string path, IncrementalHash hash = null)
        {
            using FileStream file = File.OpenRead(path);
            return Read(file, hash);
        }

        // Returns null if the stream isn't a replay. The hash gets every byte of the unpacked data, also of a broken replay,
        // so a packed and an unpacked copy hash the same.
        public static NsrInfo Read(Stream stream, IncrementalHash hash = null)
        {
            NsrInfo info = new NsrInfo();
            PeekStream data = new PeekStream(stream);
            byte[] head = data.Peek(4);
            while (head.Length >= 2 && head[0] == 0x1F && head[1] == 0x8B && info.GzipLayers < MaxGzipLayers)
            {
                info.GzipLayers++;
                try
                {
                    data = new PeekStream(new GZipStream(data, CompressionMode.Decompress));
                    head = data.Peek(4);
                }
                catch (InvalidDataException)
                {
                    return null;
                }
            }

            if (head.Length < 4 || head[0] != 'N' || head[1] != 'S' || head[2] != 'R' || head[3] != 'D')
                return null;

            Reader reader = new Reader(data, hash);
            try
            {
                info.ReadContent(reader);
            }
            catch (Exception e) when (e is InvalidDataException or IOException)
            {
                // A cut off gzip stream ends up here, the data before the cut is still counted
                info.Truncated = true;
                info.Error = e.Message;
                reader.Drain();
            }

            info.DataSize = reader.Position;
            return info;
        }

        private void ReadContent(Reader reader)
        {
            byte[] header = new byte[Nsr.DescriptionSection.Length];
            int length = reader.ReadFull(header.AsSpan(0, 8));
            int version = length == 8 ? BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4)) : 0;

            if (version == 2)
            {
                Array.Resize(ref header, Version2HeaderSize);
                length += reader.ReadFull(header.AsSpan(8));
            }
            else
            {
                length += reader.ReadFull(header.AsSpan(8));
                if (length == header.Length)
                {
                    int dataOffset = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(20));
                    if (dataOffset < header.Length || dataOffset > MaxHeaderSize)
                    {
                        Error = $"Unexpected data offset {dataOffset}";
                        reader.Drain();
                        return;
                    }

                    Array.Resize(ref header, dataOffset);
                    length += reader.ReadFull(header.AsSpan(length));
                }
            }

            if (!ReadSections(header, length, out int dataStart))
            {
                if (Truncated)
                    Error ??= "Sections cut off";
                reader.Drain();
                return;
            }

            // Version 2 read past the sections, those bytes are already packets
            reader.Unread(header.AsSpan(dataStart, length - dataStart));
            CountPackets(reader);
        }

        private bool ReadSections(byte[] header, int length, out int dataStart)
        {
            dataStart = 0;
            try
            {
                using BinaryStream bs = new BinaryStream(new MemoryStream(header, 0, length, false));
                dataStart = Nsr.ReadSections(bs, out Nsr.DescriptionSection description, out Nsr.IndexSection index, out Nsr.MetaSection meta);
                if (dataStart > length)
                    throw new EndOfStreamException();

                Description = description;
                Index = index;
                Meta = meta;
                return true;
            }
            catch (Exception e) when (e is InvalidDataException or NotSupportedException or EndOfStreamException or ArgumentException or IndexOutOfRangeException)
            {
                // Running out of data means the file is cut off, anything else that it's broken
                Truncated = length < header.Length || e is EndOfStreamException;
                Error = e.Message;
                return false;
            }
        }

        private void CountPackets(Reader reader)
        {
            Span<byte> header = stackalloc byte[Nsr.Packet.HeaderLength];
            while (true)
            {
                int length = reader.ReadFull(header);
                if (length == 0)
                    return;

                if (length < header.Length)
                {
                    Truncated = true;
                    Error = "Packet header cut off";
                    return;
                }

                uint time = BinaryPrimitives.ReadUInt32LittleEndian(header);
                int dataLength = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(4));
                if (reader.Skip(dataLength) < dataLength)
                {
                    Truncated = true;
                    Error = "Packet data cut off";
                    return;
                }

                if (Packets == 0)
                    FirstPacketTime = time;

                LastPacketTime = time;
                Packets++;
            }
        }

        // Reads the unpacked data through a small buffer, hashing every byte once
        private sealed class Reader
        {
            // Small reads, since GZipStream throws at the cut of a cut off file and the data of that read is lost
            private const int ReadSize = 4096;

            private readonly Stream stream;
            private readonly IncrementalHash hash;
            private readonly byte[] buffer = new byte[ReadSize];
            private int start;
            private int end;
            private byte[] pending = Array.Empty<byte>();
            private int pendingStart;

            public long Position { get; private set; }

            public Reader(Stream stream, IncrementalHash hash)
            {
                this.stream = stream;
                this.hash = hash;
            }

            // Hands bytes that were read and hashed already out again
            public void Unread(ReadOnlySpan<byte> data)
            {
                pending = data.ToArray();
                pendingStart = 0;
                Position -= data.Length;
            }

            private bool Fill()
            {
                start = 0;
                end = stream.Read(buffer, 0, buffer.Length);
                hash?.AppendData(buffer.AsSpan(0, end));
                return end > 0;
            }

            public int ReadFull(Span<byte> destination)
            {
                int total = System.Math.Min(destination.Length, pending.Length - pendingStart);
                pending.AsSpan(pendingStart, total).CopyTo(destination);
                pendingStart += total;

                while (total < destination.Length)
                {
                    if (start == end && !Fill())
                        break;

                    int count = System.Math.Min(destination.Length - total, end - start);
                    buffer.AsSpan(start, count).CopyTo(destination.Slice(total));
                    start += count;
                    total += count;
                }

                Position += total;
                return total;
            }

            public long Skip(long count)
            {
                long total = System.Math.Min(count, pending.Length - pendingStart);
                pendingStart += (int)total;

                while (total < count)
                {
                    if (start == end && !Fill())
                        break;

                    int skipped = (int)System.Math.Min(count - total, end - start);
                    start += skipped;
                    total += skipped;
                }

                Position += total;
                return total;
            }

            public void Drain()
            {
                try
                {
                    while (Skip(long.MaxValue) > 0)
                    {
                    }
                }
                catch (Exception e) when (e is InvalidDataException or IOException)
                {
                }
            }
        }

        // Lets the magic of a stream that can't seek be looked at before it's read
        private sealed class PeekStream : Stream
        {
            private readonly Stream inner;
            private byte[] peeked = Array.Empty<byte>();
            private int peekPosition;

            public PeekStream(Stream inner)
            {
                this.inner = inner;
            }

            public byte[] Peek(int count)
            {
                byte[] buffer = new byte[count];
                int total = 0;
                while (total < count)
                {
                    int read = inner.Read(buffer, total, count - total);
                    if (read == 0)
                        break;

                    total += read;
                }

                peeked = buffer.AsSpan(0, total).ToArray();
                peekPosition = 0;
                return peeked;
            }

            public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

            public override int Read(Span<byte> buffer)
            {
                if (peekPosition < peeked.Length)
                {
                    int count = System.Math.Min(buffer.Length, peeked.Length - peekPosition);
                    peeked.AsSpan(peekPosition, count).CopyTo(buffer);
                    peekPosition += count;
                    return count;
                }
                return inner.Read(buffer);
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
