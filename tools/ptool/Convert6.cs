using AssetsTools.NET;
using AssetsTools.NET.Extra;
using K4os.Compression.LZ4;
using System.Diagnostics;
using System.Text;

// D3D11 -> GLES3 shader conversion for Unity 6 (6000.3) Shader objects.
//
// Port of lomtool's 2020.3 converter. What changed in Unity 6:
//   - sub-programs live in m_PlayerSubPrograms[tier][i] (D3D builds put them all in slot 3, GLES
//     builds fill tiers 0..2 with the same list), with one keyword index list each;
//   - every sub-program has a parameter record of its own (m_ParameterBlobIndices[tier][i]) next to
//     its code record in the platform slice;
//   - stageCounts gives the number of program stages per platform: 2 for D3D, 1 for GLES (vertex
//     and fragment text travel together in progVertex).
// The GLES3 layout written here is the one the Android player's own "unity default resources" uses.
class Pair6
{
    public CodeRec V, F; public ParamRec PV, PF; public Refl RV, RF; public string Prefix;
    public long Req;
    public List<ushort[]> Targets = new();
}

class Converter6
{
    public string Tmp, Cli;
    public int Total, Failed, Skipped, MaskFixes, BlitFlips, LightClamps, OverBudget;
    public List<string> Log = new();

    record Sub(AssetTypeValueField F, int Code, int Par, ushort[] Kw, long Req);

    static string Key(ushort[] k) => string.Join(",", k);

    static List<Sub> Subs(AssetTypeValueField prog, Func<int, bool> type)
    {
        var res = new List<Sub>();
        var outers = prog["m_PlayerSubPrograms.Array"].Children;
        for (int oi = 0; oi < outers.Count; oi++)
        {
            var subs = outers[oi]["Array"].Children;
            var pis = prog["m_ParameterBlobIndices.Array"].Children[oi]["Array"].Children;
            for (int j = 0; j < subs.Count; j++)
            {
                if (!type(subs[j]["m_GpuProgramType"].AsSByte)) continue;
                var kw = subs[j]["m_KeywordIndices.Array"].Children.Select(c => c.AsUShort).OrderBy(x => x).ToArray();
                if (res.Any(r => Key(r.Kw) == Key(kw))) continue; // same set in another tier
                res.Add(new Sub(subs[j], (int)subs[j]["m_BlobIndex"].AsUInt, (int)pis[j].AsUInt, kw, subs[j]["m_ShaderRequirements"].AsLong));
            }
        }
        return res;
    }

    // The entry whose keywords are the largest subset of the target's.
    static Sub Best(List<Sub> list, ushort[] t) =>
        list.Where(s => s.Kw.All(t.Contains)).OrderByDescending(s => s.Kw.Length).FirstOrDefault();

    public List<(AssetFileInfo a, AssetTypeValueField b, string name, List<(AssetTypeValueField pass, List<Pair6> pairs)> passes)> Prepare(AssetsManager am, AssetsFileInstance i, StreamWriter list, ref int seq)
    {
        var res = new List<(AssetFileInfo, AssetTypeValueField, string, List<(AssetTypeValueField, List<Pair6>)>)>();
        foreach (var a in i.file.GetAssetsOfType(AssetClassID.Shader))
        {
            var b = Templates.Get(am, i, a);
            var nm = b["m_ParsedForm.m_Name"].AsString;
            var plats = b["platforms.Array"].Children.Select(c => c.AsInt).ToList();
            if (plats.Contains(9) || !plats.Contains(4)) { Skipped++; continue; }
            var d = Slice6.Get(b, 4);
            var t = Rd.Table(d);
            var codes = new Dictionary<int, CodeRec>();
            var pars = new Dictionary<int, ParamRec>();
            CodeRec C(int k) => codes.TryGetValue(k, out var p) ? p : codes[k] = CodeRec.Read(d, t[k].off, t[k].len);
            ParamRec P(int k) => pars.TryGetValue(k, out var p) ? p : pars[k] = ParamRec.Read(d, t[k].off, t[k].len);
            var passes = new List<(AssetTypeValueField, List<Pair6>)>();
            int pass = 0;
            foreach (var pa in b["m_ParsedForm.m_SubShaders.Array"].Children.SelectMany(s => s["m_Passes.Array"].Children))
            {
                var pairs = new List<Pair6>();
                passes.Add((pa, pairs));
                var names = pa["m_NameIndices.Array"].Children.ToDictionary(e => e["second"].AsInt, e => e["first"].AsString);
                var dvs = Subs(pa["progVertex"], x => x is 15 or 16);
                var dfs = Subs(pa["progFragment"], x => x is 17 or 18);
                foreach (var st in new[] { "progGeometry", "progHull", "progDomain" })
                    if (!pa[st].IsDummy && pa[st + ".m_PlayerSubPrograms.Array"].Children.Any(o => o["Array"].Children.Count > 0))
                        Log.Add($"{nm} pass{pass}: has {st}, ignored");
                if (dvs.Count == 0 || dfs.Count == 0) { if (dvs.Count + dfs.Count > 0) Log.Add($"{nm} pass{pass}: missing a stage"); pass++; continue; }

                // Every keyword set either stage has, and (when it stays small) every union of one of
                // each, so a vertex-only and a fragment-only keyword switched on together still match.
                var targets = new Dictionary<string, ushort[]>();
                foreach (var s in dvs.Concat(dfs)) targets[Key(s.Kw)] = s.Kw;
                if (dvs.Count * dfs.Count <= 512)
                    foreach (var v in dvs) foreach (var f in dfs)
                    {
                        var u = v.Kw.Union(f.Kw).OrderBy(x => x).ToArray();
                        targets[Key(u)] = u;
                    }
                var byPair = new Dictionary<(int, int), Pair6>();
                foreach (var tk in targets.Values)
                {
                    var sv = Best(dvs, tk);
                    var sf = Best(dfs, tk);
                    if (sv == null || sf == null) { Log.Add($"{nm} pass{pass}: no {(sv == null ? "vertex" : "fragment")} for [{Key(tk)}]"); continue; }
                    var pk = (sv.Code, sf.Code);
                    if (byPair.TryGetValue(pk, out var have)) { have.Targets.Add(tk); continue; }
                    var v = C(sv.Code); var f = C(sf.Code);
                    var pv = P(sv.Par); var pf = P(sf.Par);
                    var rv = new Refl(); var rf = new Refl();
                    rv.AddProg(pv); rf.AddProg(pf);
                    rv.AddCommon(pa["progVertex.m_CommonParameters"], names, null);
                    rf.AddCommon(pa["progFragment.m_CommonParameters"], names, null);
                    if (rv.Bufs.Count + rf.Bufs.Count > 0) { Log.Add($"{nm} pass{pass}: skipped buffer variant [{Key(tk)}]"); continue; }
                    byte[] dv, df;
                    try { dv = Dxbc.Strip(v.Code); df = Dxbc.Strip(f.Code); }
                    catch (Exception e) { Log.Add($"{nm} pass{pass}: {e.Message}"); continue; }
                    var pre = Path.Combine(Tmp, seq++.ToString());
                    File.WriteAllBytes(pre + ".vs", Dxbc.WithRdef(dv, rv.Rdef(Dxbc.VersionToken(dv))));
                    File.WriteAllBytes(pre + ".fs", Dxbc.WithRdef(df, rf.Rdef(Dxbc.VersionToken(df))));
                    list.WriteLine($"{pre}.vs {pre}.fs {pre}");
                    var pair = new Pair6 { V = v, F = f, PV = pv, PF = pf, RV = rv, RF = rf, Prefix = pre, Req = sv.Req | sf.Req };
                    pair.Targets.Add(tk);
                    byPair[pk] = pair;
                    pairs.Add(pair);
                }
                pass++;
            }
            res.Add((a, b, nm, passes));
            Total += passes.Sum(p => p.Item2.Count);
        }
        return res;
    }

    public void RunCli(string listFile)
    {
        // A crash on one program must not lose the rest of the batch: drop it and carry on.
        var lines = File.ReadAllLines(listFile).ToList();
        while (lines.Count > 0)
        {
            File.WriteAllLines(listFile, lines);
            var psi = new ProcessStartInfo(Cli, $"{ShTest.Flags:x} 2 \"{listFile}\"") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            var p = Process.Start(psi);
            var err = p.StandardError.ReadToEndAsync();
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            var e = err.Result;
            if (e.Length > 0) Log.Add("hlslcc: " + e.Substring(0, Math.Min(e.Length, 400)));
            if (p.ExitCode >= 0) return;
            int k = lines.FindIndex(l => !File.Exists(l.Split(' ')[2] + ".meta"));
            if (k < 0) return;
            Console.WriteLine("hlslcc crashed on " + lines[k]);
            lines.RemoveRange(0, k + 1);
        }
    }

    // The GLES3 records for one translated pair, laid out like the Android player's own shaders.
    static (CodeRec code, ParamRec par) Build(Pair6 p, string glsl, Dictionary<string, int> clamped)
    {
        var code = new CodeRec { Version = p.V.Version, Type = Gpu.Gles3, SourceMap = p.V.SourceMap, Code = Encoding.UTF8.GetBytes(glsl) };
        var par = new ParamRec { Version = p.V.Version };
        var g = new Group { Name = "$Globals" };
        int off = 0;
        var seen = new HashSet<string>();
        void Add(Refl r, bool mats)
        {
            foreach (var c in r.Cbs)
                foreach (var v in c.Vars.Where(v => (v.IsMatrix != 0) == mats))
                    if (glsl.Contains(v.Name) && seen.Add(v.Name))
                    {
                        int arr = clamped.TryGetValue(v.Name, out var cut) && v.ArraySize > cut ? cut : v.ArraySize;
                        int elems = Math.Max(1, arr);
                        if (mats) { g.Params.Add(new Param { Name = v.Name, Type = v.Type, Rows = v.Rows, Cols = v.Cols, IsMatrix = 1, ArraySize = arr, Index = off }); off += 16 * v.Rows * elems; }
                        else { g.Params.Add(new Param { Name = v.Name, Type = v.Type, Rows = 1, Cols = v.Cols, ArraySize = arr, Index = off }); off += 16 * elems; }
                    }
        }
        Add(p.RV, false); Add(p.RF, false); Add(p.RV, true); Add(p.RF, true);
        g.Used = off;
        par.Groups.Add(new Group());
        if (g.Params.Count > 0) par.Groups.Add(g);
        // Textures: the location HLSLcc assigned, reported as "tex <name> <bind> <sampler> <ms> <dim> <uav>".
        foreach (var l in File.ReadAllLines(p.Prefix + ".meta"))
        {
            if (!l.StartsWith("tex ")) continue;
            var f = l.Split(' ');
            if (par.Bindings.Any(b => b.Name == f[1])) continue;
            var t = p.RF.Texs.Concat(p.RV.Texs).FirstOrDefault(x => x.Name == f[1]);
            par.Bindings.Add(new Binding { Name = f[1], Type = 0, Index = int.Parse(f[2]), Extra = 0, Extra2 = t?.Dim ?? 4 });
        }
        if (g.Params.Count > 0) par.Bindings.Add(new Binding { Name = "$Globals", Type = 1, Index = 0 });
        return (code, par);
    }

    public void Finish(AssetFileInfo a, AssetTypeValueField b, string nm, List<(AssetTypeValueField pass, List<Pair6> pairs)> passes)
    {
        var entries = new List<byte[]>();
        var byHash = new Dictionary<string, int>();
        int Entry(byte[] bytes)
        {
            var k = Convert.ToBase64String(System.Security.Cryptography.SHA1.HashData(bytes));
            if (!byHash.TryGetValue(k, out var idx)) { idx = entries.Count; entries.Add(bytes); byHash[k] = idx; }
            return idx;
        }
        int variants = 0, programs = 0, clampsBefore = LightClamps;
        foreach (var (pa, pairs) in passes)
        {
            var vtx = pa["progVertex"];
            // Built fresh: (code, params, keywords, requirements) for every keyword set.
            var subs = new List<(int code, int par, ushort[] kw, long req)>();
            foreach (var p in pairs)
            {
                if (!File.Exists(p.Prefix + ".glsl")) { Failed++; Log.Add($"{nm}: FAILED {p.Prefix} [{string.Join(" ", p.Targets.Select(Key))}]"); continue; }
                int fixes = 0;
                var glsl = Fixups6.Apply(File.ReadAllText(p.Prefix + ".glsl"), ref fixes);
                int flips = 0;
                glsl = Fixups6.FlipBlitUv(glsl, ref flips);
                var clamped = new Dictionary<string, int>();
                int cuts = 0;
                glsl = Fixups6.ClampLights(glsl, clamped, ref cuts);
                if (cuts > 0) LightClamps++;
                int vecs = Fixups6.UniformVectors(glsl);
                if (vecs > 256) { OverBudget++; Log.Add($"{nm}: {vecs} uniform vectors in {p.Prefix} [{string.Join(" ", p.Targets.Select(Key))}]"); }
                BlitFlips += flips;
                MaskFixes += fixes;
                var (code, par) = Build(p, glsl, clamped);
                int ci = Entry(code.Write()), pi = Entry(par.Write());
                programs++;
                foreach (var tk in p.Targets) { subs.Add((ci, pi, tk, p.Req)); variants++; }
            }

            // Both stages' keyword masks now apply to the one combined program.
            var mask = vtx["m_SerializedKeywordStateMask.Array"];
            var union = mask.Children.Select(c => c.AsUShort).Concat(pa["progFragment.m_SerializedKeywordStateMask.Array"].Children.Select(c => c.AsUShort)).Distinct().OrderBy(x => x).ToArray();
            Fill(mask, union, (f, v) => f.AsUShort = v);

            var outers = vtx["m_PlayerSubPrograms.Array"].Children;
            var pouters = vtx["m_ParameterBlobIndices.Array"].Children;
            // A template element for each array, taken before anything is cleared.
            // (A pass without vertex programs only gets its other stages cleared.)
            var spArr = outers.FirstOrDefault(o => o["Array"].Children.Count > 0)?["Array"];
            if (spArr == null) subs.Clear();
            for (int oi = 0; oi < outers.Count; oi++)
            {
                var arr = outers[oi]["Array"];
                var parr = pouters[oi]["Array"];
                var newSubs = new List<AssetTypeValueField>();
                var newPars = new List<AssetTypeValueField>();
                if (oi < 3)
                    foreach (var s in subs)
                    {
                        var sp = ValueBuilder.DefaultValueFieldFromArrayTemplate(spArr);
                        sp["m_BlobIndex"].AsUInt = (uint)s.code;
                        Fill(sp["m_KeywordIndices.Array"], s.kw, (f, v) => f.AsUShort = v);
                        sp["m_ShaderRequirements"].AsLong = s.req;
                        sp["m_GpuProgramType"].AsSByte = Gpu.Gles3;
                        newSubs.Add(sp);
                        var pe = ValueBuilder.DefaultValueFieldFromArrayTemplate(parr);
                        pe.AsUInt = (uint)s.par;
                        newPars.Add(pe);
                    }
                arr.Children = newSubs;
                parr.Children = newPars;
            }
            foreach (var st in new[] { "progFragment", "progGeometry", "progHull", "progDomain", "progRayTracing" })
            {
                if (pa[st].IsDummy) continue;
                foreach (var o in pa[st + ".m_PlayerSubPrograms.Array"].Children) o["Array"].Children.Clear();
                foreach (var o in pa[st + ".m_ParameterBlobIndices.Array"].Children) o["Array"].Children.Clear();
                ClearSet(pa[st + ".m_CommonParameters"]);
            }
            ClearSet(vtx["m_CommonParameters"]);
        }

        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(entries.Count);
        int off = 4 + entries.Count * 12;
        foreach (var e in entries) { w.Write(off); w.Write(e.Length); w.Write(0); off += e.Length; }
        foreach (var e in entries) w.Write(e);
        var raw = ms.ToArray();
        var comp = new byte[LZ4Codec.MaximumOutputSize(raw.Length)];
        int cl = LZ4Codec.Encode(raw, 0, raw.Length, comp, 0, comp.Length, LZ4Level.L09_HC);
        b["compressedBlob.Array"].AsByteArray = comp.AsSpan(0, cl).ToArray();
        SetOne(b["platforms.Array"], 9);
        SetNest(b["offsets.Array"], 0);
        SetNest(b["compressedLengths.Array"], cl);
        SetNest(b["decompressedLengths.Array"], raw.Length);
        SetOne(b["stageCounts.Array"], 1);
        a.SetNewData(b);
        Log.Add($"{nm}: {variants} variants, {programs} programs, {entries.Count} entries, {raw.Length / 1024} KB{(LightClamps > clampsBefore ? $", light arrays cut in {LightClamps - clampsBefore}" : "")}");
    }

    static void Fill(AssetTypeValueField arr, ushort[] values, Action<AssetTypeValueField, ushort> set)
    {
        var list = new List<AssetTypeValueField>();
        foreach (var v in values)
        {
            var e = ValueBuilder.DefaultValueFieldFromArrayTemplate(arr);
            set(e, v);
            list.Add(e);
        }
        arr.Children = list;
    }

    static void ClearSet(AssetTypeValueField s)
    {
        if (s.IsDummy) return;
        foreach (var c in s.Children) if (!c["Array"].IsDummy) c["Array"].Children.Clear();
    }
    static void SetOne(AssetTypeValueField arr, int v) { arr.Children.RemoveRange(1, arr.Children.Count - 1); arr.Children[0].AsInt = v; }
    static void SetNest(AssetTypeValueField arr, long v)
    {
        arr.Children.RemoveRange(1, arr.Children.Count - 1);
        var inner = arr.Children[0]["Array"];
        inner.Children.RemoveRange(1, inner.Children.Count - 1);
        inner.Children[0].AsUInt = (uint)v;
    }
}
