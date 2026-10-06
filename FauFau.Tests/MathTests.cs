using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using FFMath = FauFau.Util.Math;

namespace FauFau.Tests
{
    [TestClass]
    public class MathTests
    {
        [TestMethod]
        [DataRow(5, 5)]
        [DataRow(-3, 0)]
        [DataRow(42, 10)]
        public void Clamp_MaxBeforeMin_KeepsValueInRange(int value, int expected)
        {
            int clamped = FFMath.Clamp(value, 10, 0);

            clamped.ShouldBe(expected);
        }

        [TestMethod]
        [DataRow(0, 10, 100, 200, 2.5, 125)]
        [DataRow(0, 10, 200, 100, 10, 100)]
        public void Map_ScalesBetweenRanges(double fromStart, double fromEnd, double toStart, double toEnd, double value, double expected)
        {
            double mapped = FFMath.Map(fromStart, fromEnd, toStart, toEnd, value);

            mapped.ShouldBe(expected);
        }
    }
}
