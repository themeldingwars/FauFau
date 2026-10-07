using Bitter;
using FauFau.Util;
using FauFau.Util.CommmonDataTypes;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FauFau.Formats
{
    // The index of the virtual geometry (vg/static.vgeo_idx), pages of up to 2048 vertices in vg/static.vgeo
    public class VGeoIndex : BinaryWrapper
    {
        public const int VerticesPerPage = 2048;

        public int Version = 3;

        // 1 when the pages are compressed
        public byte PageUsageBits = 1;
        public List<PageInfo> Pages = new ();

        // Hashes of texture paths, like the images of the virtual texture
        public List<uint[]> Textures = new ();
        public uint[] Version1Data = Array.Empty<uint>();

        public override void Read(BinaryStream bs)
        {
            Bitter.BinaryReader Read = bs.Read;

            Version = Read.Int();
            if (Version < 1 || Version > 3)
            {
                throw new NotSupportedException($"Virtual geometry index version {Version} isn't supported, only 1 to 3");
            }
            PageUsageBits = Version > 2 ? Read.Byte() : (byte)0;

            int pageCount = Read.Int();
            Pages = new List<PageInfo>(pageCount);
            for (int i = 0; i < pageCount; i++)
            {
                PageInfo page = new PageInfo
                {
                    Offset = Read.ULong(),
                    Crc = Read.UInt(),
                    VertexCount = Read.UShort(),
                };
                if (page.VertexCount > 0 && Version != 1)
                {
                    page.Size = Read.UShort();
                    page.UsedSlots = Read.ByteArray(VerticesPerPage / 8);
                }
                Pages.Add(page);
            }

            int textureCount = Read.Int();
            Textures = new List<uint[]>(textureCount);
            for (int i = 0; i < textureCount; i++)
            {
                Textures.Add(Read.UIntArray(3));
            }

            if (Version == 1)
            {
                Version1Data = Read.UIntArray(Read.Int());
            }
        }

        public VGeoPage ReadPage(Stream vgeo, PageInfo page)
        {
            if (page.VertexCount == 0)
            {
                return new VGeoPage { Stride = VGeoPage.FullStride, Data = Array.Empty<byte>(), UsedSlots = page.UsedSlots };
            }

            bool compressed = Version != 1 && PageUsageBits != 0;
            int size = compressed ? page.Size : page.VertexCount * VGeoPage.FullStride;
            byte[] data = new byte[size];
            vgeo.Position = (long)page.Offset;
            vgeo.ReadExactly(data);
            return compressed ? VGeoPage.Decode(data, page.VertexCount, page.UsedSlots) : new VGeoPage { Stride = VGeoPage.FullStride, Data = data, UsedSlots = page.UsedSlots };
        }

        public class PageInfo
        {
            public ulong Offset;

            // The CRC-32 of the decoded vertices
            public uint Crc;
            public ushort VertexCount;

            // In bytes, of the stored page with its stride byte
            public ushort Size;

            // One bit per vertex slot of the page, set for the slots the stored vertices fill in order
            public byte[] UsedSlots;
        }
    }

    public class VGeoPage
    {
        public const int FullStride = 27;
        public const int ShortStride = 23;

        // Each vertex starts with its position, the rest of the stride isn't known yet
        public int Stride;
        public byte[] Data;
        public byte[] UsedSlots;

        public int VertexCount => Stride == 0 ? 0 : Data.Length / Stride;

        public uint Crc => Checksum.Crc32(Data);

        // A byte for the stride, then 5 LZMA property bytes and the stream
        public static VGeoPage Decode(byte[] data, int vertexCount, byte[] usedSlots)
        {
            if (data.Length < 6)
            {
                throw new InvalidDataException("The page is cut off");
            }

            int stride = data[0] == 1 ? FullStride : ShortStride;
            byte[] properties = data.AsSpan(1, 5).ToArray();
            return new VGeoPage
            {
                Stride = stride,
                Data = Lzma.Decompress(properties, data, 6, data.Length - 6, vertexCount * stride),
                UsedSlots = usedSlots,
            };
        }

        public Vector3 GetPosition(int vertex)
        {
            ReadOnlySpan<byte> span = Data.AsSpan(vertex * Stride, 12);
            return new Vector3
            {
                x = BinaryPrimitives.ReadSingleLittleEndian(span),
                y = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(4)),
                z = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(8)),
            };
        }

        // The page slot of each stored vertex
        public int[] GetSlots()
        {
            if (UsedSlots == null)
            {
                int[] all = new int[VertexCount];
                for (int i = 0; i < all.Length; i++)
                {
                    all[i] = i;
                }
                return all;
            }

            List<int> slots = new (VertexCount);
            for (int slot = 0; slot < VGeoIndex.VerticesPerPage; slot++)
            {
                if ((UsedSlots[slot / 8] & (1 << (slot % 8))) != 0)
                {
                    slots.Add(slot);
                }
            }
            return slots.ToArray();
        }
    }
}
