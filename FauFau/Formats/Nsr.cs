using Bitter;
using System;
using System.Collections.Generic;
using System.Text;

namespace FauFau.Formats
{
    public class Nsr : BinaryWrapper
    {
        public bool Compressed = false;
        public byte[] Data;
        public override void Read(BinaryStream bs)
        {
            using (BinaryStream payload = new BinaryStream())
            {
                // check if compressed
                if (bs.Read.UInt() == 559903)
                {
                    bs.ByteOffset = 0;
                    try
                    {
                        Util.Util.UnGzipUnknownTargetSize(bs, payload);
                        Compressed = true;
                    }
                    catch
                    {
                        payload.ByteOffset = 0;
                        payload.Write.ByteArray(bs.Read.ByteArray((int)bs.Length));
                    }
                }
                else
                {
                    payload.Write.ByteArray(bs.Read.ByteArray((int)bs.Length));
                }
                bs.Dispose();

                payload.ByteOffset = 0;

                if(payload.Length == 0)
                {
                    Compressed = false;
                    return;
                }

                Console.WriteLine(payload.Length);
                Data = payload.Read.ByteArray((int)payload.Length);
            } 
        }

        public override void Write(BinaryStream bs)
        {
            using (BinaryStream payload = new BinaryStream())
            {
                payload.Write.ByteArray(Data);
                payload.ByteOffset = 0;

                if (Compressed)
                {
                    // compress with gzip
                    Util.Util.Gzip(payload, bs);
                }
                else
                {
                    // write as plain data
                    bs.Write.ByteArray(payload.Read.ByteArray((int)payload.Length));
                }
            }
        }
    }
}
