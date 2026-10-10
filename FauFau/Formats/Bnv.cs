using Bitter;
using System.IO;

namespace FauFau.Formats
{
    // Environment assets (.bnv) are nothing but the environment layer (0x50001) that zones and chunks carry too
    public class Bnv : BinaryWrapper
    {
        public const uint EnvironmentLayerId = 0x50001;

        public GtContainerLayer Environment;

        public override void Read(BinaryStream bs)
        {
            GtLayer layer = GtLayer.Read(bs);
            if (layer.Id != EnvironmentLayerId || layer is not GtContainerLayer environment)
            {
                throw new InvalidDataException($"Expected the environment layer, got layer 0x{layer.Id:X}");
            }
            Environment = environment;
        }

        public override void Write(BinaryStream bs)
        {
            Environment.Write(bs);
        }
    }
}
