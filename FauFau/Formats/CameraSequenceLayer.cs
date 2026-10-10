using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace FauFau.Formats
{
    // The cinematics of a zone (0x21500), read like FUN_01563d60 in the 1962 client. The client still reads versions back
    // to 1, but every 1962 zone has version 19 and the older layouts aren't verified, so those stay raw data.
    public sealed class CameraSequenceLayer : GtLayer
    {
        public const byte SupportedVersion = 19;

        public byte Version = SupportedVersion;
        public uint CceId;
        public string Name = "";
        public Matrix4x4 Transform = Matrix4x4.Identity;

        public List<Node> Nodes = new ();
        public List<Shot> Shots = new ();
        public List<Shake> Shakes = new ();
        public List<Fade> Fades = new ();
        public List<ColorGrading> ColorGradings = new ();
        public List<Npc> Npcs = new ();
        public List<DialogScript> DialogScripts = new ();
        public List<SoundEvent> SoundEvents = new ();
        public List<Pfx> Pfxs = new ();
        public List<Light> Lights = new ();

        public CameraSequenceLayer() : base(WorldLayerIds.CameraSequence)
        {
        }

        protected override void ReadData(ReadOnlySpan<byte> data)
        {
            LayerReader read = new LayerReader(data);
            Version = read.Byte();
            if (Version != SupportedVersion)
                throw new InvalidDataException($"Camera sequence version {Version} isn't supported, only {SupportedVersion}");

            CceId = read.UInt();
            Name = CString(ref read);
            Transform = read.Transform();

            int nodes = read.UShort();
            int shots = read.UShort();
            int shakes = read.UShort();
            int fades = read.UShort();
            int colorGradings = read.UShort();
            int npcs = read.UShort();
            int dialogScripts = read.UShort();
            int soundEvents = read.UShort();
            int pfxs = read.UShort();
            int lights = read.UShort();

            Nodes = ReadAll(ref read, nodes, Node.Read);
            Shots = ReadAll(ref read, shots, Shot.Read);
            Shakes = ReadAll(ref read, shakes, Shake.Read);
            Fades = ReadAll(ref read, fades, Fade.Read);
            ColorGradings = ReadAll(ref read, colorGradings, ColorGrading.Read);
            Npcs = ReadAll(ref read, npcs, Npc.Read);
            DialogScripts = ReadAll(ref read, dialogScripts, DialogScript.Read);
            SoundEvents = ReadAll(ref read, soundEvents, SoundEvent.Read);
            Pfxs = ReadAll(ref read, pfxs, Pfx.Read);
            Lights = ReadAll(ref read, lights, Light.Read);
            read.End();
        }

        protected override void WriteData(System.IO.BinaryWriter writer)
        {
            writer.Write(Version);
            writer.Write(CceId);
            WriteCString(writer, Name);
            writer.WriteTransform(Transform);

            writer.Write((ushort)Nodes.Count);
            writer.Write((ushort)Shots.Count);
            writer.Write((ushort)Shakes.Count);
            writer.Write((ushort)Fades.Count);
            writer.Write((ushort)ColorGradings.Count);
            writer.Write((ushort)Npcs.Count);
            writer.Write((ushort)DialogScripts.Count);
            writer.Write((ushort)SoundEvents.Count);
            writer.Write((ushort)Pfxs.Count);
            writer.Write((ushort)Lights.Count);

            Nodes.ForEach(x => x.Write(writer));
            Shots.ForEach(x => x.Write(writer));
            Shakes.ForEach(x => x.Write(writer));
            Fades.ForEach(x => x.Write(writer));
            ColorGradings.ForEach(x => x.Write(writer));
            Npcs.ForEach(x => x.Write(writer));
            DialogScripts.ForEach(x => x.Write(writer));
            SoundEvents.ForEach(x => x.Write(writer));
            Pfxs.ForEach(x => x.Write(writer));
            Lights.ForEach(x => x.Write(writer));
        }

        private delegate T ItemReader<T>(ref LayerReader read);

        private static List<T> ReadAll<T>(ref LayerReader read, int count, ItemReader<T> readItem)
        {
            List<T> items = new (count);
            for (int i = 0; i < count; i++)
            {
                items.Add(readItem(ref read));
            }
            return items;
        }

        // The length counts the NUL at the end, empty strings have neither
        private static string CString(ref LayerReader read)
        {
            string value = read.String();
            return value.EndsWith('\0') ? value[..^1] : value;
        }

        private static void WriteCString(System.IO.BinaryWriter writer, string value)
        {
            writer.WriteLengthPrefixed(string.IsNullOrEmpty(value) ? "" : value + "\0");
        }

        // What a transform is relative to, the source is uint.MaxValue when it's the sequence itself
        public struct Attachment
        {
            public uint Source;
            public string Bone;
            public bool Unk1;

            internal static Attachment Read(ref LayerReader read)
            {
                return new Attachment { Source = read.UInt(), Bone = CString(ref read), Unk1 = read.Bool() };
            }

            internal void Write(System.IO.BinaryWriter writer)
            {
                writer.Write(Source);
                WriteCString(writer, Bone);
                writer.Write(Unk1);
            }
        }

        // The part that fades, color gradings and lights share
        public struct Timing
        {
            public string Name;
            public float Start;
            public float Unk1;
            public float Unk2;
            public float Unk3;
            public byte Unk4;
            public byte Unk5;

            internal static Timing Read(ref LayerReader read)
            {
                return new Timing
                {
                    Name = CString(ref read),
                    Start = read.Float(),
                    Unk1 = read.Float(),
                    Unk2 = read.Float(),
                    Unk3 = read.Float(),
                    Unk4 = read.Byte(),
                    Unk5 = read.Byte(),
                };
            }

            internal void Write(System.IO.BinaryWriter writer)
            {
                WriteCString(writer, Name);
                writer.Write(Start);
                writer.Write(Unk1);
                writer.Write(Unk2);
                writer.Write(Unk3);
                writer.Write(Unk4);
                writer.Write(Unk5);
            }
        }

        // A camera position, the shots move between them
        public sealed class Node
        {
            public string Name = "";
            public Matrix4x4 Transform = Matrix4x4.Identity;
            public Attachment Attachment;
            public bool Unk1;
            public Vector3 Unk2;
            public Attachment Target;

            // 90 in most nodes
            public float FieldOfView;

            // -1, 0.01, 300 and 4000 in most nodes, the clip planes are a guess
            public Vector4 Unk3;
            public bool Unk4;
            public float Unk5;
            public uint Unk6;

            internal static Node Read(ref LayerReader read)
            {
                return new Node
                {
                    Name = CString(ref read),
                    Transform = read.Transform(),
                    Attachment = Attachment.Read(ref read),
                    Unk1 = read.Bool(),
                    Unk2 = read.Vector3(),
                    Target = Attachment.Read(ref read),
                    FieldOfView = read.Float(),
                    Unk3 = read.Vector4(),
                    Unk4 = read.Bool(),
                    Unk5 = read.Float(),
                    Unk6 = read.UInt(),
                };
            }

            internal void Write(System.IO.BinaryWriter writer)
            {
                WriteCString(writer, Name);
                writer.WriteTransform(Transform);
                Attachment.Write(writer);
                writer.Write(Unk1);
                writer.Write(Unk2);
                Target.Write(writer);
                writer.Write(FieldOfView);
                writer.Write(Unk3);
                writer.Write(Unk4);
                writer.Write(Unk5);
                writer.Write(Unk6);
            }
        }

        // A camera move from one node to another, the client starts each shot when the one before ends
        public sealed class Shot
        {
            public string Name = "";
            public float Duration;
            public ushort FromNode;
            public ushort ToNode;
            public Vector3 Unk1;
            public Vector3 Unk2;
            public byte Unk3;

            internal static Shot Read(ref LayerReader read)
            {
                return new Shot
                {
                    Name = CString(ref read),
                    Duration = read.Float(),
                    FromNode = read.UShort(),
                    ToNode = read.UShort(),
                    Unk1 = read.Vector3(),
                    Unk2 = read.Vector3(),
                    Unk3 = read.Byte(),
                };
            }

            internal void Write(System.IO.BinaryWriter writer)
            {
                WriteCString(writer, Name);
                writer.Write(Duration);
                writer.Write(FromNode);
                writer.Write(ToNode);
                writer.Write(Unk1);
                writer.Write(Unk2);
                writer.Write(Unk3);
            }
        }

        public sealed class Shake
        {
            public string Name = "";
            public float Start;
            public float[] Unk1 = new float[7];
            public byte Unk2;
            public byte Unk3;
            public float Unk4;
            public float Unk5;
            public float Unk6;

            internal static Shake Read(ref LayerReader read)
            {
                Shake shake = new Shake { Name = CString(ref read), Start = read.Float() };
                for (int i = 0; i < shake.Unk1.Length; i++)
                {
                    shake.Unk1[i] = read.Float();
                }
                shake.Unk2 = read.Byte();
                shake.Unk3 = read.Byte();
                shake.Unk4 = read.Float();
                shake.Unk5 = read.Float();
                shake.Unk6 = read.Float();
                return shake;
            }

            internal void Write(System.IO.BinaryWriter writer)
            {
                WriteCString(writer, Name);
                writer.Write(Start);
                foreach (float value in Unk1)
                {
                    writer.Write(value);
                }
                writer.Write(Unk2);
                writer.Write(Unk3);
                writer.Write(Unk4);
                writer.Write(Unk5);
                writer.Write(Unk6);
            }
        }

        public sealed class Fade
        {
            public Timing Timing;
            public Vector4 Color;

            internal static Fade Read(ref LayerReader read)
            {
                return new Fade { Timing = Timing.Read(ref read), Color = read.Vector4() };
            }

            internal void Write(System.IO.BinaryWriter writer)
            {
                Timing.Write(writer);
                writer.Write(Color);
            }
        }

        public sealed class ColorGrading
        {
            public Timing Timing;
            public uint ColorGradingId;

            internal static ColorGrading Read(ref LayerReader read)
            {
                return new ColorGrading { Timing = Timing.Read(ref read), ColorGradingId = read.UInt() };
            }

            internal void Write(System.IO.BinaryWriter writer)
            {
                Timing.Write(writer);
                writer.Write(ColorGradingId);
            }
        }

        public sealed class Npc
        {
            public string Name = "";
            public uint CharacterId;
            public Matrix4x4 Transform = Matrix4x4.Identity;
            public List<MeshSlot> MeshSlots = new ();
            public List<Action> Actions = new ();

            public struct MeshSlot
            {
                public string Slot;
                public uint Id;
            }

            public struct Action
            {
                public uint Unk1;
                public ushort Unk2;
                public string Name;
            }

            internal static Npc Read(ref LayerReader read)
            {
                Npc npc = new Npc { Name = CString(ref read), CharacterId = read.UInt(), Transform = read.Transform() };
                int slots = read.UShort();
                for (int i = 0; i < slots; i++)
                {
                    npc.MeshSlots.Add(new MeshSlot { Slot = CString(ref read), Id = read.UInt() });
                }

                int actions = read.UShort();
                for (int i = 0; i < actions; i++)
                {
                    npc.Actions.Add(new Action { Unk1 = read.UInt(), Unk2 = read.UShort(), Name = CString(ref read) });
                }
                return npc;
            }

            internal void Write(System.IO.BinaryWriter writer)
            {
                WriteCString(writer, Name);
                writer.Write(CharacterId);
                writer.WriteTransform(Transform);
                writer.Write((ushort)MeshSlots.Count);
                foreach (MeshSlot slot in MeshSlots)
                {
                    WriteCString(writer, slot.Slot);
                    writer.Write(slot.Id);
                }

                writer.Write((ushort)Actions.Count);
                foreach (Action action in Actions)
                {
                    writer.Write(action.Unk1);
                    writer.Write(action.Unk2);
                    WriteCString(writer, action.Name);
                }
            }
        }

        public sealed class DialogScript
        {
            public uint DialogScriptId;
            public float Start;

            internal static DialogScript Read(ref LayerReader read)
            {
                return new DialogScript { DialogScriptId = read.UInt(), Start = read.Float() };
            }

            internal void Write(System.IO.BinaryWriter writer)
            {
                writer.Write(DialogScriptId);
                writer.Write(Start);
            }
        }

        public sealed class SoundEvent
        {
            public uint EventId;
            public float Start;
            public Matrix4x4 Transform = Matrix4x4.Identity;
            public Attachment Attachment;
            public bool Unk1;

            internal static SoundEvent Read(ref LayerReader read)
            {
                return new SoundEvent
                {
                    EventId = read.UInt(),
                    Start = read.Float(),
                    Transform = read.Transform(),
                    Attachment = Attachment.Read(ref read),
                    Unk1 = read.Bool(),
                };
            }

            internal void Write(System.IO.BinaryWriter writer)
            {
                writer.Write(EventId);
                writer.Write(Start);
                writer.WriteTransform(Transform);
                Attachment.Write(writer);
                writer.Write(Unk1);
            }
        }

        public sealed class Pfx
        {
            public uint EffectId;
            public float Start;
            public float Duration;
            public bool Unk1;
            public bool Unk2;
            public Matrix4x4 Transform = Matrix4x4.Identity;
            public Attachment Attachment;

            internal static Pfx Read(ref LayerReader read)
            {
                return new Pfx
                {
                    EffectId = read.UInt(),
                    Start = read.Float(),
                    Duration = read.Float(),
                    Unk1 = read.Bool(),
                    Unk2 = read.Bool(),
                    Transform = read.Transform(),
                    Attachment = Attachment.Read(ref read),
                };
            }

            internal void Write(System.IO.BinaryWriter writer)
            {
                writer.Write(EffectId);
                writer.Write(Start);
                writer.Write(Duration);
                writer.Write(Unk1);
                writer.Write(Unk2);
                writer.WriteTransform(Transform);
                Attachment.Write(writer);
            }
        }

        public sealed class Light
        {
            public Timing Timing;
            public Matrix4x4 Transform = Matrix4x4.Identity;
            public Attachment Attachment;
            public bool Unk1;
            public Vector3 Unk2;
            public Attachment Target;

            // The same light layer (0x50011) the chunk props use, kept as raw data
            public GtLayer LightData = new GtDataLayer(WorldLayerIds.PropLight);

            internal static Light Read(ref LayerReader read)
            {
                return new Light
                {
                    Timing = Timing.Read(ref read),
                    Transform = read.Transform(),
                    Attachment = Attachment.Read(ref read),
                    Unk1 = read.Bool(),
                    Unk2 = read.Vector3(),
                    Target = Attachment.Read(ref read),
                    LightData = read.Layer(WorldLayerIds.CameraSequence),
                };
            }

            internal void Write(System.IO.BinaryWriter writer)
            {
                Timing.Write(writer);
                writer.WriteTransform(Transform);
                Attachment.Write(writer);
                writer.Write(Unk1);
                writer.Write(Unk2);
                Target.Write(writer);
                writer.Write(LightData.ToArray());
            }
        }
    }
}
