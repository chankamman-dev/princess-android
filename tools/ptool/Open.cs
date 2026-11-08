using AssetsTools.NET;
using AssetsTools.NET.Extra;

// Opens a serialized file directly or the first serialized file inside a bundle.
static class Open
{
    public static (AssetsManager am, AssetsFileInstance i, BundleFileInstance b) Any(string path, string tpk)
    {
        var am = new AssetsManager();
        am.LoadClassPackage(tpk);
        BundleFileInstance bi = null;
        AssetsFileInstance i;
        using (var fs = File.OpenRead(path))
        {
            var head = new byte[8];
            fs.Read(head, 0, 8);
            bool isBundle = System.Text.Encoding.ASCII.GetString(head, 0, 7) == "UnityFS";
            fs.Close();
            if (isBundle)
            {
                bi = am.LoadBundleFile(path, true);
                int k = Enumerable.Range(0, bi.file.BlockAndDirInfo.DirectoryInfos.Count).First(x => bi.file.IsAssetsFile(x));
                i = am.LoadAssetsFileFromBundle(bi, k, false);
            }
            else i = am.LoadAssetsFile(path, false);
        }
        am.LoadClassDatabaseFromPackage(i.file.Metadata.UnityVersion);
        return (am, i, bi);
    }
}

static class Tpl
{
    // A bundle of the game's that carries the Shader type tree (see Templates).
    public const string ShaderBundle = @"H:\SteamLibrary\steamapps\common\Magical Princess\MagicalPrincess_Data\StreamingAssets\aa\StandaloneWindows64\02374ae6b759dcd642314fefca162c6b.bundle";
    public static void Ensure(AssetsTools.NET.Extra.AssetsFileInstance i, string tpk)
    {
        if (!i.file.Metadata.TypeTreeEnabled && !Templates.Has(48)) Templates.LoadFrom(ShaderBundle, tpk, 48);
    }
}
