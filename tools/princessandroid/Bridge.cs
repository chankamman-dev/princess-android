using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PrincessAndroid
{
    // Woven into the game by `ptool stage`. Hands achievements and save writes to the Java side
    // (dev.kmprincess.cloud.CloudBridge), points Addressables at the bundles on the device and applies
    // the launcher's settings (frame-rate cap, render scale, developer log).
    public static class Bridge
    {
        static bool started;
        static string bundleDir;

        // Launcher settings (dev.kmprincess.cloud.Settings), read once.
        static bool settingsLoaded;
        static int fpsCap = 60, renderScale = 100;
        static bool devLog, postFx = true;
        internal static bool DevLog { get { LoadSettings(); return devLog; } }

        // Woven into Addressables (Stage.cs): finish bundle and asset loads synchronously instead of
        // one preload step per frame. Launcher setting "syncload" (default on).
        public static bool SyncLoads { get { LoadSettings(); return syncLoads; } }
        static bool syncLoads = true;

        static void LoadSettings()
        {
            if (settingsLoaded) return;
            settingsLoaded = true;
            fpsCap = Setting("fps", 60);
            renderScale = Mathf.Clamp(Setting("scale", 100), 50, 100);
            devLog = Setting("devlog", 0) != 0;
            syncLoads = Setting("syncload", 1) != 0;
            postFx = Setting("post", 1) != 0;
        }

        // Start of InitLoader.Awake, before the first scene renders anything.
        // URP asks for 4x MSAA but the GLES window surface comes up single-sampled, so every render
        // pass fails ("Attachment 0 was created with 1 samples but 4 samples were requested") and the
        // screen stays black. MSAA goes off: on the pipeline asset and in the quality settings.
        public static void Boot()
        {
            try
            {
                LoadSettings();
                L("gfx " + SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceVersion + " / " + SystemInfo.graphicsDeviceName
                    + "; screen " + Screen.width + "x" + Screen.height + "; quality " + QualitySettings.GetQualityLevel() + "/" + QualitySettings.names.Length
                    + "; fps cap " + fpsCap + ", render scale " + renderScale + "%, sync loads " + syncLoads + ", post " + postFx + ", dev log " + devLog);
                // When the log is on (developer setting), plain logs and warnings carry no stack trace:
                // the game logs a lot and the traces are most of the cost.
                Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
                Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
                QualitySettings.antiAliasing = 0;
                // Asset integration on the main thread is time-sliced per frame (Normal: ~10 ms). The
                // game loads its whole default set behind the logo; on a handheld that crawls, so give
                // loading the larger slice (High: ~50 ms). Scenes switch behind loading screens anyway.
                Application.backgroundLoadingPriority = ThreadPriority.High;
                Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
                var seen = new List<UnityEngine.Rendering.RenderPipelineAsset>();
                seen.Add(UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline);
                for (int i = 0; i < QualitySettings.names.Length; i++) seen.Add(QualitySettings.GetRenderPipelineAssetAt(i));
                foreach (var rp in seen)
                {
                    if (rp == null) continue;
                    var t = rp.GetType();
                    var msaa = t.GetProperty("msaaSampleCount");
                    if (msaa != null && msaa.CanWrite) msaa.SetValue(rp, 1, null);
                    var scale = t.GetProperty("renderScale");
                    if (scale != null && scale.CanWrite) scale.SetValue(rp, renderScale / 100f, null);
                }
                LoadDebugFlags();
                if (debug.Contains("nopost")) postFx = false;
                if (!postFx) UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += NoPost;
            }
            catch (Exception e)
            {
                L("Boot: " + e);
            }
            try { PadWatch.Start_(); } catch (Exception e) { L("PadWatch: " + e); }
            try { TouchAssist.Start_(); } catch (Exception e) { L("TouchAssist: " + e); }
            if (debug.Contains("diag")) try { Diag.Start_(); } catch (Exception e) { L("Diag: " + e); }
            // Steam playtime runs from the start, not only once a save is loaded (a new game never loads one).
            StartPlaytime();
        }

        // Every Application.targetFrameRate = n of the game. On PC it asks for the monitor's refresh
        // rate (60-240); on a handheld that burns battery for little, so the launcher's cap applies.
        public static void SetTargetFrameRate(int requested)
        {
            LoadSettings();
            int fps = requested <= 0 ? fpsCap : Math.Min(requested, fpsCap);
            Application.targetFrameRate = fps;
            L("targetFrameRate " + requested + " -> " + fps);
        }

        // Every Debug.unityLogger.logEnabled = x of the game (false in its release build). The
        // launcher's developer setting turns the log on regardless.
        public static void SetLogEnabled(bool wanted)
        {
            LoadSettings();
            Debug.unityLogger.logEnabled = wanted || devLog;
        }

        static void StartPlaytime()
        {
            if (started) return;
            started = true;
            Call("onGameStarted");
        }

        // Every Screen.SetResolution(w, h, fullscreen) of the game lands here. The PC options (window
        // mode, a list of 16:9 sizes) mean nothing on a handheld: it stays full screen at the panel's
        // own size, otherwise the status bar shows and the picture is rescaled.
        public static void SetResolution(int w, int h, bool fullscreen)
        {
            // currentResolution is the window, which is short by the status bar until full screen.
            int pw = Display.main.systemWidth, ph = Display.main.systemHeight;
            L("SetResolution(" + w + "x" + h + ", fs=" + fullscreen + ") -> " + pw + "x" + ph + " full screen");
            Screen.SetResolution(pw, ph, FullScreenMode.FullScreenWindow);
        }

        // Debug switches, one per line, from <persistentDataPath>/princess-debug.txt:
        //   nopost   every camera renders without post-processing (same as the launcher setting)
        static readonly HashSet<string> debug = new HashSet<string>();

        static void LoadDebugFlags()
        {
            try
            {
                var p = Application.persistentDataPath + "/princess-debug.txt";
                if (!File.Exists(p)) return;
                foreach (var l in File.ReadAllLines(p)) if (l.Trim().Length > 0) debug.Add(l.Trim());
                L("debug flags: " + string.Join(",", new List<string>(debug).ToArray()));
            }
            catch (Exception e) { L("debug flags: " + e.Message); }
        }

        // Launcher setting "post" off (or the "nopost" debug flag): every camera renders without URP
        // post-processing (bloom, colour grading, ...). Checked as each camera starts rendering, so
        // cameras created later and a game script switching it back on are covered too.
        static Type camDataType;
        static System.Reflection.PropertyInfo camDataPost;
        static readonly HashSet<int> postLogged = new HashSet<int>();

        static void NoPost(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera cam)
        {
            try
            {
                if (camDataType == null)
                {
                    foreach (var c in cam.GetComponents<Component>())
                    {
                        var p = c == null ? null : c.GetType().GetProperty("renderPostProcessing");
                        if (p == null || !p.CanWrite) continue;
                        camDataType = c.GetType();
                        camDataPost = p;
                        break;
                    }
                    if (camDataType == null) return;
                }
                var data = cam.GetComponent(camDataType);
                if (data == null || !(bool)camDataPost.GetValue(data, null)) return;
                camDataPost.SetValue(data, false, null);
                if (postLogged.Add(cam.GetInstanceID())) L("post-processing off: " + cam.name);
            }
            catch (Exception e)
            {
                L("NoPost: " + e.Message);
                UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= NoPost;
            }
        }

        // AchievementController.Unlock(id); on PC that is SteamUserStats.SetAchievement("ACV_" + id).
        public static void Achievement(int id) => Call("onAchievements", "ACV_" + id);

        // End of MyData.ApplySaveDataUserMain: a save is in. Makes sure playtime runs and
        // offers every achievement the save already holds, so ones from before signing in count too.
        public static void Unlocked(List<int> ids)
        {
            StartPlaytime();
            if (ids == null || ids.Count == 0) return;
            var names = new string[ids.Count];
            for (int i = 0; i < ids.Count; i++) names[i] = "ACV_" + ids[i];
            L("achievements held: " + ids.Count);
            Call("onAchievements", string.Join(",", names));
        }

        // End of SaveController.FileSave / FileDelete.
        public static void Saved() => Call("onSaved");

        // AddressablesImpl.ResolveInternalId: "<RuntimePath>\StandaloneWindows64\<hash>.bundle" becomes
        // "<bundle dir>/<hash>.bundle". Everything else is returned unchanged.
        public static string MapId(string id)
        {
            if (id == null) return id;
            if (!id.EndsWith(".bundle", StringComparison.Ordinal)) { if (DevLog) L("id " + id); return id; }
            int k = id.IndexOf("StandaloneWindows64", StringComparison.Ordinal);
            if (k < 0) return id;
            if (bundleDir == null) bundleDir = FindBundleDir();
            if (DevLog) { mapCount++; L("bundle #" + mapCount + " " + id.Substring(k + "StandaloneWindows64".Length + 1)); }
            return bundleDir + "/" + id.Substring(k + "StandaloneWindows64".Length + 1).Replace('\\', '/');
        }

        static int mapCount;

        static string FindBundleDir()
        {
            var candidates = new List<string>();
            try { candidates.Add("/data/data/" + Application.identifier + "/files/aa"); } catch { }
            try { candidates.Add(Application.persistentDataPath + "/aa"); } catch { }
            candidates.Add("/sdcard/MagicalPrincess/aa");
            foreach (var c in candidates)
                if (Directory.Exists(c)) { L("bundles from " + c); return c; }
            L("no bundle directory; tried " + string.Join(", ", candidates.ToArray()));
            return candidates[0];
        }

        static AndroidJavaObject Activity()
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                return player.GetStatic<AndroidJavaObject>("currentActivity");
        }

        static int Setting(string key, int def)
        {
            try
            {
                using (var activity = Activity())
                using (var bridge = new AndroidJavaClass("dev.kmprincess.cloud.CloudBridge"))
                    return bridge.CallStatic<int>("intSetting", activity, key, def);
            }
            catch (Exception e)
            {
                L("setting " + key + ": " + e.Message);
                return def;
            }
        }

        static void Call(string method, string arg = null)
        {
            L("-> " + method + (arg == null ? "" : " " + arg));
            try
            {
                using (var activity = Activity())
                using (var bridge = new AndroidJavaClass("dev.kmprincess.cloud.CloudBridge"))
                {
                    if (arg == null) bridge.CallStatic(method, activity);
                    else bridge.CallStatic(method, activity, arg);
                }
            }
            catch (Exception e)
            {
                L(method + ": " + e.Message);
            }
        }

        // The port's own diagnostics go straight to logcat (tag "Princess"), whatever the game's
        // logger switch says.
        static AndroidJavaClass log;

        internal static void L(string msg)
        {
            try
            {
                if (log == null) log = new AndroidJavaClass("android.util.Log");
                log.CallStatic<int>("i", "Princess", msg);
            }
            catch
            {
                Debug.Log("[Princess] " + msg);
            }
        }
    }
}
