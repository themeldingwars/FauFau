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
            TimeStamp = Util.Time.DateTimeFromUnixTimestampMilliseconds(Read.Long());

            // The length includes the terminating NUL
            int nameLength = Read.Int();
            Name = nameLength > 1 ? Read.String(nameLength - 1) : "";
            if (nameLength > 0)
            {
                Read.Byte();
            }
        }
    }
}
