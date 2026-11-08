using System.Text;

// Unity 6 (6000.3) GPU program blobs. A platform slice starts with a table (count, then off/len/seg
// per entry). Each player sub-program points at two entries:
//   code record   : version, type, alu, tex, flow, temp, keyword strings, code, source map, channel binds
//   param record  : version, parameter groups (constant buffers), resource bindings
// In 2020.3 both halves were one record; the layout of each half is unchanged.
class Param { public string Name; public int Type, Rows, Cols, IsMatrix, ArraySize, Index; }
class Group { public string Name = ""; public int Used; public List<Param> Params = new(); }
class Binding { public string Name; public int Type, Index, Extra, Extra2; }

static class Rd
{
    public static string RS(BinaryReader r)
    {
        int l = r.ReadInt32();
        var s = Encoding.UTF8.GetString(r.ReadBytes(l));
        while (r.BaseStream.Position % 4 != 0) r.ReadByte();
        return s;
    }

    public static void WS(BinaryWriter w, string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        w.Write(b.Length);
        w.Write(b);
        while (w.BaseStream.Position % 4 != 0) w.Write((byte)0);
    }

    public static List<(int off, int len, int seg)> Table(byte[] d)
    {
        var l = new List<(int, int, int)>();
        int n = BitConverter.ToInt32(d, 0);
        for (int i = 0; i < n; i++)
            l.Add((BitConverter.ToInt32(d, 4 + i * 12), BitConverter.ToInt32(d, 8 + i * 12), BitConverter.ToInt32(d, 12 + i * 12)));
        return l;
    }

    // Entries are 4-aligned within a record; records are read from a sub-stream of their own.
    public static BinaryReader At(byte[] d, int off, int len) => new BinaryReader(new MemoryStream(d, off, len, false));
}

class CodeRec
{
    public int Version, Type, Alu, Tex, Flow, Temp;
    public List<string> Keywords = new();
    public byte[] Code;
    public int SourceMap;
    public List<(int s, int t)> Binds = new();
    public byte[] Rest = Array.Empty<byte>();

    public static CodeRec Read(byte[] d, int off, int len)
    {
        var r = Rd.At(d, off, len);
        var p = new CodeRec { Version = r.ReadInt32(), Type = r.ReadInt32(), Alu = r.ReadInt32(), Tex = r.ReadInt32(), Flow = r.ReadInt32(), Temp = r.ReadInt32() };
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++) p.Keywords.Add(Rd.RS(r));
        n = r.ReadInt32();
        p.Code = r.ReadBytes(n);
        while (r.BaseStream.Position % 4 != 0) r.ReadByte();
        p.SourceMap = r.ReadInt32();
        n = r.ReadInt32();
        for (int i = 0; i < n; i++) p.Binds.Add((r.ReadInt32(), r.ReadInt32()));
        p.Rest = r.ReadBytes((int)(r.BaseStream.Length - r.BaseStream.Position));
        return p;
    }

    public byte[] Write()
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Version); w.Write(Type); w.Write(Alu); w.Write(Tex); w.Write(Flow); w.Write(Temp);
        w.Write(Keywords.Count);
        foreach (var s in Keywords) Rd.WS(w, s);
        w.Write(Code.Length);
        w.Write(Code);
        while (ms.Position % 4 != 0) w.Write((byte)0);
        w.Write(SourceMap);
        w.Write(Binds.Count);
        foreach (var (s, t) in Binds) { w.Write(s); w.Write(t); }
        w.Write(Rest);
        return ms.ToArray();
    }

    public override string ToString() =>
        $"code ver={Version:x} type={Type} alu={Alu} tex={Tex} flow={Flow} temp={Temp} kw=[{string.Join(" ", Keywords)}] code={Code.Length} srcmap={SourceMap:x} binds={string.Join(",", Binds)}" + (Rest.Length > 0 ? " REST " + Convert.ToHexString(Rest) : "");
}

class ParamRec
{
    public int Version;
    public List<Group> Groups = new();
    public List<Binding> Bindings = new();
    public byte[] Rest = Array.Empty<byte>();

    public static ParamRec Read(byte[] d, int off, int len)
    {
        var r = Rd.At(d, off, len);
        var p = new ParamRec { Version = r.ReadInt32() };
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            var g = new Group { Name = Rd.RS(r), Used = r.ReadInt32() };
            int pc = r.ReadInt32();
            for (int k = 0; k < pc; k++)
                g.Params.Add(new Param { Name = Rd.RS(r), Type = r.ReadInt32(), Rows = r.ReadInt32(), Cols = r.ReadInt32(), IsMatrix = r.ReadInt32(), ArraySize = r.ReadInt32(), Index = r.ReadInt32() });
            int sc = r.ReadInt32();
            if (sc != 0) throw new InvalidDataException("struct params in " + g.Name + ": " + sc);
            p.Groups.Add(g);
        }
        n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            var b = new Binding { Name = Rd.RS(r), Type = r.ReadInt32(), Index = r.ReadInt32(), Extra = r.ReadInt32() };
            if (b.Type == 0) b.Extra2 = r.ReadInt32();
            p.Bindings.Add(b);
        }
        p.Rest = r.ReadBytes((int)(r.BaseStream.Length - r.BaseStream.Position));
        return p;
    }

    public byte[] Write()
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Version);
        w.Write(Groups.Count);
        foreach (var g in Groups)
        {
            Rd.WS(w, g.Name);
            w.Write(g.Used);
            w.Write(g.Params.Count);
            foreach (var q in g.Params)
            {
                Rd.WS(w, q.Name);
                w.Write(q.Type); w.Write(q.Rows); w.Write(q.Cols); w.Write(q.IsMatrix); w.Write(q.ArraySize); w.Write(q.Index);
            }
            w.Write(0);
        }
        w.Write(Bindings.Count);
        foreach (var b in Bindings)
        {
            Rd.WS(w, b.Name);
            w.Write(b.Type); w.Write(b.Index); w.Write(b.Extra);
            if (b.Type == 0) w.Write(b.Extra2);
        }
        w.Write(Rest);
        return ms.ToArray();
    }

    public override string ToString()
    {
        var sb = new StringBuilder($"params ver={Version:x}\n");
        foreach (var g in Groups)
        {
            sb.Append($"  group [{g.Name}] used={g.Used}: ");
            foreach (var q in g.Params) sb.Append($"{q.Name}(t{q.Type} {q.Rows}x{q.Cols} m{q.IsMatrix} a{q.ArraySize} @{q.Index}) ");
            sb.Append('\n');
        }
        foreach (var b in Bindings) sb.Append($"  bind {b.Name} type={b.Type} idx={b.Index} extra={b.Extra},{b.Extra2}\n");
        if (Rest.Length > 0) sb.Append("  REST " + Convert.ToHexString(Rest) + "\n");
        return sb.ToString();
    }
}

// ptool shrec <file|bundle> <shader> <tpk> [plat]   parses every record of a slice and checks it writes back byte for byte
static class ShRec
{
    public static void Run(string file, string name, string tpk, int plat)
    {
        var (am, i, _) = Open.Any(file, tpk);
        Tpl.Ensure(i, tpk);
        foreach (var a in i.file.GetAssetsOfType(AssetsTools.NET.Extra.AssetClassID.Shader))
        {
            var b = Templates.Get(am, i, a);
            var nm = b["m_ParsedForm.m_Name"].AsString;
            if (name != "*" && nm != name) continue;
            var d = Slice6.Get(b, plat);
            if (d == null) { Console.WriteLine($"{nm}: no platform {plat}"); continue; }
            var t = Rd.Table(d);
            var code = new HashSet<int>(); var par = new HashSet<int>();
            foreach (var pa in b["m_ParsedForm.m_SubShaders.Array"].Children.SelectMany(s => s["m_Passes.Array"].Children))
                foreach (var st in new[] { "progVertex", "progFragment", "progGeometry", "progHull", "progDomain" })
                {
                    var p = pa[st];
                    if (p.IsDummy) continue;
                    var outers = p["m_PlayerSubPrograms.Array"].Children;
                    for (int oi = 0; oi < outers.Count; oi++)
                    {
                        var subs = outers[oi]["Array"].Children;
                        var pis = p["m_ParameterBlobIndices.Array"].Children[oi]["Array"].Children;
                        for (int j = 0; j < subs.Count; j++)
                        {
                            if (!Gpu.OnPlatform(subs[j]["m_GpuProgramType"].AsSByte, plat)) continue;
                            code.Add((int)subs[j]["m_BlobIndex"].AsUInt);
                            if (j < pis.Count) par.Add((int)pis[j].AsUInt);
                        }
                    }
                }
            int bad = 0;
            foreach (var k in code.OrderBy(x => x))
            {
                var c = CodeRec.Read(d, t[k].off, t[k].len);
                if (!c.Write().AsSpan().SequenceEqual(d.AsSpan(t[k].off, t[k].len))) bad++;
                if (name != "*" && k == code.Min()) { Console.WriteLine($"[{k}] {c}"); if (plat == 9) Console.WriteLine(System.Text.Encoding.UTF8.GetString(c.Code)); }
            }
            foreach (var k in par.OrderBy(x => x))
            {
                var c = ParamRec.Read(d, t[k].off, t[k].len);
                if (!c.Write().AsSpan().SequenceEqual(d.AsSpan(t[k].off, t[k].len))) bad++;
                if (name != "*") Console.Write($"[{k}] {c}");
            }
            Console.WriteLine($"{nm}: entries={t.Count} code={code.Count} params={par.Count} roundtrip-mismatch={bad}");
        }
    }
}

// ShaderGpuProgramType values per shader platform (ShaderCompilerPlatform).
static class Gpu
{
    public const sbyte Gles3 = 4;
    public static bool OnPlatform(int type, int plat) => plat switch
    {
        4 => type is >= 15 and <= 18,  // D3D: DX11 vertex/pixel SM4.0/5.0
        9 => type is 2 or 3 or 4,      // GLES3x: GLES31AEP, GLES31, GLES3
        18 => type == 25,              // Vulkan: SPIR-V
        _ => true,
    };
}

static class Slice6
{
    public static byte[] Get(AssetsTools.NET.AssetTypeValueField b, int plat)
    {
        var plats = b["platforms.Array"].Children.Select(c => c.AsInt).ToList();
        int k = plats.IndexOf(plat);
        if (k < 0) return null;
        var blob = b["compressedBlob.Array"].AsByteArray;
        int off = (int)b["offsets.Array"].Children[k]["Array"].Children[0].AsUInt;
        int c = (int)b["compressedLengths.Array"].Children[k]["Array"].Children[0].AsUInt;
        int dl = (int)b["decompressedLengths.Array"].Children[k]["Array"].Children[0].AsUInt;
        if (b["offsets.Array"].Children[k]["Array"].Children.Count != 1) throw new InvalidDataException("multi-segment slice");
        var res = new byte[dl];
        K4os.Compression.LZ4.LZ4Codec.Decode(blob, off, c, res, 0, dl);
        return res;
    }
}
