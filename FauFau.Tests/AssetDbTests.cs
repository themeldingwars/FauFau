using System.IO;
using FauFau.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class AssetDbTests
    {
        [TestMethod]
        [DataRow(0U, "00000000")]
        [DataRow(999U, "00000000")]
        [DataRow(19374U, "00019000")]
        [DataRow(1234567U, "01234000")]
        public void GetFolderName_RoundsDownToThousand(uint assetId, string expected)
        {
            string folder = AssetDb.GetFolderName(assetId);

            folder.ShouldBe(expected);
        }

        [TestMethod]
        public void GetPath_CombinesFolderAndFile()
        {
            string path = AssetDb.GetPath("assetdb", 19374, ".bMesh");

            path.ShouldBe(Path.Combine("assetdb", "00019000", "00019374.bMesh"));
        }
    }
}
