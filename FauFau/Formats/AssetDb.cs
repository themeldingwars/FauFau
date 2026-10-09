using System.IO;

namespace FauFau.Formats
{
    // Layout of the client's system/assetdb folder: the files are named after their asset id with 8 digits and sit in folders
    // of 1000 ids, named after the first id of the folder, e.g. 00019000/00019374.bMesh
    public static class AssetDb
    {
        public static string GetFolderName(uint assetId) => (assetId / 1000 * 1000).ToString("D8");

        public static string GetFileName(uint assetId, string extension) => assetId.ToString("D8") + extension;

        // The extension includes the dot and keeps the client's casing, e.g. ".bMesh"
        public static string GetPath(string assetDbPath, uint assetId, string extension)
        {
            return Path.Combine(assetDbPath, GetFolderName(assetId), GetFileName(assetId, extension));
        }
    }
}
