package dev.kmprincess.cloud;

import android.app.Activity;
import android.app.Application;
import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.TextUtils;
import android.util.Log;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.animation.AccelerateInterpolator;
import android.view.animation.DecelerateInterpolator;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.PopupWindow;
import android.widget.TextView;

import java.io.File;
import java.io.FileOutputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;

import in.dragonbra.javasteam.types.KeyValue;
import okhttp3.OkHttpClient;
import okhttp3.Request;
import okhttp3.Response;

// The "achievement unlocked" card the Steam overlay shows on the desktop, drawn over the game when
// Steam confirms an unlock (SteamPlay.flushAchievements). Name, description and icon come from the
// game's Steam schema in the device language. The card rises from the bottom-right corner, stays a
// few seconds and goes back down the way it came. A batch (the first sign-in offers every
// achievement the save already holds) shows up to three cards and then one summary card.
// It never takes focus or touches: the game keeps all input.
final class AchievementToast {
    private static final String TAG = "PrincessCloud";
    private static final String ICONS = "https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/" + CloudSync.APP + "/";
    private static final long SHOW_MS = 4500;
    private static final int MAX_CARDS = 3;

    static final class Item {
        final String name, desc, icon;
        Bitmap bitmap;
        int more; // a summary card: this many more were unlocked

        Item(String name, String desc, String icon) {
            this.name = name;
            this.desc = desc;
            this.icon = icon;
        }
    }

    private static final Handler ui = new Handler(Looper.getMainLooper());
    private static final ExecutorService worker = Executors.newSingleThreadExecutor(r -> {
        Thread t = new Thread(r, "PrincessCloud-toast");
        t.setDaemon(true);
        return t;
    });
    private static final ArrayDeque<Item> queue = new ArrayDeque<>();
    private static Activity resumed;
    private static boolean registered, showing;
    private static Context app;

    // The game is on screen (CloudBridge.onGameStarted): from here on cards go over this activity.
    static void attach(Activity a) {
        ui.post(() -> {
            app = a.getApplicationContext();
            resumed = a;
            if (!registered) {
                registered = true;
                ((Application) app).registerActivityLifecycleCallbacks(new Application.ActivityLifecycleCallbacks() {
                    @Override public void onActivityResumed(Activity x) { resumed = x; next(); }
                    @Override public void onActivityPaused(Activity x) { if (resumed == x) resumed = null; }
                    @Override public void onActivityCreated(Activity x, Bundle b) { }
                    @Override public void onActivityStarted(Activity x) { }
                    @Override public void onActivityStopped(Activity x) { }
                    @Override public void onActivitySaveInstanceState(Activity x, Bundle b) { }
                    @Override public void onActivityDestroyed(Activity x) { }
                });
            }
            next();
        });
        testHook(a);
    }

    // Names just unlocked on Steam, described by the stats schema Steam sent with the user's stats.
    static void unlocked(Context c, KeyValue schema, List<String> names) {
        if (names.isEmpty()) return;
        String lang = steamLanguage();
        List<Item> items = new ArrayList<>();
        for (String n : names) items.add(describe(schema, n, lang));
        post(c, items);
    }

    static void post(Context c, List<Item> items) {
        if (items.isEmpty()) return;
        Context appCtx = c.getApplicationContext();
        List<Item> cards = new ArrayList<>(items.subList(0, Math.min(items.size(), MAX_CARDS)));
        if (items.size() > MAX_CARDS) {
            Item sum = new Item(null, null, items.get(MAX_CARDS).icon);
            sum.more = items.size() - MAX_CARDS;
            cards.add(sum);
        }
        worker.execute(() -> {
            for (Item it : cards) it.bitmap = icon(appCtx, it.icon);
            ui.post(() -> {
                queue.addAll(cards);
                next();
            });
        });
    }

    // ---- schema ----

    private static Item describe(KeyValue schema, String name, String lang) {
        if (schema != null) {
            for (KeyValue stat : schema.getChildren()) {
                for (KeyValue bit : stat.get("bits").getChildren()) {
                    if (!name.equals(bit.get("name").getValue())) continue;
                    KeyValue d = bit.get("display");
                    return new Item(text(d.get("name"), lang, name), text(d.get("desc"), lang, ""), d.get("icon").getValue());
                }
            }
        }
        return new Item(name, "", null);
    }

    private static String text(KeyValue kv, String lang, String fallback) {
        String v = kv.get(lang).getValue();
        if (TextUtils.isEmpty(v)) v = kv.get("english").getValue();
        if (TextUtils.isEmpty(v)) v = kv.getValue(); // a plain string in older schemas
        return TextUtils.isEmpty(v) ? fallback : v;
    }

    // Steam's language names for the device language.
    static String steamLanguage() {
        Locale l = Locale.getDefault();
        switch (l.getLanguage()) {
            case "zh":
                String s = l.getScript(), r = l.getCountry();
                return "Hant".equals(s) || "TW".equals(r) || "HK".equals(r) || "MO".equals(r) ? "tchinese" : "schinese";
            case "ja": return "japanese";
            case "ko": return "koreana";
            default: return "english";
        }
    }

    // ---- icons: Steam's CDN, cached in the app's cache dir ----

    private static OkHttpClient http;

    private static Bitmap icon(Context c, String hash) {
        if (TextUtils.isEmpty(hash) || !hash.matches("[0-9a-f]{40}\\.(jpg|png)")) return null;
        File f = new File(new File(c.getCacheDir(), "achievement-icons"), hash);
        try {
            if (!f.isFile()) {
                if (http == null) http = new OkHttpClient.Builder().connectTimeout(6, TimeUnit.SECONDS).readTimeout(8, TimeUnit.SECONDS).build();
                try (Response r = http.newCall(new Request.Builder().url(ICONS + hash).build()).execute()) {
                    if (!r.isSuccessful() || r.body() == null) throw new Exception("HTTP " + r.code());
                    byte[] b = r.body().bytes();
                    f.getParentFile().mkdirs();
                    File tmp = new File(f.getPath() + ".part");
                    try (FileOutputStream o = new FileOutputStream(tmp)) { o.write(b); }
                    if (!tmp.renameTo(f)) throw new Exception("rename");
                }
            }
            return BitmapFactory.decodeFile(f.getPath());
        } catch (Throwable t) {
            Log.w(TAG, "achievement icon " + hash + ": " + t);
            return null;
        }
    }

    // ---- the card ----

    private static void next() {
        if (showing || queue.isEmpty() || resumed == null || resumed.isFinishing()) return;
        Activity a = resumed;
        ViewGroup root = (ViewGroup) a.getWindow().getDecorView();
        Item it = queue.poll();
        View card;
        try {
            card = card(a, it);
        } catch (Throwable t) {
            Log.w(TAG, "achievement card: " + t);
            next();
            return;
        }
        // A window of its own (not a child of the game's window, which would then be redrawn over
        // Unity's surface on every animation frame). It reaches down to the screen edge, so the
        // card can slide in from below it and leave the same way. No focus, no touches.
        FrameLayout box = new FrameLayout(a);
        FrameLayout.LayoutParams lp = new FrameLayout.LayoutParams(dp(a, 280), ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.BOTTOM);
        lp.setMargins(dp(a, 12), dp(a, 12), dp(a, 20), dp(a, 20));
        box.addView(card, lp);
        PopupWindow w = new PopupWindow(box, ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT, false);
        w.setTouchable(false);
        w.setFocusable(false);
        w.setOutsideTouchable(false);
        w.setInputMethodMode(PopupWindow.INPUT_METHOD_NOT_NEEDED);
        w.setBackgroundDrawable(null);
        w.setAnimationStyle(0);
        try {
            w.showAtLocation(root, Gravity.BOTTOM | Gravity.END, 0, 0);
        } catch (Throwable t) {
            Log.w(TAG, "achievement card window: " + t);
            ui.postDelayed(AchievementToast::next, 1000);
            return;
        }
        showing = true;
        float off = dp(a, 160);
        card.setTranslationY(off);
        card.setAlpha(0f);
        card.animate().translationY(0).alpha(1f).setDuration(320).setInterpolator(new DecelerateInterpolator(2f)).start();
        ui.postDelayed(() -> card.animate().translationY(off).alpha(0f).setDuration(240).setInterpolator(new AccelerateInterpolator(1.5f))
                .withEndAction(() -> {
                    try { w.dismiss(); } catch (Throwable ignored) { }
                    showing = false;
                    ui.postDelayed(AchievementToast::next, 250);
                }).start(), SHOW_MS);
        Log.i(TAG, "achievement card: " + (it.more > 0 ? "+" + it.more : it.name));
    }

    private static View card(Activity a, Item it) {
        LinearLayout row = new LinearLayout(a);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);
        int p = dp(a, 10);
        row.setPadding(p, p, dp(a, 14), p);
        GradientDrawable bg = new GradientDrawable(GradientDrawable.Orientation.TL_BR,
                new int[] { Color.argb(250, 38, 42, 66), Color.argb(250, 24, 26, 42) });
        bg.setCornerRadius(dp(a, 12));
        bg.setStroke(dp(a, 1), Color.argb(70, 255, 126, 179));
        row.setBackground(bg);
        row.setElevation(dp(a, 8));
        row.setClickable(false);
        row.setFocusable(false);
        row.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_YES);

        int size = dp(a, 48);
        View iconView;
        if (it.bitmap != null) {
            ImageView iv = new ImageView(a);
            iv.setImageBitmap(it.bitmap);
            iv.setScaleType(ImageView.ScaleType.CENTER_CROP);
            iv.setClipToOutline(true);
            GradientDrawable clip = new GradientDrawable();
            clip.setCornerRadius(dp(a, 8));
            iv.setBackground(clip);
            iconView = iv;
        } else {
            TextView star = new TextView(a);
            star.setText(it.more > 0 ? "+" + it.more : "★");
            star.setTextColor(Color.WHITE);
            star.setTextSize(TypedValue.COMPLEX_UNIT_SP, it.more > 0 ? 18 : 26);
            star.setTypeface(Typeface.DEFAULT_BOLD);
            star.setGravity(Gravity.CENTER);
            GradientDrawable g = new GradientDrawable(GradientDrawable.Orientation.TL_BR,
                    new int[] { Color.rgb(255, 126, 179), Color.rgb(155, 123, 255) });
            g.setCornerRadius(dp(a, 8));
            star.setBackground(g);
            iconView = star;
        }
        row.addView(iconView, new LinearLayout.LayoutParams(size, size));

        LinearLayout col = new LinearLayout(a);
        col.setOrientation(LinearLayout.VERTICAL);
        LinearLayout.LayoutParams cp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        cp.leftMargin = dp(a, 12);
        row.addView(col, cp);

        boolean zh = steamLanguage().endsWith("chinese");
        String head, title, sub;
        if (it.more > 0) {
            head = zh ? "Steam 成就" : "Steam achievements";
            title = zh ? "另有 " + it.more + " 个成就已解锁" : it.more + " more unlocked";
            sub = zh ? "已同步到你的 Steam 账号" : "Synced to your Steam account";
        } else {
            head = zh ? "成就已解锁" : "Achievement unlocked";
            title = it.name;
            sub = it.desc;
        }
        col.addView(label(a, head, 11, Color.rgb(255, 160, 200), false, 1));
        col.addView(label(a, title, 14, Color.WHITE, true, 2));
        if (!TextUtils.isEmpty(sub)) col.addView(label(a, sub, 11, Color.rgb(170, 178, 210), false, 2));
        row.setContentDescription(head + "：" + title);
        return row;
    }

    private static TextView label(Context c, String s, int sp, int color, boolean bold, int lines) {
        TextView t = new TextView(c);
        t.setText(s);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, sp);
        t.setTextColor(color);
        t.setMaxLines(lines);
        t.setEllipsize(TextUtils.TruncateAt.END);
        if (bold) t.setTypeface(Typeface.create("sans-serif-medium", Typeface.BOLD));
        t.setIncludeFontPadding(false);
        t.setPadding(0, dp(c, 2), 0, dp(c, 2));
        return t;
    }

    private static int dp(Context c, int v) {
        return Math.round(v * c.getResources().getDisplayMetrics().density);
    }

    // Testing without a Steam account: `run-as dev.kmprincess.magical` can write
    // files/achievement-test.txt (lines "name<TAB>description<TAB>icon hash"); the next game start
    // shows those cards and deletes the file. Only the app itself (or run-as on this debuggable
    // build) can write there.
    private static void testHook(Activity a) {
        File f = new File(a.getFilesDir(), "achievement-test.txt");
        if (!f.isFile()) return;
        Context appCtx = a.getApplicationContext();
        worker.execute(() -> {
            try {
                List<Item> items = new ArrayList<>();
                for (String line : new String(Files.readAllBytes(f.toPath()), StandardCharsets.UTF_8).split("\n")) {
                    String[] p = line.trim().split("\t");
                    if (p.length > 0 && !p[0].isEmpty()) items.add(new Item(p[0], p.length > 1 ? p[1] : "", p.length > 2 ? p[2] : null));
                }
                f.delete();
                Log.i(TAG, "achievement test cards: " + items.size());
                Thread.sleep(4000);
                post(appCtx, items);
            } catch (Throwable t) {
                Log.w(TAG, "achievement test: " + t);
            }
        });
    }

    private AchievementToast() { }
}
