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
