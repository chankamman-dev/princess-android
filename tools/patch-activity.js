// Makes Unity's UnityPlayerActivity immersive full screen. The Windows player data carries the PC
// window settings, so the Android player leaves the status and navigation bars up; on a handheld
// the game should own the whole panel.
// usage: node patch-activity.js <UnityPlayerActivity.java in> <out>
const fs = require('fs');
const [src, dst] = process.argv.slice(2);
let t = fs.readFileSync(src, 'utf8');
const helper = `
    // [princess] immersive full screen, drawn under the display cutout too.
    private void princessImmersive()
    {
        Window w = getWindow();
        if (Build.VERSION.SDK_INT >= 28)
        {
            android.view.WindowManager.LayoutParams lp = w.getAttributes();
            lp.layoutInDisplayCutoutMode = 1; // SHORT_EDGES
            w.setAttributes(lp);
        }
        // LAYOUT_STABLE | LAYOUT_HIDE_NAVIGATION | LAYOUT_FULLSCREEN | HIDE_NAVIGATION | FULLSCREEN | IMMERSIVE_STICKY
        w.getDecorView().setSystemUiVisibility(0x100 | 0x200 | 0x400 | 0x2 | 0x4 | 0x1000);
        if (Build.VERSION.SDK_INT >= 30) princessHideBars(w);
        // The player sets the bars itself once its surface is up (later on a slow start); have the
        // last word, and take them away again whenever something brings them back.
        final Runnable hide = new Runnable() { public void run() {
            if (Build.VERSION.SDK_INT >= 30) princessHideBars(getWindow());
            getWindow().getDecorView().setSystemUiVisibility(0x100 | 0x200 | 0x400 | 0x2 | 0x4 | 0x1000);
        } };
        android.view.View d = w.getDecorView();
        d.postDelayed(hide, 600);
        d.postDelayed(hide, 2000);
        d.postDelayed(hide, 5000);
        if (!princessListening)
        {
            princessListening = true;
            d.setOnSystemUiVisibilityChangeListener(new android.view.View.OnSystemUiVisibilityChangeListener() {
                public void onSystemUiVisibilityChange(int vis) {
                    if ((vis & 0x4) == 0) getWindow().getDecorView().postDelayed(hide, 1500);
                }
            });
        }
    }
    private boolean princessListening;

    @TargetApi(30)
    private static void princessHideBars(Window w)
    {
        w.setDecorFitsSystemWindows(false);
        android.view.WindowInsetsController c = w.getInsetsController();
        if (c == null) return;
        c.hide(android.view.WindowInsets.Type.statusBars() | android.view.WindowInsets.Type.navigationBars());
        c.setSystemBarsBehavior(android.view.WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
    }
`;
function after(anchor, add) {
  const i = t.indexOf(anchor);
  if (i < 0) throw new Error('anchor not found: ' + anchor);
  t = t.slice(0, i + anchor.length) + add + t.slice(i + anchor.length);
}
after('mUnityPlayer.getFrameLayout().requestFocus();', '\n        princessImmersive();');
after('mUnityPlayer.onResume();', '\n        princessImmersive();');
after('super.onWindowFocusChanged(hasFocus);', '\n        if (hasFocus) princessImmersive();');
const last = t.lastIndexOf('}');
t = t.slice(0, last) + helper + t.slice(last);
fs.writeFileSync(dst, t);
console.log('UnityPlayerActivity: immersive patch applied');
