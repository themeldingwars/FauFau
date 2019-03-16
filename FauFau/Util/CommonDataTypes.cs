using Bitter;
using static Bitter.BinaryWrapper;

namespace FauFau.Util.CommmonDataTypes
{
    public class Vector2 : ReadWrite
    {
        public float x;
        public float y;

        public void Read(BinaryStream bs)
        {
            x = bs.Read.Float();
            y = bs.Read.Float();
        }

        public void Write(BinaryStream bs)
        {
            bs.Write.Float(x);
            bs.Write.Float(y);
        }
    }
    public class Vector3 : ReadWrite
    {
        public float x;
        public float y;
        public float z;

        public void Read(BinaryStream bs)
        {
            x = bs.Read.Float();
            y = bs.Read.Float();
            z = bs.Read.Float();
        }

        public void Write(BinaryStream bs)
        {
            bs.Write.Float(x);
            bs.Write.Float(y);
            bs.Write.Float(z);
        }
    }
    public class Vector4 : ReadWrite
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public void Read(BinaryStream bs)
        {
            x = bs.Read.Float();
            y = bs.Read.Float();
            z = bs.Read.Float();
            w = bs.Read.Float();
        }

        public void Write(BinaryStream bs)
        {
            bs.Write.Float(x);
            bs.Write.Float(y);
            bs.Write.Float(z);
            bs.Write.Float(w);
        }
    }
    public class Box3 : ReadWrite
    {
        public Vector3 min;
        public Vector3 max;

        public void Read(BinaryStream bs)
        {
            min = bs.Read.Type<Vector3>();
            max = bs.Read.Type<Vector3>();
        }

        public void Write(BinaryStream bs)
        {
            bs.Write.Type(min);
            bs.Write.Type(max);
        }
    }
    public class Matrix4x4 : ReadWrite
    {
        public Vector4 x;
        public Vector4 y;
        public Vector4 z;
        public Vector4 w;

        public void Read(BinaryStream bs)
        {
            x = bs.Read.Type<Vector4>();
            y = bs.Read.Type<Vector4>();
            z = bs.Read.Type<Vector4>();
            w = bs.Read.Type<Vector4>();
        }

        public void Write(BinaryStream bs)
        {
            bs.Write.Type(x);
            bs.Write.Type(y);
            bs.Write.Type(z);
            bs.Write.Type(w);
        }
    }
    public class Half3 : ReadWrite
    {
        public float x;
        public float y;
        public float z;

        public void Read(BinaryStream bs)
        {
            x = bs.Read.Half();
            y = bs.Read.Half();
            z = bs.Read.Half();
        }

        public void Write(BinaryStream bs)
        {
            bs.Write.Half(x);
            bs.Write.Half(y);
            bs.Write.Half(z);
        }
    }
    public class HalfMatrix4x3 : ReadWrite
    {
        public Half3 x;
        public Half3 y;
        public Half3 z;
        public Half3 w;

        public void Read(BinaryStream bs)
        {
            x = bs.Read.Type<Half3>();
            y = bs.Read.Type<Half3>();
            z = bs.Read.Type<Half3>();
            w = bs.Read.Type<Half3>();
        }

        public void Write(BinaryStream bs)
        {
            bs.Write.Type(x);
            bs.Write.Type(y);
            bs.Write.Type(z);
            bs.Write.Type(w);
        }
    }  
}
