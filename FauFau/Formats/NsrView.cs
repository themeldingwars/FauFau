using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Bitter;

namespace FauFau.Formats
{
    // A read-only replay, the unpacked file stays in one pooled buffer instead of an array per packet
    // Packet data points into that buffer and is only valid until Dispose
    public sealed class NsrView : IDisposable, IReadOnlyCollection<NsrView.Packet>
    {
        public int GzipLayers { get; }

        // Set when the gzip stream or the last packet is cut off, the packets then hold everything before the cut
        public bool Truncated { get; }

        public Nsr.DescriptionSection Description { get; }
        public Nsr.IndexSection Index { get; }
        public Nsr.MetaSection Meta { get; }

        // Number of complete packets
        public int Count { get; }

        private readonly ArrayPool<byte> pool;
        private readonly int dataOffset;
        private readonly int dataEnd;
        private byte[] data;

        private NsrView(byte[] file, int length, ArrayPool<byte> pool, bool ownsFile)
        {
            this.pool = pool;
            byte[] unpacked = Nsr.Unpack(file, length, pool, out length, out int layers, out bool truncated);
            if (ownsFile && unpacked != file)
            {
                pool.Return(file);
            }
            else if (!ownsFile && unpacked == file)
            {
                // The packets point into the data, they'd change along with the caller's array
                unpacked = pool.Rent(length);
                file.AsSpan(0, length).CopyTo(unpacked);
            }
            data = unpacked;
            GzipLayers = layers;

            try
            {
                if (length == 0)
                    throw new InvalidDataException("The replay is empty");

                using (BinaryStream bs = new BinaryStream(new MemoryStream(data, 0, length, false)))
                {
                    dataOffset = Nsr.ReadSections(bs, out Nsr.DescriptionSection description, out Nsr.IndexSection index, out Nsr.MetaSection meta);
                    Description = description;
                    Index = index;
                    Meta = meta;
                }

                // Walk the headers once for the count and where the complete packets end
                int position = dataOffset;
                int count = 0;
                while (length - position >= Nsr.Packet.HeaderLength)
                {
                    int packetLength = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(position + 4));
                    if (length - position - Nsr.Packet.HeaderLength < packetLength)
                        break;

                    position += Nsr.Packet.HeaderLength + packetLength;
                    count++;
                }

                Count = count;
                dataEnd = position;
                Truncated = truncated || position != length;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        // Rents its buffers from the given pool, or the shared one
        public static NsrView Open(ReadOnlySpan<byte> file, ArrayPool<byte> pool = null)
        {
            pool ??= ArrayPool<byte>.Shared;
            byte[] buffer = pool.Rent(file.Length);
            file.CopyTo(buffer);
            return new NsrView(buffer, file.Length, pool, true);
        }

        // The array isn't changed or kept, an unpacked replay is copied
        public static NsrView Open(byte[] file, ArrayPool<byte> pool = null)
        {
            return new NsrView(file, file.Length, pool ?? ArrayPool<byte>.Shared, false);
        }

        public static NsrView Open(string path, ArrayPool<byte> pool = null)
        {
            pool ??= ArrayPool<byte>.Shared;
            using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
            if (stream.Length > Array.MaxLength)
                throw new InvalidDataException($"{path} is too big to be a replay");

            int length = (int)stream.Length;
            byte[] file = pool.Rent(length);
            try
            {
                stream.ReadExactly(file, 0, length);
            }
            catch
            {
                pool.Return(file);
                throw;
            }
            return new NsrView(file, length, pool, true);
        }

        public void Dispose()
        {
            byte[] buffer = Interlocked.Exchange(ref data, null);
            if (buffer != null)
            {
                pool.Return(buffer);
            }
        }

        private byte[] Data => data ?? throw new ObjectDisposedException(nameof(NsrView));

        public Enumerator GetEnumerator() => new Enumerator(this);

        IEnumerator<Packet> IEnumerable<Packet>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public readonly struct Packet
        {
            public uint TimeStamp { get; }
            public ushort MessageId { get; }
            public ReadOnlyMemory<byte> Data { get; }

            public int Length => Data.Length;

            internal Packet(uint timeStamp, ushort messageId, ReadOnlyMemory<byte> data)
            {
                TimeStamp = timeStamp;
                MessageId = messageId;
                Data = data;
            }

            // A copy that outlives the view
            public Nsr.Packet ToPacket()
            {
                return new Nsr.Packet { TimeStamp = TimeStamp, Length = (ushort)Data.Length, MessageId = MessageId, Data = Data.ToArray() };
            }
        }

        public struct Enumerator : IEnumerator<Packet>
        {
            private readonly NsrView view;
            private int position;

            internal Enumerator(NsrView view)
            {
                this.view = view;
                position = view.dataOffset;
                Current = default;
            }

            public Packet Current { get; private set; }

            object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                if (position >= view.dataEnd)
                    return false;

                byte[] buffer = view.Data;
                ReadOnlySpan<byte> header = buffer.AsSpan(position, Nsr.Packet.HeaderLength);
                int length = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(4));
                Current = new Packet(
                    BinaryPrimitives.ReadUInt32LittleEndian(header),
                    BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(6)),
                    new ReadOnlyMemory<byte>(buffer, position + Nsr.Packet.HeaderLength, length));
                position += Nsr.Packet.HeaderLength + length;
                return true;
            }

            public void Reset()
            {
                position = view.dataOffset;
                Current = default;
            }

            public void Dispose()
            {
            }
        }
    }
}
