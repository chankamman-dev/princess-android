using AssetsTools.NET;
using AssetsTools.NET.Extra;
using K4os.Compression.LZ4;
using System.Text;

// ptool shdump <file|bundle> <shader name|*> <outdir> <tpk>
// Shows how a Unity 6 Shader object stores its programs: blob layout per platform and the
// sub-program records of the first pass, and writes each decompressed slice to <outdir>.
static class ShDump
{
    public static void Run(string file, string name, string outDir, string tpk)
    {
        var (am, i, _) = Open.Any(file, tpk);
        Tpl.Ensure(i, tpk);
        Directory.CreateDirectory(outDir);
        Console.WriteLine($"unity={i.file.Metadata.UnityVersion} target={i.file.Metadata.TargetPlatform}");
        foreach (var a in i.file.GetAssetsOfType(AssetClassID.Shader))
        {
            var b = Templates.Get(am, i, a);
            var nm = b["m_ParsedForm.m_Name"].AsString;
            if (name == "*") { Console.WriteLine($"  {a.PathId} {nm} plats={string.Join(",", b["platforms.Array"].Children.Select(c => c.AsInt))}"); continue; }
            if (nm != name) continue;
            Console.WriteLine($"--- {nm} path={a.PathId}");
            Console.WriteLine("platforms: " + string.Join(",", b["platforms.Array"].Children.Select(c => c.AsInt)));
            foreach (var f in new[] { "offsets", "compressedLengths", "decompressedLengths" })
                Console.WriteLine($"{f}: {Flat(b[f])}");
            var blob = b["compressedBlob.Array"].AsByteArray;
            Console.WriteLine($"compressedBlob: {blob.Length} bytes");
            Console.WriteLine("stageCounts: " + Flat(b["stageCounts"]));
            var plats = b["platforms.Array"].Children.Select(c => c.AsInt).ToList();
            for (int k = 0; k < plats.Count; k++)
            {
                var offs = Leaves(b["offsets.Array"].Children[k]);
                var cls = Leaves(b["compressedLengths.Array"].Children[k]);
                var dls = Leaves(b["decompressedLengths.Array"].Children[k]);
                for (int s = 0; s < offs.Count; s++)
                {
                    var res = new byte[dls[s]];
                    if (dls[s] == 0) continue;
                    LZ4Codec.Decode(blob, (int)offs[s], (int)cls[s], res, 0, (int)dls[s]);
                    var fn = Path.Combine(outDir, San(nm) + $".p{plats[k]}.s{s}.bin");
                    File.WriteAllBytes(fn, res);
                    int n = BitConverter.ToInt32(res, 0);
                    Console.WriteLine($"plat {plats[k]} seg {s}: {res.Length} bytes, entries={n} -> {fn}");
                    for (int e = 0; e < Math.Min(n, 4); e++)
                        Console.WriteLine($"   [{e}] {BitConverter.ToInt32(res, 4 + e * 12)} {BitConverter.ToInt32(res, 8 + e * 12)} {BitConverter.ToInt32(res, 12 + e * 12)}");
                }
            }
            var pa = b["m_ParsedForm.m_SubShaders.Array"].Children[0]["m_Passes.Array"].Children[0];
            Console.WriteLine("pass0 nameIndices: " + string.Join(" ", pa["m_NameIndices.Array"].Children.Take(40).Select(e => e["first"].AsString + "=" + e["second"].AsInt)));
            var kwn = b["m_ParsedForm.m_KeywordNames.Array"];
            if (!kwn.IsDummy) Console.WriteLine("keywords: " + string.Join(" ", kwn.Children.Select((k, ki) => ki + "=" + k.AsString)));
            int si = 0;
            foreach (var ss in b["m_ParsedForm.m_SubShaders.Array"].Children)
            {
                Console.WriteLine($"subshader {si} tags: " + string.Join(" ", ss["m_Tags.tags.Array"].Children.Select(t => t["first"].AsString + "=" + t["second"].AsString)));
                int pj = 0;
                foreach (var ps2 in ss["m_Passes.Array"].Children)
                {
                    var tags = string.Join(" ", ps2["m_Tags.tags.Array"].Children.Select(t => t["first"].AsString + "=" + t["second"].AsString));
                    foreach (var st in new[] { "progVertex", "progFragment" })
                    {
                        var outs = ps2[st]["m_PlayerSubPrograms.Array"].Children;
                        var lists = outs.Select(o => string.Join(" ", o["Array"].Children.Select(s => $"{s["m_BlobIndex"].AsUInt}:[{string.Join(",", s["m_KeywordIndices.Array"].Children.Select(x => x.AsUShort))}]")));
                        Console.WriteLine($"  pass {pj} [{ps2["m_Name"].AsString}] mask={ps2["m_ProgramMask"].AsUInt} inst={ps2["m_HasInstancingVariant"].AsBool}/{ps2["m_HasProceduralInstancingVariant"].AsBool} {tags} {st}: " + string.Join(" | ", lists));
                    }
                    var lk = ps2["m_SerializedKeywordStateMask"];
                    pj++;
                }
                si++;
            }
            foreach (var st in new[] { "progVertex", "progFragment" })
            {
                var p = pa[st];
                Console.WriteLine($"== {st}: subprograms={p["m_SubPrograms.Array"].Children.Count}");
                Console.WriteLine("  m_PlayerSubPrograms: " + Flat(p["m_PlayerSubPrograms"], 300));
                int oi = 0;
                foreach (var outer in p["m_PlayerSubPrograms.Array"].Children)
                {
                    var pb = p["m_ParameterBlobIndices.Array"].Children.Count > oi ? p["m_ParameterBlobIndices.Array"].Children[oi]["Array"].Children.Select(x => x.AsUInt.ToString()).ToList() : new List<string>();
                    Console.WriteLine($"    outer[{oi}]: " + string.Join(" ", outer["Array"].Children.Select((s, j) => $"(code#{s["m_BlobIndex"].AsUInt} par#{(j < pb.Count ? pb[j] : "-")} kw={string.Join(",", s["m_KeywordIndices.Array"].Children.Select(x => x.AsUShort))} req={s["m_ShaderRequirements"].AsLong} gpu={s["m_GpuProgramType"].AsSByte})")));
                    oi++;
                }
                Console.WriteLine("  m_ParameterBlobIndices: " + Flat(p["m_ParameterBlobIndices"], 300));
                Console.WriteLine("  m_SerializedKeywordStateMask: " + Flat(p["m_SerializedKeywordStateMask"], 300));
                Console.WriteLine("  m_CommonParameters:");
                Dump(p["m_CommonParameters"], 2, 4);
                var subs = p["m_SubPrograms.Array"].Children;
                if (subs.Count > 0) { Console.WriteLine("  m_SubPrograms[0]:"); Dump(subs[0], 2, 5); }
                var ps = p["m_PlayerSubPrograms.Array"].Children;
                if (ps.Count > 0 && ps[0]["Array"].Children.Count > 0) { Console.WriteLine("  m_PlayerSubPrograms[0][0]:"); Dump(ps[0]["Array"].Children[0], 2, 5); }
            }
            Console.WriteLine("keywordNames: " + string.Join(" ", b["m_ParsedForm.m_KeywordNames.Array"].Children.Take(60).Select(c => c.AsString)));
            { var kfa = b["m_ParsedForm.m_KeywordFlags.Array"]; var kfb = kfa.AsByteArray ?? Array.Empty<byte>(); Console.WriteLine("keywordFlags(" + kfb.Length + "): " + string.Join(" ", kfb.Select((c, ci) => ci + ":" + c))); }
        }
    }

    static List<long> Leaves(AssetTypeValueField f)
    {
        var l = new List<long>();
        void Rec(AssetTypeValueField x)
        {
            if (x.TemplateField.HasValue && x.Value != null && !x.TemplateField.IsArray && (x.Children == null || x.Children.Count == 0)) { l.Add(x.AsLong); return; }
            if (x.Children != null) foreach (var c in x.Children) Rec(c);
        }
        Rec(f);
        return l;
    }

    static string Flat(AssetTypeValueField f, int max = 2000)
    {
        var sb = new StringBuilder();
        void Rec(AssetTypeValueField x)
        {
            if (sb.Length > max) return;
            if (x.Children == null || x.Children.Count == 0) { sb.Append(x.TemplateField.HasValue && x.Value != null ? x.AsString : "_").Append(' '); return; }
            sb.Append('[');
            foreach (var c in x.Children) Rec(c);
            sb.Append("] ");
        }
        Rec(f);
        return sb.ToString();
    }

    public static void Dump(AssetTypeValueField f, int d, int maxD)
    {
        if (f.IsDummy) { Console.WriteLine(new string(' ', d * 2) + "(dummy)"); return; }
        foreach (var c in f.Children)
        {
            var pad = new string(' ', d * 2);
            if (c.TemplateField.IsArray || c.FieldName == "Array")
            {
                Console.WriteLine($"{pad}{c.FieldName}[{c.Children.Count}] {Flat(c, 200)}");
                if (c.Children.Count > 0 && c.Children[0].Children?.Count > 0 && d < maxD) { Console.WriteLine($"{pad}  [0]:"); Dump(c.Children[0], d + 2, maxD); }
                continue;
            }
            if (c.Children != null && c.Children.Count > 0)
            {
                if (c.Children.Count == 1 && c.Children[0].FieldName == "Array") { Console.WriteLine($"{pad}{c.FieldName}[{c.Children[0].Children.Count}] {Flat(c, 200)}"); if (c.Children[0].Children.Count > 0 && c.Children[0].Children[0].Children?.Count > 0 && d < maxD) { Console.WriteLine($"{pad}  [0]:"); Dump(c.Children[0].Children[0], d + 2, maxD); } continue; }
                Console.WriteLine($"{pad}{c.FieldName}:");
                if (d < maxD) Dump(c, d + 1, maxD);
            }
            else Console.WriteLine($"{pad}{c.FieldName} = {(c.Value != null ? c.AsString : "_")}");
        }
    }

    public static string San(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
}
