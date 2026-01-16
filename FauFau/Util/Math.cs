// Copyleft freakbyte 2018, feel free to do whatever you want with this class.

namespace FauFau.Util
{

    public static class Math
    {
        public static double Map(double fromStart, double fromEnd, double toStart, double toEnd, double value)
        {
            return toStart + (value - fromStart) * (toEnd - toStart) / (fromEnd - fromStart);
        }

        public static T Clamp<T>(T value, T max, T min) where T : System.IComparable<T>
        {
            T result = value;
            if (value.CompareTo(max) > 0)
                result = max;
            if (value.CompareTo(min) < 0)
                result = min;
            return result;
        }
    }
}
