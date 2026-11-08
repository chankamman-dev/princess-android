using AssetsTools.NET;
using AssetsTools.NET.Extra;
using K4os.Compression.LZ4;
using System.Diagnostics;
using System.Text;

// Shared with lomtool (Convert.cs): rebuilding the RDEF chunk HLSLcc needs from Unity's parameter records.
//
// The game's DXBC has had its RDEF chunk stripped, and HLSLcc needs one, so it is rebuilt here from
// what the player keeps: the program record's parameter groups plus the pass-level common parameters.
class CbVar { public string Name; public int Type, Rows, Cols, IsMatrix, ArraySize, Offset; }
class Cb { public string Name; public int Size; public List<CbVar> Vars = new(); public int Bind; }
class Tex { public string Name; public int Index, Sampler, Dim; }

class Refl
{
    public List<Cb> Cbs = new();
    public List<Tex> Texs = new();
    public List<(string name, int idx)> Samplers = new();
    public List<(string name, int idx)> Bufs = new();

    public Cb GetCb(string n)
    {
        var c = Cbs.FirstOrDefault(x => x.Name == n);
        if (c == null) Cbs.Add(c = new Cb { Name = n, Bind = -1 });
        return c;
    }

    // Parameters that live in the program record.
    public void AddProg(ParamRec p)
    {
        foreach (var g in p.Groups)
        {
            if (g.Name == "" && g.Params.Count == 0) continue;
            var c = GetCb(g.Name == "" ? "$Globals" : g.Name);
            c.Size = Math.Max(c.Size, g.Used);
            foreach (var q in g.Params) c.Vars.Add(new CbVar { Name = q.Name, Type = q.Type, Rows = q.Rows, Cols = q.Cols, IsMatrix = q.IsMatrix, ArraySize = q.ArraySize, Offset = q.Index });
        }
        foreach (var b in p.Bindings)
        {
            if (b.Type == 1) GetCb(b.Name).Bind = b.Index;
            else if (b.Type == 0) Texs.Add(new Tex { Name = b.Name, Index = b.Index, Sampler = b.Extra, Dim = b.Extra2 });
            // Structured/raw buffers (type 2), e.g. SKINNED_SPRITE's _SpriteBoneTransforms: no GLES 3.0 form.
            else if (b.Type == 2) Bufs.Add((b.Name, b.Index));
        }
    }

    // Parameters the build pipeline moved to the program level. Used by every variant, so a binding
    // is only trusted when the CB is actually present in this variant's record or the common list.
    public void AddCommon(AssetTypeValueField c, Dictionary<int, string> names, Dictionary<string, int> dimFix)
    {
        if (c.IsDummy) return;
        AddSet(c, names);
    }

    public void AddSet(AssetTypeValueField c, Dictionary<int, string> names)
    {
        foreach (var e in c["m_ConstantBuffers.Array"].Children)
        {
            var cb = GetCb(names[e["m_NameIndex"].AsInt]);
            cb.Size = Math.Max(cb.Size, e["m_Size"].AsInt);
            foreach (var v in e["m_VectorParams.Array"].Children) cb.Vars.Add(Vec(v, names));
            foreach (var v in e["m_MatrixParams.Array"].Children) cb.Vars.Add(Mat(v, names));
        }
        foreach (var e in c["m_ConstantBufferBindings.Array"].Children)
            GetCb(names[e["m_NameIndex"].AsInt]).Bind = e["m_Index"].AsInt;
        foreach (var e in c["m_TextureParams.Array"].Children)
            Texs.Add(new Tex { Name = names[e["m_NameIndex"].AsInt], Index = e["m_Index"].AsInt, Sampler = e["m_SamplerIndex"].AsInt, Dim = DimToSrv(e["m_Dim"].AsSByte) });
        foreach (var e in c["m_Samplers.Array"].Children)
            Samplers.Add(("sampler_inline" + e["bindPoint"].AsInt, e["bindPoint"].AsInt));
        foreach (var e in c["m_BufferParams.Array"].Children)
            Bufs.Add((names[e["m_NameIndex"].AsInt], e["m_Index"].AsInt));
    }

    static CbVar Vec(AssetTypeValueField v, Dictionary<int, string> names) => new CbVar
    {
        Name = names[v["m_NameIndex"].AsInt], Type = v["m_Type"].AsSByte, Rows = 1, Cols = v["m_Dim"].AsSByte,
        ArraySize = v["m_ArraySize"].AsInt, Offset = v["m_Index"].AsInt,
    };

    static CbVar Mat(AssetTypeValueField v, Dictionary<int, string> names) => new CbVar
    {
        Name = names[v["m_NameIndex"].AsInt], Type = v["m_Type"].AsSByte, Rows = v["m_RowCount"].AsSByte,
        Cols = v["m_ColumnCount"].IsDummy ? v["m_RowCount"].AsSByte : v["m_ColumnCount"].AsSByte, IsMatrix = 1, ArraySize = v["m_ArraySize"].AsInt, Offset = v["m_Index"].AsInt,
    };

    public static int DimToSrv(int unityDim) => unityDim switch { 2 => 4, 3 => 8, 4 => 9, 5 => 5, 6 => 10, _ => 4 };
    public static int SrvToUnity(int srv) => srv switch { 4 => 2, 8 => 3, 9 => 4, 5 => 5, 10 => 6, _ => 2 };

    public byte[] Rdef(uint versionToken)
    {
        // Laid out as: header, resource bindings, cbuffers, variables, types, strings.
        int major = (int)((versionToken >> 4) & 0xf);
        var strs = new MemoryStream();
        var strOff = new Dictionary<string, int>();
        int Str(string s)
        {
            if (strOff.TryGetValue(s, out var o)) return o;
            o = (int)strs.Position;
            strs.Write(Encoding.ASCII.GetBytes(s));
            strs.WriteByte(0);
            strOff[s] = o;
            return o;
        }

        var cbs = Cbs.Where(c => c.Bind >= 0 || c.Vars.Count > 0).ToList();
        var dedupVars = cbs.ToDictionary(c => c, c => c.Vars.GroupBy(v => v.Name).Select(g => g.First()).ToList());
        var binds = new List<(string name, int type, int ret, int dim, int samples, int point, int count, int flags)>();
        foreach (var c in cbs) binds.Add((c.Name, 0, 0, 0, 0, c.Bind < 0 ? 0 : c.Bind, 1, 1));
        var samplerSeen = new HashSet<int>();
        foreach (var t in Texs.GroupBy(t => t.Name).Select(g => g.First()))
        {
            binds.Add((t.Name, 2, 5, t.Dim, -1, t.Index, 1, 0x0c));
            if (t.Sampler >= 0 && t.Sampler < 32 && samplerSeen.Add(t.Sampler)) binds.Add(("sampler" + t.Name, 3, 0, 0, 0, t.Sampler, 1, 0));
        }
        foreach (var (n, i) in Samplers) if (samplerSeen.Add(i)) binds.Add((n, 3, 0, 0, 0, i, 1, 0));

        const int headerSize = 28;
        int bindSize = 32, cbSize = 24, varSize = major >= 5 ? 40 : 24, typeSize = 16;
        int bindOff = headerSize, cbOff = bindOff + binds.Count * bindSize, varOff = cbOff + cbs.Count * cbSize;
        int varCount = dedupVars.Values.Sum(v => v.Count);
        int typeOff = varOff + varCount * varSize;
        int strBase = typeOff + varCount * typeSize;

        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(cbs.Count); w.Write(cbOff); w.Write(binds.Count); w.Write(bindOff); w.Write(versionToken & 0xffff0000 | (versionToken & 0xff)); w.Write(0); w.Write(strBase + Str("lomtool"));
        foreach (var b in binds)
        {
            w.Write(strBase + Str(b.name)); w.Write(b.type); w.Write(b.ret); w.Write(b.dim); w.Write(b.samples); w.Write(b.point); w.Write(b.count); w.Write(b.flags);
        }
        int vi = 0;
        foreach (var c in cbs)
        {
            w.Write(strBase + Str(c.Name)); w.Write(dedupVars[c].Count); w.Write(varOff + vi * varSize); w.Write(Math.Max(c.Size, 16)); w.Write(0); w.Write(0);
            vi += dedupVars[c].Count;
        }
        vi = 0;
        foreach (var c in cbs)
            foreach (var v in dedupVars[c])
            {
                int elems = Math.Max(1, v.ArraySize);
                int size = v.IsMatrix != 0 ? 16 * v.Rows * elems : elems > 1 ? 16 * elems : 4 * v.Cols;
                w.Write(strBase + Str(v.Name)); w.Write(v.Offset); w.Write(size); w.Write(2); w.Write(typeOff + vi * typeSize); w.Write(0);
                if (major >= 5) { w.Write(-1); w.Write(0); w.Write(-1); w.Write(0); }
                vi++;
            }
        foreach (var c in cbs)
            foreach (var v in dedupVars[c])
            {
                int svc = v.IsMatrix != 0 ? 3 : v.Cols == 1 ? 0 : 1;
                int svt = v.Type switch { 1 => 2, 2 => 1, 5 => 19, _ => 3 };
                w.Write((ushort)svc); w.Write((ushort)svt); w.Write((ushort)v.Rows); w.Write((ushort)v.Cols); w.Write((ushort)v.ArraySize); w.Write((ushort)0); w.Write(0);
            }
        w.Write(strs.ToArray());
        while (ms.Position % 4 != 0) w.Write((byte)0);
        return ms.ToArray();
    }
}

static class Dxbc
{
    // Unity prefixes the container with its own 38 byte header.
    public static byte[] Strip(byte[] code)
    {
        for (int i = 0; i + 4 < code.Length; i++)
            if (code[i] == 'D' && code[i + 1] == 'X' && code[i + 2] == 'B' && code[i + 3] == 'C') return code.AsSpan(i).ToArray();
        throw new InvalidDataException("no DXBC");
    }

    public static uint VersionToken(byte[] d)
    {
        int n = BitConverter.ToInt32(d, 28);
        for (int i = 0; i < n; i++)
        {
            int o = BitConverter.ToInt32(d, 32 + i * 4);
            var cc = Encoding.ASCII.GetString(d, o, 4);
            if (cc is "SHDR" or "SHEX") return BitConverter.ToUInt32(d, o + 8);
        }
        throw new InvalidDataException("no shader chunk");
    }

    public static byte[] WithRdef(byte[] d, byte[] rdef)
    {
        int n = BitConverter.ToInt32(d, 28);
        var chunks = new List<(string cc, byte[] data)>();
        for (int i = 0; i < n; i++)
        {
            int o = BitConverter.ToInt32(d, 32 + i * 4);
            var cc = Encoding.ASCII.GetString(d, o, 4);
            int len = BitConverter.ToInt32(d, o + 4);
            chunks.Add((cc, d.AsSpan(o + 8, len).ToArray()));
        }
        chunks.Insert(0, ("RDEF", rdef));
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(d, 0, 24);
        w.Write(0);
        w.Write(chunks.Count);
        int off = 32 + 4 * chunks.Count;
        foreach (var c in chunks) { w.Write(off); off += 8 + c.data.Length; }
        foreach (var c in chunks) { w.Write(Encoding.ASCII.GetBytes(c.cc)); w.Write(c.data.Length); w.Write(c.data); }
        var r = ms.ToArray();
        BitConverter.TryWriteBytes(r.AsSpan(24), r.Length);
        return r;
    }
}

