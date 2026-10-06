using System;
using System.IO;
using System.Linq;
using FauFau.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class NsrViewTests
    {
        private static Nsr CreateLongSample()
        {
            Nsr nsr = NsrTests.CreateSample(true);
            for (int i = 0; i < 2000; i++)
                nsr.Packets.Add(new Nsr.Packet { TimeStamp = (uint)i, MessageId = 3, Data = BitConverter.GetBytes(i * 7919) });
            return nsr;
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void Open_ReadsSectionsAndPackets(bool compressed)
        {
            Nsr nsr = NsrTests.CreateSample(compressed);
            byte[] bytes = NsrTests.WriteBytes(nsr);

            using NsrView view = NsrView.Open(bytes);
            NsrView.Packet[] packets = view.ToArray();

            view.GzipLayers.ShouldBe(compressed ? 1 : 0);
            view.Truncated.ShouldBeFalse();
            view.Meta.CharacterName.ShouldBe("Tester");
            view.Index.Offsets.ShouldBe(new uint[] { 329 });
            view.Count.ShouldBe(2);
            packets.Select(p => p.MessageId).ShouldBe(nsr.Packets.Select(p => p.MessageId));
            packets.Select(p => p.TimeStamp).ShouldBe(nsr.Packets.Select(p => p.TimeStamp));
            packets[0].Data.ToArray().ShouldBe(nsr.Packets[0].Data);
            packets[1].Data.ToArray().ShouldBe(nsr.Packets[1].Data);
        }

        [TestMethod]
        public void Open_MatchesNsr()
        {
            byte[] bytes = NsrTests.WriteBytes(CreateLongSample());
            Nsr nsr = new Nsr();
            nsr.Read(bytes);

            using NsrView view = NsrView.Open(bytes);
            Nsr.Packet[] packets = view.Select(p => p.ToPacket()).ToArray();

            packets.Select(p => (p.TimeStamp, p.MessageId, p.Length, Convert.ToHexString(p.Data)))
                .ShouldBe(nsr.Packets.Select(p => (p.TimeStamp, p.MessageId, p.Length, Convert.ToHexString(p.Data))));
        }

        [TestMethod]
        public void Open_Version2_ReadsPackets()
        {
            byte[] bytes = NsrTests.CreateVersion2();

            using NsrView view = NsrView.Open(bytes);

            view.Description.Version.ShouldBe(2);
            view.Count.ShouldBe(2);
            view.First().TimeStamp.ShouldBe(3795714048U);
        }

        [TestMethod]
        public void Open_GzipTwice_ReadsAllLayers()
        {
            byte[] bytes = NsrTests.Gzip(NsrTests.Gzip(NsrTests.WriteBytes(NsrTests.CreateSample(false))));

            using NsrView view = NsrView.Open(bytes);

            view.GzipLayers.ShouldBe(2);
            view.Count.ShouldBe(2);
        }

        [TestMethod]
        public void Open_LastPacketCutOff_KeepsCompletePackets()
        {
            byte[] bytes = NsrTests.WriteBytes(NsrTests.CreateSample(false));

            using NsrView view = NsrView.Open(bytes.AsSpan(0, bytes.Length - 5));
            int enumerated = view.Count();

            view.Truncated.ShouldBeTrue();
            view.Count.ShouldBe(1);
            enumerated.ShouldBe(1);
        }

        [TestMethod]
        public void Open_GzipCutOff_KeepsCompletePackets()
        {
            Nsr nsr = CreateLongSample();
            byte[] bytes = NsrTests.WriteBytes(nsr);
            Nsr read = new Nsr();
            read.Read(bytes.AsSpan(0, bytes.Length / 2).ToArray());

            using NsrView view = NsrView.Open(bytes.AsSpan(0, bytes.Length / 2));

            view.Truncated.ShouldBeTrue();
            view.Count.ShouldBe(read.Packets.Count);
            view.Count.ShouldBeLessThan(nsr.Packets.Count);
        }

        [TestMethod]
        public void Open_UnpackedArray_CopiesTheData()
        {
            byte[] bytes = NsrTests.WriteBytes(NsrTests.CreateSample(false));
            using NsrView view = NsrView.Open(bytes);

            Array.Clear(bytes);
            byte[] data = view.Last().Data.ToArray();

            data.ShouldBe(NsrTests.CreateSample(false).Packets[1].Data);
        }

        [TestMethod]
        public void Open_Path_ReadsFile()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, NsrTests.WriteBytes(NsrTests.CreateSample(true)));

                using NsrView view = NsrView.Open(path);

                view.Count.ShouldBe(2);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void Enumerate_AfterDispose_ThrowsObjectDisposed()
        {
            byte[] bytes = NsrTests.WriteBytes(NsrTests.CreateSample(false));
            NsrView view = NsrView.Open(bytes);
            view.Dispose();

            Action act = () => view.ToArray();

            act.ShouldThrow<ObjectDisposedException>();
        }

        [TestMethod]
        public void Open_NotAReplay_Throws()
        {
            byte[] bytes = new byte[64];

            Action act = () => NsrView.Open(bytes);

            act.ShouldThrow<InvalidDataException>();
        }
    }
}
