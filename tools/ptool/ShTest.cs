using System.Diagnostics;

// ptool shtest <file|bundle> <shader> <tpk> <tmp> <hlslcc-cli> [pass]
// Translates the first vertex/fragment pair of one pass from DXBC to GLSL and prints the result,
// with the parameter records the RDEF was rebuilt from.
static class ShTest
{
    public const uint Flags = 0x20000 | 0x80 | 0x8000 | 0x800 | 0x10000 | 0x8000000 | 0x8;

    public static void Run(string file, string name, string tpk, string tmp, string cli, int passNo)
    {
        var (am, i, _) = Open.Any(file, tpk);
        Tpl.Ensure(i, tpk);
        Directory.CreateDirectory(tmp);
        foreach (var a in i.file.GetAssetsOfType(AssetsTools.NET.Extra.AssetClassID.Shader))
        {
            var b = Templates.Get(am, i, a);
            if (b["m_ParsedForm.m_Name"].AsString != name) continue;
            var d = Slice6.Get(b, 4);
            var t = Rd.Table(d);
            var pa = b["m_ParsedForm.m_SubShaders.Array"].Children.SelectMany(s => s["m_Passes.Array"].Children).ElementAt(passNo);
            var names = pa["m_NameIndices.Array"].Children.ToDictionary(e => e["second"].AsInt, e => e["first"].AsString);
            (CodeRec c, ParamRec p) Get(string st)
            {
                var outer = pa[st + ".m_PlayerSubPrograms.Array"].Children.Select((o, k) => (o, k)).First(x => x.o["Array"].Children.Count > 0);
                var want = Environment.GetEnvironmentVariable("SHTEST_KW");
                var spIdx = 0;
                if (want != null) spIdx = outer.o["Array"].Children.FindIndex(s => string.Join(",", s["m_KeywordIndices.Array"].Children.Select(x => x.AsUShort).OrderBy(x => x)) == want);
                if (spIdx < 0) spIdx = 0;
                var sp = outer.o["Array"].Children[spIdx];
                int ci = (int)sp["m_BlobIndex"].AsUInt;
                int pi = (int)pa[st + ".m_ParameterBlobIndices.Array"].Children[outer.k]["Array"].Children[spIdx].AsUInt;
                Console.WriteLine($"{st}: outer={outer.k} code#{ci} params#{pi} kw=[{string.Join(",", sp["m_KeywordIndices.Array"].Children.Select(x => x.AsUShort))}] gpu={sp["m_GpuProgramType"].AsSByte}");
                return (CodeRec.Read(d, t[ci].off, t[ci].len), ParamRec.Read(d, t[pi].off, t[pi].len));
            }
            var (vc, vp) = Get("progVertex");
            var (fc, fp) = Get("progFragment");
            Console.WriteLine(vc); Console.Write(vp); Console.WriteLine(fc); Console.Write(fp);
            var rv = new Refl(); var rf = new Refl();
            rv.AddProg(vp); rf.AddProg(fp);
            rv.AddCommon(pa["progVertex.m_CommonParameters"], names, null);
            rf.AddCommon(pa["progFragment.m_CommonParameters"], names, null);
            var dv = Dxbc.Strip(vc.Code); var df = Dxbc.Strip(fc.Code);
            var pre = Path.Combine(tmp, "test");
            File.WriteAllBytes(pre + ".vs", Dxbc.WithRdef(dv, rv.Rdef(Dxbc.VersionToken(dv))));
            File.WriteAllBytes(pre + ".fs", Dxbc.WithRdef(df, rf.Rdef(Dxbc.VersionToken(df))));
            foreach (var ext in new[] { ".glsl", ".meta" }) File.Delete(pre + ext);
            var list = Path.Combine(tmp, "test.txt");
            File.WriteAllText(list, $"{pre}.vs {pre}.fs {pre}\n");
            var psi = new ProcessStartInfo(cli, $"{Flags:x} 2 \"{list}\"") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            var pr = Process.Start(psi);
            Console.WriteLine(pr.StandardOutput.ReadToEnd() + pr.StandardError.ReadToEnd());
            pr.WaitForExit();
            if (File.Exists(pre + ".meta")) Console.WriteLine(File.ReadAllText(pre + ".meta"));
            if (File.Exists(pre + ".glsl")) Console.WriteLine(File.ReadAllText(pre + ".glsl"));
            return;
        }
    }
}
