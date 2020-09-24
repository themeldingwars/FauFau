using System.Numerics;

namespace FauFau.Util
{
    public static class Extensions
    {
        public static Vector3 Vector3(this Bitter.BinaryReader br)
        {
            var vec = new Vector3()
            {
                X = br.Float(),
                Y = br.Float(),
                Z = br.Float()
            };

            return vec;
        }
        
        public static Vector3[] Vector3Array(this Bitter.BinaryReader br, int count)
        {
            var arr = new Vector3[count];
            for (int i = 0; i < count; i++) {
                arr[i] = new Vector3()
                {
                    X = br.Float(),
                    Y = br.Float(),
                    Z = br.Float()
                };
            }

            return arr;
        }

        public static void Vector3(this Bitter.BinaryWriter bw, Vector3 vec)
        {
            bw.Float(vec.X);
            bw.Float(vec.Y);
            bw.Float(vec.Z);
        }
    }
}