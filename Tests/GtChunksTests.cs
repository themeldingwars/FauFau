using System;
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
            
            Console.WriteLine();
        }
    }
}