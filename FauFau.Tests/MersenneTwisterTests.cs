using System;
using FauFau.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class MersenneTwisterTests
    {
        [TestMethod]
        public void Next_DefaultSeed_MatchesKnownSequence()
        {
            MersenneTwister mt = new MersenneTwister();

            uint[] values = { mt.Next(), mt.Next(), mt.Next(), mt.Next() };

            values.ShouldBe(new[] { 0xD091BB5CU, 0x22AE9EF6U, 0xE7E1FAEEU, 0xD5C31F79U });
        }

        [TestMethod]
        public void Next_AcrossStateRegeneration_MatchesKnownValues()
        {
            MersenneTwister mt = new MersenneTwister();

            uint[] values = mt.Next(1000);

            values[623].ShouldBe(0xEFA14DFFU);
            values[624].ShouldBe(0xF914DC58U);
            values[999].ShouldBe(0x4FEE4F80U);
        }

        [TestMethod]
        public void Next_CustomSeed_MatchesKnownSequence()
        {
            MersenneTwister mt = new MersenneTwister(1234);

            uint[] values = mt.Next(3);

            values.ShouldBe(new[] { 0x31076B2FU, 0x7F66E2D3U, 0x9F428526U });
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(7)]
        [DataRow(908)]
        [DataRow(909)]
        [DataRow(911)]
        [DataRow(5000)]
        public void Xor_MatchesSequentialOutputs(int length)
        {
            byte[] data = new byte[length];
            new System.Random(length).NextBytes(data);
            byte[] expected = (byte[])data.Clone();
            MersenneTwister mt = new MersenneTwister(0xC0FFEE);
            for (int i = 0; i < length / 4; i++)
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(i * 4), System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(expected.AsSpan(i * 4)) ^ mt.Next());
            for (int i = length / 4 * 4; i < length; i++)
                expected[i] ^= (byte)mt.Next();

            MersenneTwister.Xor(0xC0FFEE, data);

            data.ShouldBe(expected);
        }

        [TestMethod]
        [DataRow(1)]
        [DataRow(227)]
        [DataRow(228)]
        [DataRow(700)]
        public void Fill_MatchesNext(int count)
        {
            uint[] expected = new MersenneTwister(0xC0FFEE).Next((uint)count);
            uint[] values = new uint[count];

            MersenneTwister.Fill(0xC0FFEE, values);

            values.ShouldBe(expected);
        }

        [TestMethod]
        public void Reseed_RestartsSequence()
        {
            MersenneTwister mt = new MersenneTwister(1234);
            mt.Next(700);
            uint[] expected = new MersenneTwister(1234).Next(700);

            mt.Reseed(1234);
            uint[] values = mt.Next(700);

            values.ShouldBe(expected);
        }
    }
}
