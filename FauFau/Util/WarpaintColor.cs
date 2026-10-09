namespace FauFau.Util
{
    // Warpaint colors pack a light and a dark shade as RGB565 into one uint, the light one in the upper 16 bits.
    // The unpacked colors are ARGB8888 with full alpha.
    public static class WarpaintColor
    {
        public static uint Pack(uint light, uint dark) => ToRgb565(dark) | ((uint)ToRgb565(light) << 16);
        public static uint UnpackLight(uint packed) => FromRgb565((ushort)(packed >> 16));
        public static uint UnpackDark(uint packed) => FromRgb565((ushort)packed);

        public static ushort ToRgb565(uint argb)
        {
            return (ushort)((((argb >> 16) & 0xFF) >> 3 << 11) | (((argb >> 8) & 0xFF) >> 2 << 5) | ((argb & 0xFF) >> 3));
        }

        // Scales the 5 and 6 bit channels to 8 bits with rounding
        public static uint FromRgb565(ushort rgb)
        {
            int r = (rgb >> 11) * 255 + 16;
            int g = ((rgb >> 5) & 0x3F) * 255 + 32;
            int b = (rgb & 0x1F) * 255 + 16;
            return 0xFF000000 | (uint)((r / 32 + r) / 32) << 16 | (uint)((g / 64 + g) / 64) << 8 | (uint)((b / 32 + b) / 32);
        }
    }
}
