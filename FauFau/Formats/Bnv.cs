using Bitter;
using System.IO;

namespace FauFau.Formats
{
    // Environment assets (.bnv) are nothing but the environment layer (0x50001) that zones and chunks carry too
    public class Bnv : BinaryWrapper
    {
        public const uint EnvironmentLayerId = 0x50001;

        public GtLayer Environment;

        public override void Read(BinaryStream bs)
        {
            Environment = GtLayer.Read(bs);
            if (Environment.Id != EnvironmentLayerId)
            {
                throw new InvalidDataException($"Expected the environment layer, got layer 0x{Environment.Id:X}");
            }
        }
    }
}
