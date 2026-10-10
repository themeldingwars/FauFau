using System.Linq;
using System.Numerics;
using FauFau.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class CameraSequenceLayerTests
    {
        private static readonly object[] Identity = { 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 10f, 20f, 30f };

        private static object[] Values(params object[] values)
        {
            return values.SelectMany(v => v is object[] nested ? nested : new[] { v }).ToArray();
        }

        private static object[] Attachment(string bone = "")
        {
            return new object[] { uint.MaxValue, bone, (byte)0 };
        }

        private static byte[] Sequence(byte version)
        {
            return WorldLayersTests.Bytes(Values(
                version, 5672u, "Intro\0", Identity,
                (ushort)1, (ushort)1, (ushort)0, (ushort)0, (ushort)0, (ushort)0, (ushort)1, (ushort)0, (ushort)0, (ushort)1,
                "cam1\0", Identity, Attachment(), (byte)0, 0f, 0f, 0f, Attachment("HP_God\0"), 90f, -1f, 0.01f, 300f, 4000f, (byte)1, 2.3f, 0u,
                "shot1\0", 5.5f, (ushort)0, (ushort)0, 0f, 0f, 0f, 0f, 0f, 0f, (byte)3,
                46246u, 0.1f,
                "Light1\0", 19f, 0.5f, 8f, 0.5f, (byte)0, (byte)0, Identity, Attachment(), (byte)0, 0f, 0f, 0f, Attachment(),
                WorldLayersTests.Layer(WorldLayerIds.PropLight, new byte[] { 1, 2, 3 })));
        }

        [TestMethod]
        public void Read_Version19_ReadsEveryPart()
        {
            byte[] data = Sequence(19);

            GtLayer layer = GtLayer.ReadList(WorldLayersTests.Layer(WorldLayerIds.CameraSequence, data), WorldLayerIds.ZoneRoot).Single();
            byte[] written = layer.GetData();

            CameraSequenceLayer sequence = layer.ShouldBeOfType<CameraSequenceLayer>();
            sequence.CceId.ShouldBe(5672U);
            sequence.Name.ShouldBe("Intro");
            sequence.Transform.Translation.ShouldBe(new Vector3(10, 20, 30));
            sequence.Nodes.Single().Name.ShouldBe("cam1");
            sequence.Nodes.Single().Target.Bone.ShouldBe("HP_God");
            sequence.Nodes.Single().FieldOfView.ShouldBe(90f);
            sequence.Shots.Single().Duration.ShouldBe(5.5f);
            sequence.DialogScripts.Single().DialogScriptId.ShouldBe(46246U);
            sequence.Lights.Single().Timing.Start.ShouldBe(19f);
            sequence.Lights.Single().LightData.ShouldBeOfType<GtDataLayer>().Data.ShouldBe(new byte[] { 1, 2, 3 });
            written.ShouldBe(data);
        }

        [TestMethod]
        public void Read_OtherVersion_StaysData()
        {
            byte[] data = Sequence(18);

            GtLayer layer = GtLayer.ReadList(WorldLayersTests.Layer(WorldLayerIds.CameraSequence, data), WorldLayerIds.ZoneRoot).Single();

            layer.ShouldBeOfType<GtDataLayer>().Data.ShouldBe(data);
        }
    }
}
