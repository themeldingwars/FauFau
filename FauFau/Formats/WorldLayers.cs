using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace FauFau.Formats
{
    // Ids of the layers in zone and chunk files. A layer's meaning depends on its parent, MeldingPerimeter for example is
    // only a perimeter below Melding.
    public static class WorldLayerIds
    {
        // Zone, below ZoneRoot
        public const uint ZoneRoot = 0x30000;
        public const uint Skybox = 0x20000;
        public const uint DefaultEnvironment = 0x20100;
        public const uint Melding = 0x20200;
        public const uint Water = 0x20300;
        public const uint ChunkInfo = 0x20400;
        public const uint MeldingHeightMap = 0x20700;
        public const uint Path = 0x20800;
        public const uint WorldChunkImport = 0x20900;
        public const uint Bounds = 0x21000;
        public const uint PropEncounterNameRegistry = 0x21200;
        public const uint PropDoodads = 0x21300;
        public const uint PropDoodads2 = 0x21400;
        public const uint CameraSequence = 0x21500;
        public const uint TransferBounds = 0x21600;
        public const uint SubZoneRegion = 0x21700;

        // Below Melding
        public const uint MeldingPerimeter = 5;

        // Below ChunkInfo
        public const uint ChunkRange = 0x10000;
        public const uint ChunkRef2 = 0x10100;
        public const uint ChunkRef = 0x10101;

        // Below DefaultEnvironment
        public const uint PropEnvironment = 0x50001;

        // Chunk files, the shared data of a LOD and its sub chunks hold the layers below Lod and SubChunk
        public const uint ChunkRoot = 0x40000;
        public const uint Lod = 0x40001;
        public const uint SubChunk = 0x40002;
        public const uint StaticGeometryCollision = 0x40101;
        public const uint SubZoneGrid = 0x40102;
        public const uint MovementBlockerCollision = 0x40103;
        public const uint ChunkEncounterNameRegistry = 0x40104;
        public const uint WaterCollision = 0x40105;
        public const uint ChunkPropEncounterNameRegistry = 0x40204;
    }

    public sealed class ZoneBounds
    {
        public Vector3 Min;
        public Vector3 Max;

        public static ZoneBounds Read(ReadOnlySpan<byte> data)
        {
            LayerReader read = new LayerReader(data);
            return new ZoneBounds { Min = read.Vector3(), Max = read.Vector3() };
        }
    }

    public sealed class ZoneSkybox
    {
        public uint SkyboxRecordId;

        public static ZoneSkybox Read(ReadOnlySpan<byte> data)
        {
            LayerReader read = new LayerReader(data);
            return new ZoneSkybox { SkyboxRecordId = read.UInt() };
        }
    }

    // The chunk coordinates on one face of the planet cube that the zone covers
    public sealed class ZoneChunkRange
    {
        public uint CubeFace;
        public uint MinX;
        public uint MaxX;
        public uint MinY;
        public uint MaxY;

        public bool Contains(uint x, uint y) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;

        public static ZoneChunkRange Read(ReadOnlySpan<byte> data)
        {
            LayerReader read = new LayerReader(data);
            return new ZoneChunkRange { CubeFace = read.UInt(), MinX = read.UInt(), MaxX = read.UInt(), MinY = read.UInt(), MaxY = read.UInt() };
        }
    }

    // ChunkRef layers carry a chunk record id, ChunkRef2 layers only the coordinates
    public sealed class ZoneChunkRef
    {
        public uint X;
        public uint Y;
        public uint ChunkRecordId;

        public static ZoneChunkRef Read(ReadOnlySpan<byte> data)
        {
            LayerReader read = new LayerReader(data);
            return new ZoneChunkRef { X = read.UInt(), Y = read.UInt(), ChunkRecordId = data.Length >= 12 ? read.UInt() : 0 };
        }
    }

    public sealed class ZonePath
    {
        public uint CceId;
        public uint Unk1;
        public List<Step> Steps = new ();

        public struct Step
        {
            public Vector3 Position;
            public Vector4 Orientation;
            public byte[] Action;
        }

        public static ZonePath Read(ReadOnlySpan<byte> data)
        {
            LayerReader read = new LayerReader(data);
            ZonePath path = new ZonePath { CceId = read.UInt(), Unk1 = read.UInt() };
            uint count = read.UInt();
            for (uint i = 0; i < count; i++)
            {
                path.Steps.Add(new Step { Position = read.Vector3(), Orientation = read.Vector4(), Action = read.Bytes((int)read.UInt()) });
            }
            return path;
        }
    }

    public sealed class MeldingPerimeter
    {
        public string Name;
        public uint ControlPoints;
        public uint BitfieldLength;
        public byte[] Bitfield;
        public uint Unk1;
        public List<string> Perimeters = new ();

        // Not every perimeter has the byte at the end
        public byte? Unk2;

        public static MeldingPerimeter Read(ReadOnlySpan<byte> data)
        {
            LayerReader read = new LayerReader(data);
            MeldingPerimeter perimeter = new MeldingPerimeter { Name = read.String(), ControlPoints = read.UInt(), BitfieldLength = read.UInt() };
            perimeter.Bitfield = read.Bytes((int)((perimeter.BitfieldLength + 7) / 8));
            perimeter.Unk1 = read.UInt();

            uint count = read.UInt();
            for (uint i = 0; i < count; i++)
            {
                perimeter.Perimeters.Add(read.String());
            }

            if (read.Remaining > 0)
                perimeter.Unk2 = read.Byte();

            return perimeter;
        }
    }

    // A bitmap of the zone area that belongs to a sub zone, one bit per cell
    public sealed class SubZoneRegion
    {
        public uint RegionId;
        public Vector2 Origin;
        public uint Width;
        public uint Height;
        public float CellSize;
        public byte[] Bitmap;

        public static SubZoneRegion Read(ReadOnlySpan<byte> data)
        {
            LayerReader read = new LayerReader(data);
            SubZoneRegion region = new SubZoneRegion { RegionId = read.UInt(), Origin = new Vector2(read.Float(), read.Float()) };
            region.Width = read.UInt();
            region.Height = read.UInt();
            region.CellSize = read.Float();

            uint length = read.UInt();
            if (length != (region.Width * region.Height + 7) / 8)
                throw new InvalidDataException($"The bitmap of sub zone region {region.RegionId} has {length} bytes, not {(region.Width * region.Height + 7) / 8}");

            region.Bitmap = read.Bytes((int)length);
            return region;
        }
    }

    public sealed class EncounterNameRegistry
    {
        public string[] Names;

        public static EncounterNameRegistry Read(ReadOnlySpan<byte> data)
        {
            LayerReader read = new LayerReader(data);
            string[] names = new string[read.UInt()];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = read.String();
            }
            return new EncounterNameRegistry { Names = names };
        }
    }

    // The sub zone of each cell of a chunk
    public sealed class SubZoneGrid
    {
        public uint Unk1;
        public int GridSize;
        public uint[] SubZoneIds;
        public uint GridCount;
        public byte[] Grid;

        public static SubZoneGrid Read(ReadOnlySpan<byte> data)
        {
            LayerReader read = new LayerReader(data);
            SubZoneGrid grid = new SubZoneGrid { Unk1 = read.UInt(), GridSize = (int)read.UInt() };
            grid.SubZoneIds = new uint[read.UInt()];
            for (int i = 0; i < grid.SubZoneIds.Length; i++)
            {
                grid.SubZoneIds[i] = read.UInt();
            }
            grid.GridCount = read.UInt();
            grid.Grid = read.Bytes((int)(grid.GridCount * grid.GridSize * grid.GridSize));
            return grid;
        }
    }

    internal ref struct LayerReader
    {
        private readonly ReadOnlySpan<byte> data;
        private int position;

        public LayerReader(ReadOnlySpan<byte> data)
        {
            this.data = data;
            position = 0;
        }

        public int Remaining => data.Length - position;

        private ReadOnlySpan<byte> Take(int length)
        {
            if (length < 0 || length > Remaining)
                throw new InvalidDataException($"The layer ends {length - Remaining} bytes early");

            ReadOnlySpan<byte> span = data.Slice(position, length);
            position += length;
            return span;
        }

        public byte Byte() => Take(1)[0];
        public uint UInt() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public float Float() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
        public Vector3 Vector3() => new Vector3(Float(), Float(), Float());
        public Vector4 Vector4() => new Vector4(Float(), Float(), Float(), Float());
        public byte[] Bytes(int length) => Take(length).ToArray();

        // Length prefixed, without NUL
        public string String() => Encoding.UTF8.GetString(Take((int)UInt()));
    }
}
