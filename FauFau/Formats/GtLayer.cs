using Bitter;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FauFau.Formats
{
    // A layer of the world formats (zones, chunks, world maps), either a container of child layers or a leaf with data
    public class GtLayer
    {
        public const ulong Marker = 0x12ED5A12ED5B12ED;

        public uint Id;
        public byte[] Data = Array.Empty<byte>();
        public List<GtLayer> Children = new ();

        public bool IsContainer => Children.Count > 0;

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

        // Reads a marked layer and every marked layer below it
        public static GtLayer Read(BinaryStream bs)
        {
            (uint id, uint length) = ReadHeader(bs);
            if (bs.Length - bs.ByteOffset < length)
            {
                throw new InvalidDataException($"Layer 0x{id:X} is cut off");
            }

            byte[] data = bs.Read.ByteArray((int)length);
            return FromData(id, data);
        }

        private static GtLayer FromData(uint id, byte[] data)
        {
            GtLayer layer = new GtLayer { Id = id };
            List<GtLayer> children = TryReadChildren(data);
            if (children != null)
            {
                layer.Children = children;
            }
            else
            {
                layer.Data = data;
            }
            return layer;
        }

        // Layers hold either data or children, it's a container when the data is nothing but marked layers
        private static List<GtLayer> TryReadChildren(byte[] data)
        {
            if (data.Length < 16 || BinaryPrimitives.ReadUInt64LittleEndian(data) != Marker)
            {
                return null;
            }

            List<(uint Id, int Start, int Length)> spans = new ();
            int position = 0;
            while (position < data.Length)
            {
                if (data.Length - position < 16 || BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(position)) != Marker)
                {
                    return null;
                }

                uint id = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position + 8));
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position + 12));
                position += 16;
                if (data.Length - position < length)
                {
                    return null;
                }

                spans.Add((id, position, (int)length));
                position += (int)length;
            }

            List<GtLayer> children = new (spans.Count);
            foreach ((uint id, int start, int length) in spans)
            {
                children.Add(FromData(id, data.AsSpan(start, length).ToArray()));
            }
            return children;
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
    }
}
