using AssetsTools.NET;
using AssetsTools.NET.Extra;

// Player data files carry no type trees, so AssetsTools falls back to classdata.tpk, whose Unity 6
// Shader layout is not 6000.3's. The game's own bundles do carry type trees (same engine build), so
// those are the templates for any class we rewrite in the player data.
static class Templates
{
    static readonly Dictionary<int, AssetTypeTemplateField> byClass = new();

    public static void LoadFrom(string bundle, string tpk, params int[] classIds)
    {
        var am = new AssetsManager();
        am.LoadClassPackage(tpk);
        var bi = am.LoadBundleFile(bundle, true);
        var ai = am.LoadAssetsFileFromBundle(bi, 0, false);
        foreach (var id in classIds)
        {
            var tt = ai.file.Metadata.TypeTreeTypes.FirstOrDefault(t => t.TypeId == id);
            if (tt == null) throw new InvalidDataException($"{bundle}: no type tree for class {id}");
            var tf = new AssetTypeTemplateField();
            tf.FromTypeTree(tt);
            byClass[id] = tf;
        }
    }

    public static bool Has(int classId) => byClass.ContainsKey(classId);

    // Base field of an object, through the bundle-derived template when the file itself has no type tree.
    public static AssetTypeValueField Get(AssetsManager am, AssetsFileInstance i, AssetFileInfo a)
    {
        if (i.file.Metadata.TypeTreeEnabled || !byClass.TryGetValue(a.TypeId, out var tf)) return am.GetBaseField(i, a);
        lock (tf)
        {
            var r = i.file.Reader;
            lock (r) return tf.MakeValue(r, a.GetAbsoluteByteOffset(i.file));
        }
    }

    // ptool tplcmp <bundle> <assets file> <tpk> <classId>: field-by-field diff, tpk template vs the bundle's type tree.
    public static void Compare(string bundle, string file, string tpk, int classId)
    {
        LoadFrom(bundle, tpk, classId);
        var am = new AssetsManager();
        am.LoadClassPackage(tpk);
        var i = am.LoadAssetsFile(file, false);
        am.LoadClassDatabaseFromPackage(i.file.Metadata.UnityVersion);
        Console.WriteLine("tpk picked for " + i.file.Metadata.UnityVersion + ": " + am.ClassPackage.Header);
        var a = i.file.GetAssetsOfType(classId).First();
        var t1 = am.GetTemplateBaseField(i, a);
        var l1 = new List<string>(); Flat(t1, "", l1);
        var l2 = new List<string>(); Flat(byClass[classId], "", l2);
        Console.WriteLine($"tpk fields={l1.Count} bundle fields={l2.Count}");
        foreach (var x in l1.Except(l2).Take(30)) Console.WriteLine("  only tpk:    " + x);
        foreach (var x in l2.Except(l1).Take(30)) Console.WriteLine("  only bundle: " + x);
    }

    static void Flat(AssetTypeTemplateField t, string p, List<string> l)
    {
        var q = p + "/" + t.Name + ":" + t.Type;
        l.Add(q);
        foreach (var c in t.Children) Flat(c, q, l);
    }
}
