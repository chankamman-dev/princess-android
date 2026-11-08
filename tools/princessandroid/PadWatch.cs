using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace PrincessAndroid
{
    // Logs gamepad changes as Unity and Rewired see them ("[PrincessPad] ..." in logcat), so a pad
    // that does nothing on the device can be told apart: not seen by Unity, seen but not by Rewired,
    // or seen by both (then it is a mapping problem).
    public class PadWatch : MonoBehaviour
    {
        string lastUnity = "", lastRewired = "";
        float next;
        // Frame-rate report (developer log only): frames and the slowest frame of every 5 s.
        int frames; float fpsStart = -1, worst;

        public static void Start_()
        {
            var go = new GameObject("PrincessPadWatch");
            DontDestroyOnLoad(go);
            go.AddComponent<PadWatch>();
        }

        void Update()
        {
            if (Bridge.DevLog)
            {
                float now = Time.unscaledTime;
                if (fpsStart < 0) fpsStart = now;
                frames++;
                worst = Mathf.Max(worst, Time.unscaledDeltaTime);
                if (now - fpsStart >= 5f)
                {
                    Bridge.L("fps: " + (frames / (now - fpsStart)).ToString("F1") + ", slowest frame " + (worst * 1000f).ToString("F0") + " ms, target " + Application.targetFrameRate);
                    frames = 0; worst = 0; fpsStart = now;
                }
            }
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 2f;
            try
            {
                var u = string.Join(" | ", Input.GetJoystickNames());
                if (u != lastUnity) { lastUnity = u; Bridge.L("pad: Unity joysticks: [" + u + "]"); }
                var r = RewiredJoysticks();
                if (r != lastRewired) { lastRewired = r; Bridge.L("pad: Rewired joysticks: [" + r + "]"); }
            }
            catch (Exception e) { Bridge.L("pad: " + e.Message); next = float.MaxValue; }
        }

        static string RewiredJoysticks()
        {
            var re = Type.GetType("Rewired.ReInput, Rewired_Core");
            if (re == null) return "no ReInput";
            if (!(bool)re.GetProperty("isReady", BindingFlags.Public | BindingFlags.Static).GetValue(null, null)) return "not ready";
            var ctl = re.GetProperty("controllers", BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
            var list = (IEnumerable)ctl.GetType().GetProperty("Joysticks").GetValue(ctl, null);
            var s = "";
            foreach (var j in list)
            {
                var t = j.GetType();
                s += (s.Length > 0 ? " | " : "") + t.GetProperty("name").GetValue(j, null) + " (hw " + t.GetProperty("hardwareName").GetValue(j, null) + ")";
            }
            return s;
        }
    }
}
