using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace PrincessAndroid
{
    // Rendering diagnostics, only with the "diag" debug flag (princess-debug.txt). Each time the file
    // <persistentDataPath>/diag-now appears it is deleted and every enabled renderer of the loaded
    // scenes is written to logcat (tag Princess): object path, renderer type, sorting, material,
    // shader, the keywords the material asks for and the ones Unity resolved, plus the enabled global
    // keywords. Used to find effects that render without their shader keywords.
    public class Diag : MonoBehaviour
    {
        public static void Start_()
        {
            var go = new GameObject("PrincessDiag");
            DontDestroyOnLoad(go);
            go.AddComponent<Diag>();
            Bridge.L("diag: ready, touch " + Application.persistentDataPath + "/diag-now to dump");
        }

        float next;

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 1f;
            var trigger = Application.persistentDataPath + "/diag-now";
            if (!File.Exists(trigger)) return;
            string filter = "";
            try { filter = File.ReadAllText(trigger).Trim(); File.Delete(trigger); } catch { }
            try { Dump(filter); } catch (Exception e) { Bridge.L("diag: " + e); }
        }

        static string PathOf(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        static void Dump(string filter)
        {
            Bridge.L("diag: global keywords [" + string.Join(",", Shader.enabledGlobalKeywords.Select(k => k.name).ToArray()) + "]");
            var all = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            int n = 0;
            foreach (var r in all)
            {
                if (r == null || !r.enabled) continue;
                var path = PathOf(r.transform);
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    var sh = m.shader;
                    var line = "diag R " + path + " [" + r.GetType().Name + "] vis=" + r.isVisible
                        + " sort=" + r.sortingLayerName + "/" + r.sortingOrder + " q=" + m.renderQueue
                        + " mat=" + m.name + " shader=" + (sh == null ? "null" : sh.name)
                        + (sh == null ? "" : " sup=" + sh.isSupported + " space=" + sh.keywordSpace.keywordCount + " passes=" + sh.passCount)
                        + " kw=[" + string.Join(",", m.shaderKeywords) + "]"
                        + " enabled=[" + string.Join(",", m.enabledKeywords.Select(k => k.name).ToArray()) + "]";
                    if (filter.Length > 0 && line.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (sh != null && m.shaderKeywords.Length > 0)
                    {
                        var sb = new StringBuilder(" check");
                        foreach (var k in m.shaderKeywords)
                        {
                            var lk = sh.keywordSpace.FindKeyword(k);
                            sb.Append(" " + k + ":valid=" + lk.isValid + ",on=" + (lk.isValid && m.IsKeywordEnabled(lk)) + ",type=" + (lk.isValid ? lk.type.ToString() : "-") + ",ovr=" + (lk.isValid && lk.isOverridable));
                        }
                        line += sb.ToString();
                    }
                    Bridge.L(line);
                    n++;
                }
            }
            Bridge.L("diag: " + n + " material slots on " + all.Length + " renderers");
        }
    }
}
