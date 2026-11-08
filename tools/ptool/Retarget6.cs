using AssetsTools.NET;
using AssetsTools.NET.Extra;

// ptool rt-data <MagicalPrincess_Data> <out> <tmp> <hlslcc-cli> <tpk>
// ptool rt-bundles <aa/StandaloneWindows64> <out> <tmp root> <hlslcc-cli> <tpk> [threads]
//
// Turns the Windows player data into Android player data: every serialized file is stamped for
// Android (13), its shaders are converted to GLES3, and BuildSettings lists GLES3 as the only API.
// Bundles are rewritten in memory and packed back to LZ4 in one go.
static class Retarget6
{
    const int Android = 13;
    const int Gles3Api = 11;

    static int Convert(Converter6 c, AssetsManager am, AssetsFileInstance i, string tag)
    {
        int seq = 0;
        var listPath = Path.Combine(c.Tmp, "list.txt");
        List<(AssetFileInfo a, AssetTypeValueField b, string name, List<(AssetTypeValueField, List<Pair6>)> passes)> jobs;
        // Leftovers from an earlier batch would pass for this batch's results (crash detection looks for .meta).
        foreach (var f in Directory.GetFiles(c.Tmp)) File.Delete(f);
        using (var list = new StreamWriter(listPath)) jobs = c.Prepare(am, i, list, ref seq);
        if (jobs.Count == 0) return 0;
        if (seq > 0) c.RunCli(listPath);
        foreach (var (a, b, n, p) in jobs) c.Finish(a, b, n, p);
        if (Environment.GetEnvironmentVariable("PRINCESS_KEEP") == null) foreach (var f in Directory.GetFiles(c.Tmp)) File.Delete(f);
        return jobs.Count;
    }

    public static void Data(string src, string dst, string tmp, string cli, string tpk)
    {
        // Shader layout for the type-tree-less player files, from one of the game's own bundles.
        var any = Directory.GetFiles(Path.Combine(src, "StreamingAssets", "aa", "StandaloneWindows64"), "*.bundle").First(f => { try { Templates.LoadFrom(f, tpk, 48); return true; } catch { return false; } });
        Console.WriteLine("Shader template from " + Path.GetFileName(any));
        Directory.CreateDirectory(dst);
        Directory.CreateDirectory(Path.Combine(dst, "Resources"));
        Directory.CreateDirectory(tmp);
        var c = new Converter6 { Tmp = tmp, Cli = cli };
        var am = new AssetsManager();
        am.LoadClassPackage(tpk);
        var names = Directory.GetFiles(src).Select(Path.GetFileName).Where(n =>
            n == "globalgamemanagers" || n == "globalgamemanagers.assets" || n == "resources.assets"
            || (n.StartsWith("level") && !n.EndsWith(".resS"))
            || (n.StartsWith("sharedassets") && n.EndsWith(".assets"))).OrderBy(x => x).ToList();
        foreach (var n in names.Append(Path.Combine("Resources", "unity_builtin_extra")))
        {
            var i = am.LoadAssetsFile(Path.Combine(src, n), false);
            am.LoadClassDatabaseFromPackage(i.file.Metadata.UnityVersion);
            int sh = Convert(c, am, i, n);
            if (n == "globalgamemanagers")
            {
                foreach (var a in i.file.GetAssetsOfType(AssetClassID.BuildSettings))
                {
                    var b = am.GetBaseField(i, a);
                    var arr = b["m_GraphicsAPIs.Array"];
                    arr.Children.RemoveRange(1, arr.Children.Count - 1);
                    arr.Children[0].AsInt = Gles3Api;
                    a.SetNewData(b);
                }
                foreach (var a in i.file.GetAssetsOfType(AssetClassID.AudioManager))
                    Console.WriteLine("AudioManager m_DisableAudio=" + am.GetBaseField(i, a)["m_DisableAudio"].AsBool);
            }
            i.file.Metadata.TargetPlatform = Android;
            using (var fs = File.Create(Path.Combine(dst, n)))
            using (var w = new AssetsFileWriter(fs))
                i.file.Write(w);
            am.UnloadAll();
            am.LoadClassPackage(tpk);
            Console.WriteLine($"wrote {n} ({sh} shaders)");
        }
        foreach (var f in Directory.GetFiles(src))
        {
            var n = Path.GetFileName(f);
            if (n.EndsWith(".resS") || n.EndsWith(".resource") || n is "app.info" or "RuntimeInitializeOnLoads.json" or "ScriptingAssemblies.json" or "boot.config")
                File.Copy(f, Path.Combine(dst, n), true);
        }
        File.WriteAllLines(Path.Combine(dst, "..", "shader-report-data.txt"), c.Log);
        Console.WriteLine($"variants={c.Total} failed={c.Failed} skipped={c.Skipped} mask-fixes={c.MaskFixes} blit-flips={c.BlitFlips} light-clamps={c.LightClamps} over-budget={c.OverBudget}");
    }

    public static void Bundles(string src, string dst, string tmpRoot, string cli, string tpk, int threads)
    {
        Directory.CreateDirectory(dst);
        var files = Directory.GetFiles(src, "*.bundle").OrderBy(f => f).ToArray();
        int done = 0, withShaders = 0, failed = 0;
        long inBytes = 0, outBytes = 0;
        var log = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var started = DateTime.Now;
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = threads }, () => (object)null, (f, _, state) =>
        {
            var o = Path.Combine(dst, Path.GetFileName(f));
            if (!File.Exists(o) || new FileInfo(o).Length == 0)
            {
                var tmp = Path.Combine(tmpRoot, Environment.CurrentManagedThreadId.ToString());
                Directory.CreateDirectory(tmp);
                var c = new Converter6 { Tmp = tmp, Cli = cli };
                try
                {
                    var am = new AssetsManager();
                    am.LoadClassPackage(tpk);
                    var bundle = am.LoadBundleFile(f, true);
                    am.LoadClassDatabaseFromPackage(bundle.file.Header.EngineVersion);
                    int sh = 0;
                    for (int k = 0; k < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; k++)
                    {
                        if (!bundle.file.IsAssetsFile(k)) continue;
                        var i = am.LoadAssetsFileFromBundle(bundle, k, false);
                        sh += Convert(c, am, i, Path.GetFileName(f));
                        i.file.Metadata.TargetPlatform = Android;
                        bundle.file.BlockAndDirInfo.DirectoryInfos[k].SetNewData(i.file);
                    }
                    var raw = new MemoryStream();
                    using (var w = new AssetsFileWriter(raw)) bundle.file.Write(w);
                    am.UnloadAll();
                    var am2 = new AssetsManager();
                    var packed = am2.LoadBundleFile(new MemoryStream(raw.ToArray()), f, false);
                    var part = o + ".part";
                    using (var fs = File.Create(part))
                    using (var w = new AssetsFileWriter(fs))
                        packed.file.Pack(w, AssetBundleCompressionType.LZ4);
                    am2.UnloadAll();
                    File.Move(part, o, true);
                    if (sh > 0) Interlocked.Increment(ref withShaders);
                    foreach (var l in c.Log) log.Enqueue(Path.GetFileName(f) + ": " + l);
                }
                catch (Exception e)
                {
                    // AssetsTools cannot parse a few files (a string it fails to decode). Those only get
                    // their target platform stamped, on the raw bytes.
                    try
                    {
                        int k = RawStamp(f, o);
                        log.Enqueue($"{Path.GetFileName(f)}: parse failed ({e.Message}); raw platform stamp x{k}, shaders NOT converted");
                    }
                    catch (Exception e2)
                    {
                        Interlocked.Increment(ref failed);
                        log.Enqueue($"{Path.GetFileName(f)}: ERROR {e.GetType().Name} {e.Message}; raw: {e2.Message}");
                    }
                }
            }
            Interlocked.Add(ref inBytes, new FileInfo(f).Length);
            if (File.Exists(o)) Interlocked.Add(ref outBytes, new FileInfo(o).Length);
            int n = Interlocked.Increment(ref done);
            if (n % 500 == 0) Console.WriteLine($"{n}/{files.Length} {(DateTime.Now - started).TotalMinutes:F1} min, shaders in {withShaders}, failed {failed}");
            return state;
        }, _ => { });
        File.WriteAllLines(Path.Combine(dst, "..", "shader-report-bundles.txt"), log);
        Console.WriteLine($"bundles={done} with-shaders={withShaders} failed={failed} {inBytes >> 20} MB -> {outBytes >> 20} MB");
    }

    // Unpacks the bundle, rewrites every serialized file's target platform (the uint right after the
    // "<unity version>\0" that opens its metadata) from 19 to 13, and packs it back to LZ4.
    static int RawStamp(string f, string o)
    {
        var am = new AssetsManager();
        var bundle = am.LoadBundleFile(f, false);
        var ver = System.Text.Encoding.ASCII.GetBytes(bundle.file.Header.EngineVersion + "\0");
        var ms = new MemoryStream();
        using (var w = new AssetsFileWriter(ms)) bundle.file.Unpack(w);
        am.UnloadAll();
        var d = ms.ToArray();
        int n = 0;
        // The bundle header carries the version too, but not followed by a platform id of 19.
        for (int i = d.AsSpan().IndexOf(ver); i >= 0 && i + ver.Length + 4 <= d.Length;)
        {
            int p = i + ver.Length;
            if (BitConverter.ToInt32(d, p) == 19) { BitConverter.TryWriteBytes(d.AsSpan(p), Android); n++; }
            int next = d.AsSpan(p).IndexOf(ver);
            i = next < 0 ? -1 : p + next;
        }
        if (n == 0) throw new InvalidDataException("no platform field found");
        var am2 = new AssetsManager();
        var packed = am2.LoadBundleFile(new MemoryStream(d), f, false);
        var part = o + ".part";
        using (var fs = File.Create(part))
        using (var w = new AssetsFileWriter(fs))
            packed.file.Pack(w, AssetBundleCompressionType.LZ4);
        am2.UnloadAll();
        File.Move(part, o, true);
        return n;
    }
}
