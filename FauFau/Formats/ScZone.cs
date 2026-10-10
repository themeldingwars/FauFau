using Bitter;
using FauFau.Util.CommmonDataTypes;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FauFau.Formats
{
    // Self-contained zones (.scZone) hold the props and environment of a 3D scene in the UI, the client calls that Sinvironment
    public class ScZone : BinaryWrapper
    {
        public const uint HeaderLayerId = 0x100;
        public const uint SettingsWithEnvironmentLayerId = 0x200;
        public const uint SettingsLayerId = 0x201;
        public const uint EnvironmentLayerId = 0x202;
        public const uint PropsLayerId = 0x300;
        public const uint ContextsLayerId = 0x400;

        public string Magic = "SCZN";
        public uint Version = 1;

        // Every layer as stored, the client ignores the header layer
        public List<GtLayer> Layers = new ();

        public SceneSettings Settings;

        // Its children are the environment layers (0x50001), the same as in zones
        public GtContainerLayer Environment;

        // The same prop layers the chunks use
        public List<GtLayer> Props = new ();

        // Prop groups the UI can show and hide, like "Character01"
        public List<string> Contexts = new ();

        public override void Read(BinaryStream bs)
        {
            Bitter.BinaryReader Read = bs.Read;

            Magic = Read.String(4);
            if (Magic != "SCZN")
            {
                throw new InvalidDataException($"Not a self-contained zone, the magic is {Magic}");
            }
            Version = Read.UInt();
            if (Version != 1)
            {
                throw new NotSupportedException($"Self-contained zone version {Version} isn't supported, only version 1");
            }

            Layers = new List<GtLayer>();
            while (bs.ByteOffset < bs.Length)
            {
                Layers.Add(GtLayer.Read(bs));
            }

            foreach (GtLayer layer in Layers)
            {
                switch (layer.Id)
                {
                    case SettingsWithEnvironmentLayerId or SettingsLayerId when layer is GtDataLayer settings:
                        ReadSettings(settings);
                        break;
                    case EnvironmentLayerId:
                        Environment = layer as GtContainerLayer;
                        break;
                    case PropsLayerId when layer is GtDataLayer props:
                        Props = ReadProps(props.Data);
                        break;
                    case ContextsLayerId when layer is GtDataLayer contexts:
                        Contexts = ReadContexts(contexts.Data);
                        break;
                }
            }
        }

        private void ReadSettings(GtDataLayer layer)
        {
            BinaryStream bs = new BinaryStream(new MemoryStream(layer.Data));
            Bitter.BinaryReader Read = bs.Read;

            Settings = new SceneSettings
            {
                Unknown1 = Read.Byte(),
                Unknown2 = Read.UInt() != 0,
                Unknown3 = Read.UInt(),
                Orientation = Read.FloatArray(4),
                Direction = Read.Type<Vector3>(),
            };

            if (layer.Id == SettingsWithEnvironmentLayerId)
            {
                // The environment layers follow the settings inside the same layer
                GtContainerLayer environment = new GtContainerLayer(EnvironmentLayerId);
                while (bs.ByteOffset < bs.Length)
                {
                    environment.Children.Add(GtLayer.Read(bs, EnvironmentLayerId));
                }
                Environment = environment;
            }
            else if (bs.ByteOffset + 4 <= bs.Length)
            {
                Settings.ReferenceId = Read.UInt();
            }
        }

        // The decoded size, then zlib data with the prop layers
        private static List<GtLayer> ReadProps(byte[] data)
        {
            if (data.Length < 4)
            {
                throw new InvalidDataException("The props layer is cut off");
            }

            int size = BitConverter.ToInt32(data, 0);
            byte[] decoded = new byte[size];
            using (ZLibStream zlib = new ZLibStream(new MemoryStream(data, 4, data.Length - 4), CompressionMode.Decompress))
            {
                int read = zlib.ReadAtLeast(decoded, decoded.Length, false);
                if (read != decoded.Length)
                {
                    throw new InvalidDataException($"The props have {read} instead of {decoded.Length} bytes");
                }
            }

            List<GtLayer> props = new ();
            BinaryStream bs = new BinaryStream(new MemoryStream(decoded));
            while (bs.ByteOffset < bs.Length)
            {
                props.Add(GtLayer.Read(bs, WorldLayerIds.Props));
            }
            return props;
        }

        // A count, then strings with a length that includes the terminating NUL
        private static List<string> ReadContexts(byte[] data)
        {
            BinaryStream bs = new BinaryStream(new MemoryStream(data));
            int count = bs.Read.Int();
            List<string> contexts = new (count);
            for (int i = 0; i < count; i++)
            {
                int length = bs.Read.Int();
                byte[] text = bs.Read.ByteArray(length);
                contexts.Add(Encoding.ASCII.GetString(text, 0, Math.Max(0, length - 1)));
            }
            return contexts;
        }

        public class SceneSettings
        {
            public byte Unknown1;
            public bool Unknown2;
            public uint Unknown3;

            // Named after their values, a unit quaternion and a unit vector
            public float[] Orientation;
            public Vector3 Direction;

            // Only in the settings layer without environment, and only when there's room left
            public uint? ReferenceId;
        }
    }
}
