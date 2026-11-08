using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Mono.Cecil;
using Mono.Cecil.Cil;
static class ILDump
{
    // ildump <dll> <type substring> [method substring]
    public static void Run(string dll, string type, string? method)
    {
        var r = new DefaultAssemblyResolver(); r.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(dll))!);
        var m = ModuleDefinition.ReadModule(dll, new ReaderParameters { AssemblyResolver = r });
        foreach (var t in AllTypes(m.Types))
        {
            if (!t.FullName.Contains(type)) continue;
            Console.WriteLine($"== {t.FullName}");
            foreach (var md in t.Methods)
            {
                if (method != null && !md.Name.Contains(method)) { continue; }
                Console.WriteLine($"  {md.FullName}");
                if (method == null || !md.HasBody) continue;
                foreach (var i in md.Body.Instructions) Console.WriteLine($"    {i}");
            }
        }
    }
    static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> ts)
    {
        foreach (var t in ts) { yield return t; foreach (var n in AllTypes(t.NestedTypes)) yield return n; }
    }
}
static class EnumDump
{
    public static void Run(string dll, string type)
    {
        var m = ModuleDefinition.ReadModule(dll);
        foreach (var t in m.Types.Concat(m.Types.SelectMany(x => x.NestedTypes)))
            if (t.IsEnum && t.FullName.Contains(type))
            {
                Console.WriteLine($"== {t.FullName}");
                foreach (var f in t.Fields) if (f.HasConstant) Console.WriteLine($"  {f.Name} = {f.Constant}");
            }
    }
}
static class StrRefs
{
    public static void Run(string dll, string needle)
    {
        var r = new DefaultAssemblyResolver(); r.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(dll))!);
        var m = ModuleDefinition.ReadModule(dll, new ReaderParameters { AssemblyResolver = r });
        foreach (var t in m.GetTypes()) foreach (var md in t.Methods.Where(x => x.HasBody))
            foreach (var i in md.Body.Instructions)
                if ((i.Operand is string s && s.Contains(needle)) || (i.Operand is MemberReference mr && mr.FullName.Contains(needle)))
                    Console.WriteLine($"{md.FullName}  @{i.Offset:x4}  {i.Operand}");
    }
}

// ptool matfind <bundle dir> <tpk> <property> [threads]: materials whose saved properties include
// <property>; prints bundle, material, keywords and the shader it points at (file index + path id,
// resolved to a bundle name through the externals).
static class MatFind
{
    public static void Run(string dir, string tpk, string prop, int threads)
    {
        var files = Directory.GetFiles(dir, "*.bundle").OrderBy(f => f).ToArray();
        int done = 0;
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = threads }, f =>
        {
            var am = new AssetsManager();
            am.LoadClassPackage(tpk);
            try
            {
                var bi = am.LoadBundleFile(f, true);
                am.LoadClassDatabaseFromPackage(bi.file.Header.EngineVersion);
                for (int k = 0; k < bi.file.BlockAndDirInfo.DirectoryInfos.Count; k++)
                {
                    if (!bi.file.IsAssetsFile(k)) continue;
                    var ai = am.LoadAssetsFileFromBundle(bi, k, false);
                    foreach (var a in ai.file.GetAssetsOfType(21))
                    {
                        var b = am.GetBaseField(ai, a);
                        var sp = b["m_SavedProperties"];
                        bool hit = new[] { "m_Colors", "m_Floats", "m_TexEnvs" }.Any(s => sp[s]["Array"].Children.Any(c => c["first"].AsString == prop));
                        if (!hit) continue;
                        int fid = b["m_Shader"]["m_FileID"].AsInt; long pid = b["m_Shader"]["m_PathID"].AsLong;
                        string ext = fid == 0 ? "self" : ai.file.Metadata.Externals[fid - 1].PathName;
                        var kw = b["m_ValidKeywords"].IsDummy ? "" : string.Join(",", b["m_ValidKeywords"]["Array"].Children.Select(x => x.AsString));
                        lock (files) Console.WriteLine($"{Path.GetFileName(f)} mat={b["m_Name"].AsString} shader={ext}:{pid} kw=[{kw}]");
                    }
                }
            }
            catch (Exception e) { lock (files) Console.WriteLine($"ERR {Path.GetFileName(f)} {e.Message}"); }
            am.UnloadAll();
            int n = Interlocked.Increment(ref done);
            if (n % 2000 == 0) lock (files) Console.WriteLine($"# {n}/{files.Length}");
        });
    }
}
