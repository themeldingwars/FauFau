using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Bitter;
using FauFau.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class ScZoneTests
    {
        private static byte[] Layer(uint id, params byte[][] content)
        {
            byte[] data = content.SelectMany(c => c).ToArray();
            return BitConverter.GetBytes(GtLayer.Marker).Concat(BitConverter.GetBytes(id)).Concat(BitConverter.GetBytes(data.Length)).Concat(data).ToArray();
        }

        private static byte[] UInts(params uint[] values)
        {
            return values.SelectMany(BitConverter.GetBytes).ToArray();
        }

        private static byte[] Floats(params float[] values)
        {
            return values.SelectMany(BitConverter.GetBytes).ToArray();
        }

        private static byte[] Settings()
        {
            return new byte[] { 2 }.Concat(UInts(1, 0)).Concat(Floats(0, 0, 0, 1)).Concat(Floats(0, 0, -1)).ToArray();
        }

        private static byte[] Environment()
        {
            return Layer(Bnv.EnvironmentLayerId, Layer(0x3E8, UInts(1, 1, 7, 8)));
        }

        private static byte[] Contexts(params string[] names)
        {
            return UInts((uint)names.Length).Concat(names.SelectMany(n => UInts((uint)n.Length + 1).Concat(Encoding.ASCII.GetBytes(n + "\0")))).ToArray();
        }

        private static byte[] Props()
        {
            byte[] props = Layer(0x50011, UInts(1, 2)).Concat(Layer(0x50008, UInts(3))).ToArray();
            MemoryStream compressed = new MemoryStream();
            using (ZLibStream zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
                zlib.Write(props);
            return UInts((uint)props.Length).Concat(compressed.ToArray()).ToArray();
        }

        private static byte[] CreateScZone(params byte[][] layers)
        {
            return Encoding.ASCII.GetBytes("SCZN").Concat(UInts(1)).Concat(layers.SelectMany(l => l)).ToArray();
        }

        [TestMethod]
        public void Read_ScZone_ReadsKnownLayers()
        {
            byte[] bytes = CreateScZone(
                Layer(ScZone.HeaderLayerId, UInts(0, 0)),
                Layer(ScZone.ContextsLayerId, Contexts("Character01", "Character02")),
                Layer(ScZone.SettingsLayerId, Settings(), UInts(19182)),
                Layer(ScZone.EnvironmentLayerId, Environment()),
                Layer(ScZone.PropsLayerId, Props()));
            ScZone zone = new ScZone();

            zone.Read(bytes);

            zone.Layers.Count.ShouldBe(5);
            zone.Contexts.ShouldBe(new[] { "Character01", "Character02" });
            zone.Settings.Unknown1.ShouldBe((byte)2);
            zone.Settings.Unknown2.ShouldBeTrue();
            zone.Settings.Orientation.ShouldBe(new float[] { 0, 0, 0, 1 });
            zone.Settings.Direction.z.ShouldBe(-1f);
            zone.Settings.ReferenceId.ShouldBe(19182U);
            zone.Environment.Find(Bnv.EnvironmentLayerId).ShouldBeOfType<GtContainerLayer>().Find(0x3E8).ShouldBeOfType<GtDataLayer>().Data.ShouldBe(UInts(1, 1, 7, 8));
            zone.Props.Select(p => p.Id).ShouldBe(new uint[] { 0x50011, 0x50008 });
            zone.Props[1].ShouldBeOfType<GtDataLayer>().Data.ShouldBe(UInts(3));
        }

        [TestMethod]
        public void Read_SettingsWithEnvironment_ReadsEnvironmentFromSameLayer()
        {
            byte[] bytes = CreateScZone(Layer(ScZone.SettingsWithEnvironmentLayerId, Settings(), Environment()));
            ScZone zone = new ScZone();

            zone.Read(bytes);

            zone.Settings.ReferenceId.ShouldBeNull();
            zone.Environment.Find(Bnv.EnvironmentLayerId).ShouldNotBeNull();
        }

        [TestMethod]
        public void Read_WrongVersion_Throws()
        {
            byte[] bytes = Encoding.ASCII.GetBytes("SCZN").Concat(UInts(2)).ToArray();

            Action act = () => new ScZone().Read(bytes);

            act.ShouldThrow<NotSupportedException>();
        }

        [TestMethod]
        public void Read_Bnv_ReadsEnvironmentLayer()
        {
            Bnv bnv = new Bnv();

            bnv.Read(Environment());

            bnv.Environment.Children.Single().Id.ShouldBe(0x3E8U);
        }

        [TestMethod]
        public void Read_BnvWithOtherLayer_Throws()
        {
            byte[] bytes = Layer(0x30000, UInts(1));

            Action act = () => new Bnv().Read(bytes);

            act.ShouldThrow<InvalidDataException>();
        }
    }
}
