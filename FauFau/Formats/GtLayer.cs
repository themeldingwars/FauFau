using Bitter;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FauFau.Formats
{
    // A layer of the world formats (zones, chunks, environments), either a container of child layers, a typed layer or raw data.
    // What a layer holds depends on its id and the id of its parent, see WorldLayerIds.
    public abstract class GtLayer
    {
        public const ulong Marker = 0x12ED5A12ED5B12ED;

        // The parent id of layers at the top of a file
        public const uint NoParent = uint.MaxValue;

        public uint Id;

        // Zones and chunks always write the marker, the client also reads layers without it
        public bool HasMarker = true;

        protected GtLayer(uint id)
        {
            Id = id;
        }

        // The data without the layer header, throws when it doesn't match the layer
        protected abstract void ReadData(ReadOnlySpan<byte> data);

        protected abstract void WriteData(System.IO.BinaryWriter writer);

        public byte[] GetData()
        {
            using MemoryStream stream = new MemoryStream();
            using (System.IO.BinaryWriter writer = new System.IO.BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                WriteData(writer);
            }
            return stream.ToArray();
        }

        // The layer with its header
        public byte[] ToArray()
        {
            byte[] data = GetData();
            int header = HasMarker ? 16 : 8;
            byte[] bytes = new byte[header + data.Length];
            if (HasMarker)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(bytes, Marker);
            }

            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(header - 8), Id);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(header - 4), (uint)data.Length);
            data.CopyTo(bytes, header);
            return bytes;
        }

        public void Write(BinaryStream bs)
        {
            bs.Write.ByteArray(ToArray());
        }

        // Layers back to back, like in the decompressed blocks of a chunk
        public static byte[] ToArray(IEnumerable<GtLayer> layers)
        {
            using MemoryStream stream = new MemoryStream();
            foreach (GtLayer layer in layers)
            {
                stream.Write(layer.ToArray());
            }
            return stream.ToArray();
        }

        // The 8 bytes are either the marker, followed by id and length, or already the id and length
        public static (uint Id, uint Length) ReadHeader(BinaryStream bs)
        {
            ulong first = bs.Read.ULong();
            if (first != Marker)
            {
                return ((uint)first, (uint)(first >> 32));
            }
            return (bs.Read.UInt(), bs.Read.UInt());
        }

        public static GtLayer Read(BinaryStream bs, uint parentId = NoParent)
        {
            long start = bs.ByteOffset;
            (uint id, uint length) = ReadHeader(bs);
            if (bs.Length - bs.ByteOffset < length)
            {
                throw new InvalidDataException($"Layer 0x{id:X} is cut off");
            }

            byte[] data = bs.Read.ByteArray((int)length);
            return Create(parentId, id, data, bs.ByteOffset - start - length == 16);
        }

        public static List<GtLayer> ReadList(ReadOnlySpan<byte> data, uint parentId = NoParent)
        {
            List<GtLayer> layers = new ();
            int position = 0;
            while (position < data.Length)
            {
                if (!TryReadHeader(data, position, out uint id, out int length, out int header))
                {
                    throw new InvalidDataException($"Layer header at {position} is cut off");
                }

                position += header;
                if (data.Length - position < length)
                {
                    throw new InvalidDataException($"Layer 0x{id:X} is cut off");
                }

                layers.Add(Create(parentId, id, data.Slice(position, length), header == 16));
                position += length;
            }
            return layers;
        }

        private static bool TryReadHeader(ReadOnlySpan<byte> data, int position, out uint id, out int length, out int header)
        {
            id = 0;
            length = 0;
            header = 8;
            if (data.Length - position < 8)
            {
                return false;
            }

            ulong first = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(position));
            if (first == Marker)
            {
                if (data.Length - position < 16)
                {
                    return false;
                }

                header = 16;
                first = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(position + 8));
            }

            id = (uint)first;
            length = (int)Math.Min(first >> 32, int.MaxValue);
            return true;
        }

        // A layer that doesn't parse as its type stays raw data, so it still writes back as it was
        private static GtLayer Create(uint parentId, uint id, ReadOnlySpan<byte> data, bool hasMarker)
        {
            GtLayer layer = WorldLayerIds.Create(parentId, id);
            if (layer != null)
            {
                try
                {
                    layer.ReadData(data);
                }
                catch (InvalidDataException)
                {
                    layer = null;
                }
            }

            layer ??= new GtDataLayer(id) { Data = data.ToArray() };
            layer.HasMarker = hasMarker;
            return layer;
        }
    }

    // A layer FauFau doesn't know, or whose data didn't match its type
    public sealed class GtDataLayer : GtLayer
    {
        public byte[] Data = Array.Empty<byte>();

        public GtDataLayer(uint id) : base(id)
        {
        }

        protected override void ReadData(ReadOnlySpan<byte> data)
        {
            Data = data.ToArray();
        }

        protected override void WriteData(System.IO.BinaryWriter writer)
        {
            writer.Write(Data);
        }
    }

    // A layer that holds nothing but child layers
    public sealed class GtContainerLayer : GtLayer
    {
        public List<GtLayer> Children = new ();

        public GtContainerLayer(uint id) : base(id)
        {
        }

        protected override void ReadData(ReadOnlySpan<byte> data)
        {
            Children = ReadList(data, Id);
        }

        protected override void WriteData(System.IO.BinaryWriter writer)
        {
            foreach (GtLayer child in Children)
            {
                writer.Write(child.ToArray());
            }
        }

        public GtLayer Find(uint id)
        {
            foreach (GtLayer child in Children)
            {
                if (child.Id == id)
                {
                    return child;
                }
            }
            return null;
        }

        public T Find<T>() where T : GtLayer
        {
            foreach (GtLayer child in Children)
            {
                if (child is T typed)
                {
                    return typed;
                }
            }
            return null;
        }

        public IEnumerable<GtLayer> FindAll(uint id)
        {
            foreach (GtLayer child in Children)
            {
                if (child.Id == id)
                {
                    yield return child;
                }
            }
        }

        public IEnumerable<T> FindAll<T>() where T : GtLayer
        {
            foreach (GtLayer child in Children)
            {
                if (child is T typed)
                {
                    yield return typed;
                }
            }
        }
    }
}
