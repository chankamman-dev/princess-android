using AssetsTools.NET;
using AssetsTools.NET.Extra;

// Probe tool for the Magical Princess Android feasibility study.
//   ptool inventory <aa dir> <tpk> [max]   class counts across bundles + shader list with GPU program platforms
if (args[0] == "tplcmp") Templates.Compare(args[1], args[2], args[3], int.Parse(args[4]));
else if (args[0] == "rt-data")Retarget6.Data(args[1], args[2], args[3], args[4], args[5]);
else if (args[0] == "rt-bundles") Retarget6.Bundles(args[1], args[2], args[3], args[4], args[5], args.Length > 6 ? int.Parse(args[6]) : 8);
else if (args[0] == "shtest")ShTest.Run(args[1], args[2], args[3], args[4], args[5], args.Length > 6 ? int.Parse(args[6]) : 0);
else if (args[0] == "shrec")ShRec.Run(args[1], args[2], args[3], args.Length > 4 ? int.Parse(args[4]) : 4);
else if (args[0] == "stage")Stage.Run(args[1], args[2], args[3], args[4], args[5]);
else if (args[0] == "shdump")ShDump.Run(args[1], args[2], args[3], args[4]);
else if (args[0] == "fields")Fields.Run(args[1], int.Parse(args[2]), args[3]);
else if (args[0] == "inventory") Inventory.Run(args[1], args[2], args.Length > 3 ? int.Parse(args[3]) : int.MaxValue);
else if (args[0] == "ildump") ILDump.Run(args[1], args[2], args.Length > 3 ? args[3] : null);
else if (args[0] == "enumdump") EnumDump.Run(args[1], args[2]);
else if (args[0] == "texstat") TexStat.Run(args[1], args[2], args.Length > 3 ? int.Parse(args[3]) : 10);
else if (args[0] == "strrefs") StrRefs.Run(args[1], args[2]);
else if (args[0] == "matfind") MatFind.Run(args[1], args[2], args[3], args.Length > 4 ? int.Parse(args[4]) : 8);

static class Inventory
{
    public static void Run(string dir, string tpk, int max)
    {
        var am = new AssetsManager();
        am.LoadClassPackage(tpk);
        var classCount = new SortedDictionary<int, int>();
        var shaders = new SortedDictionary<string, string>();
        var flags = new Dictionary<string, int>();
        var files = Directory.GetFiles(dir, "*.bundle", SearchOption.AllDirectories).OrderBy(f => f).Take(max).ToArray();
        long total = 0; string ver = null; int n = 0;
        foreach (var f in files)
        {
            total += new FileInfo(f).Length;
            BundleFileInstance bi;
            try { bi = am.LoadBundleFile(f, true); } catch (Exception e) { Console.WriteLine($"ERR {Path.GetFileName(f)} {e.Message}"); continue; }
            var comp = bi.file.BlockAndDirInfo.BlockInfos.Select(b => (b.Flags & 0x3f).ToString()).Distinct();
            foreach (var c in comp) flags[c] = flags.GetValueOrDefault(c) + 1;
            for (int k = 0; k < bi.file.BlockAndDirInfo.DirectoryInfos.Count; k++)
            {
                if (!bi.file.IsAssetsFile(k)) continue;
                AssetsFileInstance ai;
                try { ai = am.LoadAssetsFileFromBundle(bi, k, false); } catch (Exception e) { Console.WriteLine($"ERR {Path.GetFileName(f)}#{k} {e.Message}"); continue; }
                ver ??= ai.file.Metadata.UnityVersion;
                if (ver != null && am.ClassDatabase == null) am.LoadClassDatabaseFromPackage(ver);
                foreach (var a in ai.file.AssetInfos)
                {
                    classCount[a.TypeId] = classCount.GetValueOrDefault(a.TypeId) + 1;
                    if (a.TypeId == 28 || a.TypeId == 329)
                    {
                        try { var tb = am.GetBaseField(ai, a); var key = a.TypeId == 28 ? "tex fmt " + tb["m_TextureFormat"].AsInt : "video " + tb["m_Name"].AsString + " " + tb["m_OriginalPath"].AsString; flags[key] = flags.GetValueOrDefault(key) + 1; } catch { }
                    }
                    if (a.TypeId != 48) continue;
                    try
                    {
                        var b = am.GetBaseField(ai, a);
                        var name = b["m_ParsedForm"]["m_Name"].AsString;
                        var plats = string.Join(",", b["platforms"]["Array"].Children.Select(x => x.AsUInt));
                        long bytes = b["compressedLengths"]["Array"].Children.Sum(x => x["Array"].Children.Sum(y => (long)y.AsUInt));
                        shaders[name + " @" + Path.GetFileName(f)] = $"plats=[{plats}] blob={bytes / 1024}KB";
                    }
                    catch (Exception e) { shaders["?" + a.PathId + " @" + Path.GetFileName(f)] = "ERR " + e.Message; }
                }
            }
            am.UnloadAll(false);
            am.LoadClassPackage(tpk);
            if (ver != null) am.LoadClassDatabaseFromPackage(ver);
            if (++n % 2000 == 0) Console.Error.WriteLine($"{n}/{files.Length}");
        }
        Console.WriteLine($"bundles={files.Length} bytes={total / 1048576}MB unity={ver}");
        Console.WriteLine("block compression: " + string.Join(" ", flags.Select(kv => $"{kv.Key}:{kv.Value}")));
        foreach (var kv in classCount) Console.WriteLine($"class {kv.Key}: {kv.Value}");
        Console.WriteLine($"shaders: {shaders.Count}");
        foreach (var kv in shaders) Console.WriteLine($"  {kv.Key} {kv.Value}");
    }
}

// ptool fields <bundle> <classId> <tpk>   prints the type tree (field names/types) of the first object of a class
static class Fields
{
    public static void Run(string bundle, int cls, string tpk)
    {
        var am = new AssetsManager();
        am.LoadClassPackage(tpk);
        var bi = am.LoadBundleFile(bundle, true);
        var ai = am.LoadAssetsFileFromBundle(bi, 0, false);
        am.LoadClassDatabaseFromPackage(ai.file.Metadata.UnityVersion);
        Console.WriteLine($"typetree={ai.file.Metadata.TypeTreeEnabled} target={ai.file.Metadata.TargetPlatform} ver={ai.file.Header.Version}");
        var a = ai.file.GetAssetsOfType(cls).First();
        var b = am.GetBaseField(ai, a);
        Walk(b.TemplateField, 0);
    }
    static void Walk(AssetTypeTemplateField t, int d)
    {
        if (d > 16) return;
        Console.WriteLine(new string(' ', d * 2) + t.Type + " " + t.Name);
        foreach (var c in t.Children) Walk(c, d + 1);
    }
}

// ptool texstat <bundle dir> <tpk> [stride]   Texture2D sizes by mip count and the mipmap-limit flags,
// over every stride-th bundle: how much a global texture mipmap limit would save.
static class TexStat
{
    public static void Run(string dir, string tpk, int stride)
    {
        var am = new AssetsManager();
        am.LoadClassPackage(tpk);
        var files = Directory.GetFiles(dir, "*.bundle", SearchOption.AllDirectories).OrderBy(f => f).Where((f, i) => i % stride == 0).ToArray();
        string ver = null;
        long withMips = 0, noMips = 0, ignored = 0; int nWith = 0, nNo = 0, nIgn = 0;
        var big = new List<(long, string)>();
        var groups = new Dictionary<string, int>();
        foreach (var f in files)
        {
            BundleFileInstance bi;
            try { bi = am.LoadBundleFile(f, true); } catch { continue; }
            for (int k = 0; k < bi.file.BlockAndDirInfo.DirectoryInfos.Count; k++)
            {
                if (!bi.file.IsAssetsFile(k)) continue;
                AssetsFileInstance ai;
                try { ai = am.LoadAssetsFileFromBundle(bi, k, false); } catch { continue; }
                ver ??= ai.file.Metadata.UnityVersion;
                if (ver != null && am.ClassDatabase == null) am.LoadClassDatabaseFromPackage(ver);
                foreach (var a in ai.file.GetAssetsOfType(28))
                {
                    try
                    {
                        var t = am.GetBaseField(ai, a);
                        int mips = t["m_MipCount"].AsInt, w = t["m_Width"].AsInt, h = t["m_Height"].AsInt;
                        long size = t["m_CompleteImageSize"].AsLong;
                        var ign = t["m_IgnoreMipmapLimit"];
                        bool ig = !ign.IsDummy && ign.AsBool;
                        var g = t["m_MipmapLimitGroupName"];
                        if (!g.IsDummy && g.AsString.Length > 0) groups[g.AsString] = groups.GetValueOrDefault(g.AsString) + 1;
                        if (ig) { ignored += size; nIgn++; }
                        else if (mips > 1) { withMips += size; nWith++; }
                        else { noMips += size; nNo++; }
                        big.Add((size, $"{t["m_Name"].AsString} {w}x{h} mips={mips} fmt={t["m_TextureFormat"].AsInt}{(ig ? " ignoreLimit" : "")}"));
                    }
                    catch (Exception e) { Console.WriteLine("ERR " + e.Message); }
                }
            }
            am.UnloadAll(false);
            am.LoadClassPackage(tpk);
            if (ver != null) am.LoadClassDatabaseFromPackage(ver);
        }
        Console.WriteLine($"sampled {files.Length} bundles (every {stride}th), unity {ver}");
        Console.WriteLine($"with mips: {nWith} textures, {withMips / 1048576} MB");
        Console.WriteLine($"no mips:   {nNo} textures, {noMips / 1048576} MB");
        Console.WriteLine($"ignore mipmap limit: {nIgn} textures, {ignored / 1048576} MB");
        Console.WriteLine("limit groups: " + string.Join(", ", groups.Select(kv => kv.Key + "=" + kv.Value)));
        foreach (var g in big.GroupBy(x => x.Item2.Substring(x.Item2.IndexOf("fmt=")).Split(' ')[0] + (x.Item2.Contains("mips=1 ") ? " nomip" : " mip"))) Console.WriteLine($"  {g.Key}: {g.Count()} tex, {g.Sum(x => x.Item1) / 1048576} MB");
        foreach (var b in big.OrderByDescending(x => x.Item1).Take(8)) Console.WriteLine($"  {b.Item1 / 1024} KB {b.Item2}");
    }
}
