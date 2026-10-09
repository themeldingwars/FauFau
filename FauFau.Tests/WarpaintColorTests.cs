using FauFau.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class WarpaintColorTests
    {
        [TestMethod]
        [DataRow(0xFFFFFFFFU, (ushort)0xFFFF)]
        [DataRow(0xFF000000U, (ushort)0x0000)]
        [DataRow(0xFFFF0000U, (ushort)0xF800)]
        [DataRow(0xFF00FF00U, (ushort)0x07E0)]
        [DataRow(0xFF0000FFU, (ushort)0x001F)]
        [DataRow(0x00808080U, (ushort)0x8410)]
        public void ToRgb565_DropsLowBitsAndAlpha(uint argb, ushort expected)
        {
            ushort rgb = WarpaintColor.ToRgb565(argb);

            rgb.ShouldBe(expected);
        }

        [TestMethod]
        [DataRow((ushort)0xFFFF, 0xFFFFFFFFU)]
        [DataRow((ushort)0x0000, 0xFF000000U)]
        [DataRow((ushort)0x8410, 0xFF848284U)]
        public void FromRgb565_ScalesToFullRange(ushort rgb, uint expected)
        {
            uint argb = WarpaintColor.FromRgb565(rgb);

            argb.ShouldBe(expected);
        }

        [TestMethod]
        public void FromRgb565_ToRgb565_RoundTrips()
        {
            for (int rgb = 0; rgb <= 0xFFFF; rgb++)
            {
                WarpaintColor.ToRgb565(WarpaintColor.FromRgb565((ushort)rgb)).ShouldBe((ushort)rgb);
            }
        }

        [TestMethod]
        public void Pack_PutsLightInUpperHalf()
        {
            uint packed = WarpaintColor.Pack(0xFFFF0000, 0xFF0000FF);

            packed.ShouldBe(0xF800001FU);
            WarpaintColor.UnpackLight(packed).ShouldBe(0xFFFF0000U);
            WarpaintColor.UnpackDark(packed).ShouldBe(0xFF0000FFU);
        }
    }
}
