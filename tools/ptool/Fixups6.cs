using System.Text;

// Rewrites on HLSLcc's GLSL before it is stored.
//
// HLSLcc turns D3D comparison masks into `int(uint(cond) * 0xFFFFFFFFu)` (or the ivecN/uvecN form).
// Converting 0xFFFFFFFFu to int must keep the bit pattern (-1), but some GLES stacks -- the emulator's
// among them -- saturate it to INT_MAX, which breaks every mask test. `-T(cond)` is the same value
// everywhere: T(true) is 1.
static class Fixups6
{
    const string Mul = "* 0xFFFFFFFFu)";

    public static string Apply(string glsl, ref int count)
    {
        var sb = new StringBuilder(glsl);
        for (int at = IndexOf(sb, Mul, 0); at >= 0; at = IndexOf(sb, Mul, at + 1))
        {
            // ...<T>(<U>(<cond>) * 0xFFFFFFFFu)
            int uClose = at - 1;
            while (uClose >= 0 && sb[uClose] == ' ') uClose--;
            if (uClose < 0 || sb[uClose] != ')') continue;
            int uOpen = Match(sb, uClose);
            if (uOpen < 0) continue;
            int uName = Ident(sb, uOpen);
            var u = sb.ToString(uName, uOpen - uName);
            if (u != "uint" && !(u.StartsWith("uvec") && u.Length == 5)) continue;
            int tOpen = uName - 1;
            if (tOpen < 0 || sb[tOpen] != '(') continue;
            int tName = Ident(sb, tOpen);
            var t = sb.ToString(tName, tOpen - tName);
            if (t != "int" && !(t.StartsWith("ivec") && t.Length == 5)) continue;
            var cond = sb.ToString(uOpen + 1, uClose - uOpen - 1);
            int end = at + Mul.Length;
            var rep = $"(-{t}({cond}))";
            sb.Remove(tName, end - tName).Insert(tName, rep);
            at = tName;
            count++;
        }
        return sb.ToString();
    }

    // Full-screen triangles (Core RP's GetFullScreenTriangleTexCoord) were compiled with
    // UNITY_UV_STARTS_AT_TOP=1: texcoord.y = 1 - (vertexID & 2). URP's C# side picks the GL convention
    // on GLES (scale/bias, projection), so every blit came out upside down -- the colour LUT too, which
    // scrambled all colours once post-processing ran. In such vertex programs the y that the position
    // was built from is used as is again, as the GL build of the shader does.
    static readonly System.Text.RegularExpressions.Regex FsPos =
        new(@"gl_Position\.xy = (u_xlat\d+)\.xy \* vec2\(2\.0, 2\.0\) \+ vec2\(-1\.0, -1\.0\);");

    public static string FlipBlitUv(string glsl, ref int count)
    {
        int vs = glsl.IndexOf("#ifdef VERTEX", StringComparison.Ordinal);
        // The vertex part runs up to the fragment part (it has #if/#endif blocks of its own).
        int ve = vs < 0 ? -1 : glsl.IndexOf("#ifdef FRAGMENT", vs, StringComparison.Ordinal);
        if (vs < 0) return glsl;
        if (ve < 0) ve = glsl.Length;
        var v = glsl.Substring(vs, ve - vs);
        if (!v.Contains("uint(gl_VertexID) & 2u")) return glsl;
        var m = FsPos.Match(v);
        if (!m.Success) return glsl;
        var src = m.Groups[1].Value + ".y";
        var flip = new System.Text.RegularExpressions.Regex(@"(\S+) = \(-" + System.Text.RegularExpressions.Regex.Escape(src) + @"\) \+ 1\.0;");
        int n = 0;
        var v2 = flip.Replace(v, mm => { n++; return mm.Groups[1].Value + " = " + src + ";"; });
        if (n == 0) return glsl;
        count += n;
        return glsl.Substring(0, vs) + v2 + glsl.Substring(ve);
    }

    // URP sizes its per-light arrays by MAX_VISIBLE_LIGHTS, 256 in the D3D build. As plain GLES uniforms
    // four vec4[256] arrays alone fill the 1024 vectors a desktop GPU allows (Adreno allows far fewer),
    // so URP/Lit failed to link and every 3D mesh of the town went missing. URP's GLES3 build uses 32,
    // and the C# side never sends more on Android, so the arrays are cut to that. Records the new
    // element count per parameter name, for the parameter records.
    public const int MaxLights = 32;
    static readonly System.Text.RegularExpressions.Regex LightArr =
        new(@"(uniform\s+(?:(?:highp|mediump|lowp)\s+)?\w+\s+)(hlslcc_mtx4x4)?(_Additional\w+)\[(256|1024)\];");

    public static string ClampLights(string glsl, Dictionary<string, int> sizes, ref int count)
    {
        int n = 0;
        var r = LightArr.Replace(glsl, m =>
        {
            bool mat = m.Groups[2].Success;
            int len = int.Parse(m.Groups[4].Value);
            if (len != (mat ? 1024 : 256)) return m.Value;
            n++;
            sizes[m.Groups[3].Value] = MaxLights;
            return $"{m.Groups[1].Value}{m.Groups[2].Value}{m.Groups[3].Value}[{(mat ? MaxLights * 4 : MaxLights)}];";
        });
        count += n;
        return r;
    }

    // Default-block vectors the larger of the two stages declares (every array element counted as a
    // whole vec4, as GLES drivers do). Adreno links at most 256 per stage.
    static readonly System.Text.RegularExpressions.Regex Uni =
        new(@"uniform\s+(?:(?:highp|mediump|lowp)\s+)?(\w+)\s+\w+(?:\[(\d+)\])?\s*;");

    public static int UniformVectors(string glsl)
    {
        int f = glsl.IndexOf("#ifdef FRAGMENT", StringComparison.Ordinal);
        int Count(string s)
        {
            int n = 0;
            foreach (System.Text.RegularExpressions.Match m in Uni.Matches(s))
            {
                var t = m.Groups[1].Value;
                if (t.Contains("sampler") || t.Contains("image")) continue;
                int per = t.StartsWith("mat4") ? 4 : t.StartsWith("mat3") ? 3 : t.StartsWith("mat2") ? 2 : 1;
                n += per * (m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1);
            }
            return n;
        }
        return f < 0 ? Count(glsl) : Math.Max(Count(glsl.Substring(0, f)), Count(glsl.Substring(f)));
    }

    static int IndexOf(StringBuilder sb, string s, int from)
    {
        var str = sb.ToString();
        return from >= str.Length ? -1 : str.IndexOf(s, from, StringComparison.Ordinal);
    }

    static int Match(StringBuilder sb, int close)
    {
        int depth = 0;
        for (int i = close; i >= 0; i--)
        {
            if (sb[i] == ')') depth++;
            else if (sb[i] == '(' && --depth == 0) return i;
        }
        return -1;
    }

    static int Ident(StringBuilder sb, int open)
    {
        int i = open;
        while (i > 0 && (char.IsLetterOrDigit(sb[i - 1]) || sb[i - 1] == '_')) i--;
        return i;
    }
}
