using System.IO;
using System.Linq;
using FauFau.Formats;

namespace Tests
{
    public class CziTests
    {

        public static void ReadTest()
        {
            var czi     = new Czi("C:\\temp\\FauFau\\00107072_org.czi");
            DumpToFolder(czi, "C:\\temp\\FauFau\\00107072_og");

            string packDir = "C:\\temp\\FauFau\\00107072_pack";
            if (Directory.Exists(packDir)) {
                var newCzi = PackFromFolder(2048, 2048, packDir);
                newCzi.Save("C:\\temp\\FauFau\\00107072.czi");
            }
        }

        private static void DumpToFolder(Czi czi, string folder)
        {
            Directory.CreateDirectory(folder);
            
            for (int i = 0; i < czi.Head.NumMipLevels; i++) {
                var mipData = czi.GetMipDecompressed(i);
                File.WriteAllBytes($"{folder}\\{i}.raw", mipData);
            }
        }

        private static Czi PackFromFolder(int width, int height, string folder)
        {
            var czi   = Czi.CreateMaskCzi(width, height);
            
            var files = Directory.GetFiles(folder).Order();
            foreach (var file in files) {
                var data = File.ReadAllBytes(file);
                czi.AddMipLevel(data);
            }
            
            return czi;
        }
    }
}
