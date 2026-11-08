package dev.kmprincess.cloud;

import android.content.Context;

// The launcher's player settings, read by the game through CloudBridge.intSetting.
final class Settings {
    static final String PREFS = "princess_settings";

    static final int[] FPS = { 30, 60, 90, 120 };
    static final String[] FPS_LABEL = { "30 帧（省电）", "60 帧（推荐）", "90 帧", "120 帧" };
    static final int[] SCALE = { 100, 85, 70 };
    static final String[] SCALE_LABEL = { "100%（原生分辨率）", "85%（更流畅）", "70%（最省电）" };

    static int get(Context c, String key, int def) {
        return c.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getInt(key, def);
    }

    static void put(Context c, String key, int v) {
        c.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putInt(key, v).apply();
    }

    static int fps(Context c) {
        return get(c, "fps", 60);
    }

    static int scale(Context c) {
        return get(c, "scale", 100);
    }

    // Offline mode: the launcher and the game never contact Steam (no cloud saves, playtime or
    // achievements). Saves made meanwhile are uploaded by the next sync; achievements are offered
    // again when a save is loaded online.
    static boolean offline(Context c) {
        return get(c, "offline", 0) != 0;
    }

    static boolean devLog(Context c) {
        return get(c, "devlog", 0) != 0;
    }

    private Settings() { }
}
