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
        public const uint Environment10000 = 0x2710;
        public const uint PropEnvironment = 0x50001;

        // Chunk files, the shared data of a LOD and its sub chunks hold the layers below Lod and SubChunk
        public const uint ChunkRoot = 0x40000;
        public const uint Lod = 0x40001;
        public const uint SubChunk = 0x40002;
        public const uint Terrain = 0x40100;
        public const uint StaticGeometryCollision = 0x40101;
        public const uint SubZoneGrid = 0x40102;
        public const uint MovementBlockerCollision = 0x40103;
        public const uint ChunkEncounterNameRegistry = 0x40104;
        public const uint WaterCollision = 0x40105;
        public const uint Props = 0x40200;
        public const uint GeometryTree2 = 0x40203;
        public const uint ChunkPropEncounterNameRegistry = 0x40204;
        public const uint Vegetation = 0x40205;
        public const uint Overlay = 0x40206;
        public const uint Sectors = 0x40207;
        public const uint WaterObjects = 0x40208;
        public const uint Vegetation2 = 0x40209;
        public const uint GeometryTree = 0x40210;

        // Below Props and in camera sequences
        public const uint PropDoodad = 0x50000;
        public const uint PropLight = 0x50011;

        // The layer type for an id below a parent, null for the layers that stay raw data
        internal static GtLayer Create(uint parentId, uint id)
        {
            return (parentId, id) switch
            {
                (GtLayer.NoParent, ZoneRoot) => new GtContainerLayer(id),
                (GtLayer.NoParent, PropEnvironment) => new GtContainerLayer(id),
                (GtLayer.NoParent, ScZone.EnvironmentLayerId) => new GtContainerLayer(id),

                (ZoneRoot, Skybox) => new ZoneSkyboxLayer(),
                (ZoneRoot, DefaultEnvironment) => new GtContainerLayer(id),
                (ZoneRoot, Melding) => new GtContainerLayer(id),
                (ZoneRoot, ChunkInfo) => new GtContainerLayer(id),
                (ZoneRoot, Path) => new ZonePathLayer(),
                (ZoneRoot, Bounds) => new ZoneBoundsLayer(),
                (ZoneRoot, PropEncounterNameRegistry) => new EncounterNameRegistryLayer(id),
                (ZoneRoot, PropDoodads2) => new GtContainerLayer(id),
                (ZoneRoot, SubZoneRegion) => new SubZoneRegionLayer(),

                (Melding, MeldingPerimeter) => new MeldingPerimeterLayer(),

                (ChunkInfo, ChunkRange) => new ZoneChunkRangeLayer(),
                (ChunkInfo, ChunkRef) => new ZoneChunkRefLayer(id),
                (ChunkInfo, ChunkRef2) => new ZoneChunkRefLayer(id),

                (DefaultEnvironment, Environment10000) => new Environment10000Layer(),
                (DefaultEnvironment, PropEnvironment) => new GtContainerLayer(id),
                (ScZone.EnvironmentLayerId, PropEnvironment) => new GtContainerLayer(id),
                (Props, PropEnvironment) => new GtContainerLayer(id),

                (Lod or SubChunk, Terrain) => new GtContainerLayer(id),
                (Lod or SubChunk, SubZoneGrid) => new SubZoneGridLayer(),
                (Lod or SubChunk, ChunkEncounterNameRegistry) => new EncounterNameRegistryLayer(id),
                (Lod or SubChunk, Props) => new GtContainerLayer(id),
                (Lod or SubChunk, ChunkPropEncounterNameRegistry) => new EncounterNameRegistryLayer(id),
                (Lod or SubChunk, Sectors) => new GtContainerLayer(id),
                _ => null,
            };
        }
    }

    public sealed class ZoneBoundsLayer : GtLayer
    {
        public Vector3 Min;
        public Vector3 Max;

        public ZoneBoundsLayer() : base(WorldLayerIds.Bounds)
        {
        }

        protected override void ReadData(ReadOnlySpan<byte> data)
        {
            LayerReader read = new LayerReader(data);
            Min = read.Vector3();
            Max = read.Vector3();
            read.End();
        }

        protected override void WriteData(System.IO.BinaryWriter writer)
        {
            writer.Write(Min);
            writer.Write(Max);
        }
    }

    public sealed class ZoneSkyboxLayer : GtLayer
    {
        public uint SkyboxRecordId;

        public ZoneSkyboxLayer() : base(WorldLayerIds.Skybox)
        {
        }

        protected override void ReadData(ReadOnlySpan<byte> data)
        {
            LayerReader read = new LayerReader(data);
            SkyboxRecordId = read.UInt();
            read.End();
        }

        protected override void WriteData(System.IO.BinaryWriter writer)
        {
            writer.Write(SkyboxRecordId);
        }
    }

    // The chunk coordinates on one face of the planet cube that the zone covers
    public sealed class ZoneChunkRangeLayer : GtLayer
    {
        public uint CubeFace;
        public uint MinX;
        public uint MaxX;
        public uint MinY;
        public uint MaxY;

        public ZoneChunkRangeLayer() : base(WorldLayerIds.ChunkRange)
        {
        }

        public bool Contains(uint x, uint y) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;

        protected override void ReadData(ReadOnlySpan<byte> data)
        {
            LayerReader read = new LayerReader(data);
            CubeFace = read.UInt();
            MinX = read.UInt();
            MaxX = read.UInt();
            MinY = read.UInt();
            MaxY = read.UInt();
            read.End();
        }

        protected override void WriteData(System.IO.BinaryWriter writer)
        {
            writer.Write(CubeFace);
            writer.Write(MinX);
            writer.Write(MaxX);
            writer.Write(MinY);
            writer.Write(MaxY);
        }
    }

    // ChunkRef layers carry a chunk record id, ChunkRef2 layers only the coordinates
    public sealed class ZoneChunkRefLayer : GtLayer
    {
        public uint X;
        public uint Y;
        public uint ChunkRecordId;

        public ZoneChunkRefLayer(uint id = WorldLayerIds.ChunkRef) : base(id)
        {
        }

        public bool HasChunkRecordId => Id == WorldLayerIds.ChunkRef;

        protected override void ReadData(ReadOnlySpan<byte> data)
        {
            LayerReader read = new LayerReader(data);
            X = read.UInt();
            Y = read.UInt();
            ChunkRecordId = HasChunkRecordId ? read.UInt() : 0;
            read.End();
        }

        protected override void WriteData(System.IO.BinaryWriter writer)
        {
            writer.Write(X);
            writer.Write(Y);
            if (HasChunkRecordId)
                writer.Write(ChunkRecordId);
        }
    }

    public sealed class ZonePathLayer : GtLayer
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

        public ZonePathLayer() : base(WorldLayerIds.Path)
        {
        }

        protected override void ReadData(ReadOnlySpan<byte> data)
        {
            LayerReader read = new LayerReader(data);
            CceId = read.UInt();
            Unk1 = read.UInt();
            uint count = read.UInt();
            Steps = new List<Step>();
            for (uint i = 0; i < count; i++)
            {
                Steps.Add(new Step { Position = read.Vector3(), Orientation = read.Vector4(), Action = read.Bytes((int)read.UInt()) });
            }
            read.End();
        }

        protected override void WriteData(System.IO.BinaryWriter writer)
        {
            writer.Write(CceId);
            writer.Write(Unk1);
            writer.Write((uint)Steps.Count);
            foreach (Step step in Steps)
            {
                writer.Write(step.Position);
                writer.Write(step.Orientation);
                writer.Write((uint)(step.Action?.Length ?? 0));
                writer.Write(step.Action ?? Array.Empty<byte>());
            }
        }
    }

    public sealed class MeldingPerimeterLayer : GtLayer
    {
        public string Name;
        public uint ControlPoints;
        public uint BitfieldLength;
        public byte[] Bitfield;
        public uint Unk1;
        public List<string> Perimeters = new ();

        // Not every perimeter has the byte at the end, the 1962 zones also have two floats after it, 0 and 0 in most
        public byte? Unk2;
        public float? Unk3;
        public float? Unk4;

        public MeldingPerimeterLayer() : base(WorldLayerIds.MeldingPerimeter)
        {
        }

        protected override void ReadData(ReadOnlySpan<byte> data)
        {
            LayerReader read = new LayerReader(data);
            Name = read.String();
            ControlPoints = read.UInt();
            BitfieldLength = read.UInt();
            Bitfield = read.Bytes((int)((BitfieldLength + 7) / 8));
            Unk1 = read.UInt();

            uint count = read.UInt();
            Perimeters = new List<string>();
            for (uint i = 0; i < count; i++)
            {
                Perimeters.Add(read.String());
            }

            Unk2 = read.Remaining > 0 ? read.Byte() : null;
            Unk3 = read.Remaining > 0 ? read.Float() : null;
            Unk4 = read.Remaining > 0 ? read.Float() : null;
            read.End();
        }

        protected override void WriteData(System.IO.BinaryWriter writer)
        {
            writer.WriteLengthPrefixed(Name);
            writer.Write(ControlPoints);
            writer.Write(BitfieldLength);
            writer.Write(Bitfield);
            writer.Write(Unk1);
            writer.Write((uint)Perimeters.Count);
            foreach (string perimeter in Perimeters)
            {
                writer.WriteLengthPrefixed(perimeter);
            }

            if (Unk2.HasValue)
                writer.Write(Unk2.Value);

            if (Unk3.HasValue)
                writer.Write(Unk3.Value);

            if (Unk4.HasValue)
                writer.Write(Unk4.Value);
        }
    }

    // A bitmap of the zone area that belongs to a sub zone, one bit per cell
    public sealed class SubZoneRegionLayer : GtLayer
    {
        public uint RegionId;
        public Vector2 Origin;
        public uint Width;
        public uint Height;
        public float CellSize;
        public byte[] Bitmap;

        public SubZoneRegionLayer() : base(WorldLayerIds.SubZoneRegion)
        {
        }

        protected override void ReadData(ReadOnlySpan<byte> data)
        {
            LayerReader read = new LayerReader(data);
            RegionId = read.UInt();
            Origin = new Vector2(read.Float(), read.Float());
            Width = read.UInt();
            Height = read.UInt();
            CellSize = read.Float();

            uint length = read.UInt();
            if (length != (Width * Height + 7) / 8)
                throw new InvalidDataException($"The bitmap of sub zone region {RegionId} has {length} bytes, not {(Width * Height + 7) / 8}");

            Bitmap = read.Bytes((int)length);
            read.End();
        }

        protected override void WriteData(System.IO.BinaryWriter writer)
        {
            writer.Write(RegionId);
            writer.Write(Origin.X);
            writer.Write(Origin.Y);
            writer.Write(Width);
            writer.Write(Height);
            writer.Write(CellSize);
            writer.Write((uint)Bitmap.Length);
            writer.Write(Bitmap);
        }
    }

    // The encounter names of the props of a zone or chunk
    public sealed class EncounterNameRegistryLayer : GtLayer
    {
        public string[] Names = Array.Empty<string>();

        public EncounterNameRegistryLayer(uint id = WorldLayerIds.PropEncounterNameRegistry) : base(id)
        {
        }

        protected override void ReadData(ReadOnlySpan<byte> data)
        {
            LayerReader read = new LayerReader(data);
            Names = new string[read.UInt()];
            for (int i = 0; i < Names.Length; i++)
            {
                Names[i] = read.String();
            }
            read.End();
        }

        protected override void WriteData(System.IO.BinaryWriter writer)
        {
            writer.Write((uint)Names.Length);
            foreach (string name in Names)
            {
                writer.WriteLengthPrefixed(name);
            }
        }
    }

    // The sub zone of each cell of a chunk
    public sealed class SubZoneGridLayer : GtLayer
    {
        public uint Unk1;
        public int GridSize;
        public uint[] SubZoneIds;
        public uint GridCount;
        public byte[] Grid;

        public SubZoneGridLayer() : base(WorldLayerIds.SubZoneGrid)
        {
        }

        protected override void ReadData(ReadOnlySpan<byte> data)
        {
            LayerReader read = new LayerReader(data);
            Unk1 = read.UInt();
            GridSize = (int)read.UInt();
            SubZoneIds = new uint[read.UInt()];
            for (int i = 0; i < SubZoneIds.Length; i++)
            {
                SubZoneIds[i] = read.UInt();
            }
            GridCount = read.UInt();
            Grid = read.Bytes((int)(GridCount * GridSize * GridSize));
            read.End();
        }

        protected override void WriteData(System.IO.BinaryWriter writer)
        {
            writer.Write(Unk1);
            writer.Write(GridSize);
            writer.Write((uint)SubZoneIds.Length);
            foreach (uint id in SubZoneIds)
            {
                writer.Write(id);
            }
            writer.Write(GridCount);
            writer.Write(Grid);
        }
    }

    // Two vectors below the default environment, older zones (1710) only have the first
    public sealed class Environment10000Layer : GtLayer
    {
        public Vector3 Data1;
        public Vector3? Data2;

        public Environment10000Layer() : base(WorldLayerIds.Environment10000)
        {
        }

        protected override void ReadData(ReadOnlySpan<byte> data)
        {
            LayerReader read = new LayerReader(data);
            Data1 = read.Vector3();
            Data2 = read.Remaining > 0 ? read.Vector3() : null;
            read.End();
        }

        protected override void WriteData(System.IO.BinaryWriter writer)
        {
            writer.Write(Data1);
            if (Data2.HasValue)
                writer.Write(Data2.Value);
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

        // A layer that has bytes left isn't the type we took it for
        public void End()
        {
            if (Remaining != 0)
                throw new InvalidDataException($"The layer has {Remaining} bytes left");
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

    internal static class LayerWriterExtensions
    {
        public static void Write(this System.IO.BinaryWriter writer, Vector3 value)
        {
            writer.Write(value.X);
            writer.Write(value.Y);
            writer.Write(value.Z);
        }

        public static void Write(this System.IO.BinaryWriter writer, Vector4 value)
        {
            writer.Write(value.X);
            writer.Write(value.Y);
            writer.Write(value.Z);
            writer.Write(value.W);
        }

        // BinaryWriter.Write(string) uses a 7 bit encoded length, the layers a uint
        public static void WriteLengthPrefixed(this System.IO.BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? "");
            writer.Write((uint)bytes.Length);
            writer.Write(bytes);
        }
    }
}
