using Bitter;
using System;

namespace FauFau.Formats
{
    public class Zone : BinaryWrapper
    {
        public string Magic = "ZONE";
        public int Version = 8;
        public DateTime TimeStamp = DateTime.UtcNow;
        public string Name;

        public override void Read(BinaryStream bs)
        {
            BinaryReader Read = bs.Read;

            Magic = Read.String(4);
            Version = Read.Int();
            TimeStamp = Util.Time.DateTimeFromUnixTimestampMicroseconds(Read.Long());
            Name = Read.String(Read.Int() - 1);
        }
    }
}
