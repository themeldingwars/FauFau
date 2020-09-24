using System;
using System.IO;
using FauFau.Formats;

namespace Tests
{
    public class GtChunksTests
    {
        public static void TestLoad()
        {
            string gtChunkPath = @"C:\NonWindows\Games\Firefall\system\maps\chunks/5_1134_1497.gtchunk";

            var chunk = new GtChunkV8();
            chunk.Load(gtChunkPath);

            WriteCompressedChunksToDisk(chunk, @"C:\NonWindows\Projects\FauFau\Tests\gtchunkNodes");

            //chunk.GetDecompressedLod(0);

            /*foreach (var data in chunk.LodDataMap) {
                foreach (var datIdx in data.DatBlockIds) {
                    chunk.GetSubChunkNodes(datIdx);
                }
            }*/
            
            chunk.GetSubChunkNodes(8);

            Console.WriteLine();
        }

        public static void WriteCompressedChunksToDisk(GtChunkV8 chunk, string dir)
        {
            int i = 0;
            foreach (var data in chunk.LodDataMap) {
                var dataBlock = chunk.DataBlocks[i];
                var path = Path.Combine(dir,
                    $"DATA_{i.ToString()}_C_{dataBlock.CompressedSize.ToString()}_U_{chunk.Root.LodNodes[i].UncompressedSize}.data");
                File.WriteAllBytes(path, chunk.DataBlocks[i].CompressedData);

                int datI = 0;
                foreach (var datIdx in data.DatBlockIds) {
                    var datBlock = chunk.DatBlocks[datIdx];
                    var datPath = Path.Combine(dir,
                        $"DAT_{datIdx.ToString()}_C_{datBlock.CompressedSize.ToString()}_U_{chunk.Root.LodNodes[i].SubChunkNodes[datI].UncompressedSize}.dat");
                    File.WriteAllBytes(datPath, datBlock.CompressedData);

                    var uncompressedData = chunk.GetDecompressedSubChunk(datI).ToArray();
                    File.WriteAllBytes($"{datPath}_uncompressed", uncompressedData);
                    datI++;
                }

                i++;
            }
        }
    }
}