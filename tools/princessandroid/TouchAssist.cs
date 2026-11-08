using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace PrincessAndroid
{
    // Touch-screen play. The PC game is mouse-driven and Unity turns the first finger into the mouse,
    // so taps already click. What a finger cannot do on its own is filled in here; Stage.cs points the
    // game's calls at these methods:
    //   RewiredController.Update         Input.mousePosition -> MousePosition (a new touch always
    //                                    counts as pointer movement), Input.GetMouseButtonDown ->
    //                                    MouseButtonDown (a two-finger tap is a right click = back)
    //   GalleryViewer.Update             Input.mouseScrollDelta -> GalleryScroll (finger drag scrolls)
    //   ButtonFieldSelectGroup.UpdateOther  Input.GetMouseButtonDown -> AreaSubmit (battle area:
    //                                    the finger aims, lifting it confirms)
    public class TouchAssist : MonoBehaviour
    {
        public static void Start_()
        {
            var go = new GameObject("PrincessTouchAssist");
            DontDestroyOnLoad(go);
            go.AddComponent<TouchAssist>();
        }

        void Update() { Poll(); }

        // ---- per-frame touch state ----
        static int polledFrame = -1;
        static int touchBeganFrame = -1000;   // last frame a finger went down
        static float dragDy;                  // one-finger vertical movement this frame, screen pixels

        // two-finger tap
        static bool gesture, gestureMoved;
        static int gestureMax;
        static float gestureStart;
        static readonly Dictionary<int, Vector2> starts = new Dictionary<int, Vector2>();
        static int backFrame = -1;

        static void Poll()
        {
            int f = Time.frameCount;
            if (f == polledFrame) return;
            polledFrame = f;
            dragDy = 0;
            int n;
            try { n = Input.touchCount; } catch { return; }
            bool allUp = true;
            float moveLimit = Screen.height * 0.05f;
            for (int i = 0; i < n; i++)
            {
                var t = Input.GetTouch(i);
                if (t.phase == TouchPhase.Began)
                {
                    touchBeganFrame = f;
                    if (!gesture)
                    {
                        gesture = true;
                        gestureMoved = false;
                        gestureMax = 0;
                        gestureStart = Time.unscaledTime;
                        starts.Clear();
                    }
                    starts[t.fingerId] = t.position;
                }
                else if (starts.TryGetValue(t.fingerId, out var s) && (t.position - s).magnitude > moveLimit)
                    gestureMoved = true;
                if (t.phase != TouchPhase.Ended && t.phase != TouchPhase.Canceled) allUp = false;
                if (n == 1 && t.phase == TouchPhase.Moved) dragDy = t.deltaPosition.y;
            }
            if (gesture) gestureMax = Math.Max(gestureMax, n);
            // A second finger: the first one's press must not turn into a click on release.
            if (n >= 2 && !uiPaused) PauseUi(true);
            // allUp also holds with no touches at all: a very short tap can go away without an
            // Ended frame, and the gesture must not stay open after it.
            if (gesture && allUp)
            {
                gesture = false;
                if (gestureMax == 2 && !gestureMoved && Time.unscaledTime - gestureStart < 0.6f)
                {
                    backFrame = f;
                    Bridge.L("touch: two-finger tap -> back");
                }
            }
            if (uiPaused && n == 0) PauseUi(false);
        }

        // ---- RewiredController.Update ----

        // The game shows its mouse pointer (and lets buttons react to it) only after the pointer has
        // moved. After gamepad input, a tap on the very spot of the previous tap would not count as
        // movement and be ignored once; a new touch is nudged by a pixel so it always counts.
        static bool nudge;
        public static Vector3 MousePosition()
        {
            Poll();
            var p = Input.mousePosition;
            if (touchBeganFrame == Time.frameCount)
            {
                nudge = !nudge;
                p.x += nudge ? 1f : -1f;
            }
            return p;
        }

        // Right mouse button is the game's "back"; a two-finger tap is too.
        public static bool MouseButtonDown(int button)
        {
            Poll();
            return Input.GetMouseButtonDown(button) || (button == 1 && backFrame == Time.frameCount);
        }

        // ---- GalleryViewer.Update ----

        // The viewer scrolls tall pictures by target += -wheel * ConfigUtil.wheelSpeed * deltaTime.
        // A finger drag of d screen pixels moves the picture by d (in the 1080-high canvas).
        static FieldInfo wheelSpeedField;
        static bool wheelSpeedLooked;

        public static Vector2 GalleryScroll()
        {
            Poll();
            var w = Input.mouseScrollDelta;
            if (dragDy == 0) return w;
            if (!wheelSpeedLooked)
            {
                wheelSpeedLooked = true;
                try { wheelSpeedField = Type.GetType("ConfigUtil, Assembly-CSharp")?.GetField("wheelSpeed", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static); }
                catch (Exception e) { Bridge.L("touch: wheelSpeed: " + e.Message); }
                if (wheelSpeedField == null) Bridge.L("touch: ConfigUtil.wheelSpeed not found, gallery drag off");
            }
            if (wheelSpeedField == null) return w;
            float speed = (float)wheelSpeedField.GetValue(null), dt = Time.deltaTime;
            if (speed <= 0 || dt <= 0) return w;
            float d = dragDy * 1080f / Screen.height;
            return new Vector2(w.x, w.y - d / (speed * dt));
        }

        // ---- ButtonFieldSelectGroup.UpdateOther (battle area) ----

        // With a mouse the area follows the pointer and a click confirms. With a finger the press is
        // also the first moment the area knows where to go, so a press would confirm blind: instead
        // the area follows the finger and lifting it confirms. Only a touch that started after the
        // area appeared counts, so the tap that opened it does not confirm it on release.
        static int areaLastFrame = -10, areaRunStart = -1;

        public static bool AreaSubmit(int button)
        {
            Poll();
            int f = Time.frameCount;
            if (areaLastFrame < f - 1) areaRunStart = f;
            areaLastFrame = f;
            if (Input.touchCount == 0) return Input.GetMouseButtonDown(button);
            return touchBeganFrame >= areaRunStart && Input.GetMouseButtonUp(button);
        }

        // ---- UI pause while two fingers are down ----

        static bool uiPaused;
        static Behaviour pausedModule;

        static void PauseUi(bool pause)
        {
            uiPaused = pause;
            try
            {
                if (pause)
                {
                    var es = Type.GetType("UnityEngine.EventSystems.EventSystem, UnityEngine.UI");
                    var cur = es?.GetProperty("current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
                    pausedModule = cur?.GetType().GetProperty("currentInputModule")?.GetValue(cur, null) as Behaviour;
                    if (pausedModule != null) pausedModule.enabled = false;
                }
                else if (pausedModule != null)
                {
                    pausedModule.enabled = true;
                    pausedModule = null;
                }
            }
            catch (Exception e) { Bridge.L("touch: ui pause: " + e.Message); }
        }
    }
}
