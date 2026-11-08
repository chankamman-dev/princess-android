package dev.kmprincess.cloud;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.RectF;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.StateListDrawable;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.TextUtils;
import android.util.Log;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowInsets;
import android.view.WindowInsetsController;
import android.view.WindowManager;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import com.google.zxing.BarcodeFormat;
import com.google.zxing.EncodeHintType;
import com.google.zxing.common.BitMatrix;
import com.google.zxing.qrcode.QRCodeWriter;

import java.io.File;
import java.io.InputStream;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.HashMap;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.atomic.AtomicBoolean;

// The game's home screen: Steam account, cloud saves, the game data (downloaded from Steam on first
// start) and the player settings, in front of the title art. Everything is reachable with a gamepad.
public final class LauncherActivity extends Activity {
    // Palette: night sky with a pink-violet accent, taken from the title art.
    private static final int BG = Color.rgb(11, 16, 38), CARD = Color.argb(245, 18, 24, 52), TEXT = Color.WHITE,
            SUB = Color.rgb(196, 205, 236), MUTED = Color.rgb(132, 143, 184), PINK = Color.rgb(255, 126, 179), VIOLET = Color.rgb(155, 123, 255),
            GOLD = Color.rgb(243, 207, 122), GREEN = Color.rgb(120, 220, 160), RED = Color.rgb(255, 120, 120), CHIP = Color.argb(46, 255, 255, 255);

    private final Handler ui = new Handler(Looper.getMainLooper());
    private FrameLayout root, overlay;
    private TextView chipSteam, chipCloud, chipData, progressLabel, progressDetail, notice;
    private Bar progressBar;
    private LinearLayout progressBox, buttons;
    private TextView primary;

    private volatile boolean syncing, syncedOk, installing;
    private volatile GameData.Check check;
    private volatile String checkError;
    private final AtomicBoolean cancel = new AtomicBoolean();
    private Runnable afterSync;
    private File localTest;

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        SteamCrypto.install();
        // Achievement cards can show here too (the test file is read on the first screen that comes up).
        AchievementToast.attach(this);
        String lt = getIntent().getStringExtra("localSource");
        if (lt != null) localTest = new File(lt);
        buildHome();
        hideBars();
        refresh();
        goOnline();
    }

    // Signed in and not in offline mode: sync the cloud saves and ask Steam about the game data.
    private void goOnline() {
        if (TokenStore.load(this) == null || Settings.offline(this)) return;
        startSync(null);
        startCheck();
    }

    @Override
    public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus) hideBars();
    }

    // ---------------------------------------------------------------- layout

    private void buildHome() {
        root = new FrameLayout(this);
        root.setBackgroundColor(BG);

        ImageView hero = new ImageView(this);
        hero.setScaleType(ImageView.ScaleType.CENTER_CROP);
        try (InputStream in = getAssets().open("port/hero.jpg")) {
            hero.setImageBitmap(BitmapFactory.decodeStream(in));
        } catch (Exception ignored) {
        }
        root.addView(hero, match());

        View shade = new View(this);
        GradientDrawable g = new GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM,
                new int[] { Color.argb(0, 11, 16, 38), Color.argb(150, 11, 16, 38), Color.argb(235, 11, 16, 38), BG });
        shade.setBackground(g);
        FrameLayout.LayoutParams sp = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, Gravity.BOTTOM);
        root.addView(shade, sp);
        root.post(() -> {
            sp.height = (int) (root.getHeight() * 0.62f);
            shade.setLayoutParams(sp);
        });

        LinearLayout panel = new LinearLayout(this);
        panel.setOrientation(LinearLayout.VERTICAL);
        panel.setPadding(dp(40), 0, dp(40), dp(26));
        panel.setClipChildren(false);
        panel.setClipToPadding(false);
        FrameLayout.LayoutParams pp = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.BOTTOM);
        root.addView(panel, pp);

        // progress (download / sync)
        progressBox = new LinearLayout(this);
        progressBox.setOrientation(LinearLayout.VERTICAL);
        progressBox.setVisibility(View.GONE);
        progressLabel = text(15, TEXT, true);
        progressBar = new Bar(this);
        progressDetail = text(13, SUB, false);
        progressBox.addView(progressLabel);
        LinearLayout.LayoutParams bp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(8));
        bp.setMargins(0, dp(8), 0, dp(6));
        progressBox.addView(progressBar, bp);
        progressBox.addView(progressDetail);
        LinearLayout.LayoutParams pbl = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        pbl.setMargins(0, 0, 0, dp(16));
        panel.addView(progressBox, pbl);

        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.BOTTOM);
        row.setClipChildren(false);
        panel.addView(row);

        LinearLayout info = new LinearLayout(this);
        info.setOrientation(LinearLayout.VERTICAL);
        row.addView(info, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

        LinearLayout titleRow = new LinearLayout(this);
        titleRow.setGravity(Gravity.CENTER_VERTICAL);
        TextView title = text(30, TEXT, true);
        title.setText("梦幻魔法公主");
        title.setShadowLayer(dp(6), 0, dp(1), Color.argb(160, 0, 0, 0));
        titleRow.addView(title);
        TextView tag = text(12, TEXT, true);
        tag.setText("STEAM · ANDROID");
        tag.setLetterSpacing(0.08f);
        tag.setPadding(dp(10), dp(3), dp(10), dp(3));
        tag.setBackground(gradient(PINK, VIOLET, dp(20)));
        LinearLayout.LayoutParams tl = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        tl.setMargins(dp(14), 0, 0, 0);
        titleRow.addView(tag, tl);
        info.addView(titleRow);

        TextView sub = text(14, SUB, false);
        GameData.Port port = GameData.port(this);
        sub.setText("Magical Princess  ·  移植包基于 Steam build " + port.build);
        sub.setPadding(0, dp(2), 0, dp(12));
        info.addView(sub);

        LinearLayout chips = new LinearLayout(this);
        chipSteam = chip();
        chipCloud = chip();
        chipData = chip();
        chips.addView(chipSteam);
        chips.addView(chipCloud);
        chips.addView(chipData);
        info.addView(chips);

        notice = text(13, GOLD, false);
        notice.setVisibility(View.GONE);
        notice.setPadding(0, dp(10), 0, 0);
        info.addView(notice);

        LinearLayout right = new LinearLayout(this);
        right.setOrientation(LinearLayout.VERTICAL);
        right.setGravity(Gravity.END);
        right.setClipChildren(false);
        row.addView(right);
        primary = button("开始游戏", true);
        right.addView(primary, new LinearLayout.LayoutParams(dp(250), dp(58)));
        buttons = new LinearLayout(this);
        buttons.setClipChildren(false);
        LinearLayout.LayoutParams bl = new LinearLayout.LayoutParams(dp(250), ViewGroup.LayoutParams.WRAP_CONTENT);
        bl.setMargins(0, dp(12), 0, 0);
        right.addView(buttons, bl);

        overlay = new FrameLayout(this);
        overlay.setBackgroundColor(Color.argb(170, 4, 6, 18));
        overlay.setVisibility(View.GONE);
        overlay.setClickable(true);
        root.addView(overlay, match());

        setContentView(root);
    }

    // Recomputes chips and buttons from the current state.
    private void refresh() {
        TokenStore t = TokenStore.load(this);
        boolean ready = GameData.ready(this);
        boolean offline = Settings.offline(this);

        if (offline) setChip(chipSteam, GOLD, "离线模式" + (t == null ? "" : " · " + t.account));
        else if (t == null) setChip(chipSteam, MUTED, "Steam 未登录");
        else setChip(chipSteam, GREEN, "Steam · " + t.account);

        if (t == null) setChip(chipCloud, MUTED, "云存档未开启");
        else if (offline) setChip(chipCloud, MUTED, "云存档暂停（离线）");
        else if (syncing) setChip(chipCloud, SUB, "云存档同步中…");
        else if (syncedOk) setChip(chipCloud, GREEN, "云存档已同步 " + new SimpleDateFormat("HH:mm", Locale.CHINA).format(new Date()));
        else setChip(chipCloud, GOLD, "云存档未同步");

        GameData.Check c = check;
        if (installing) setChip(chipData, SUB, "游戏数据下载中");
        else if (!ready) setChip(chipData, GOLD, "需要下载游戏数据");
        else if (c != null && c.needsNewApk) setChip(chipData, GOLD, "Steam 有新版本");
        else if (c != null && !c.upToDate && c.target != 0) setChip(chipData, GOLD, "游戏数据可更新");
        else setChip(chipData, GREEN, "游戏数据已就绪");

        if (c != null && c.needsNewApk) {
            notice.setText("Steam 上的游戏已更新，这次更新改了游戏程序，需要新版安卓移植包才能跟上；现在可以继续玩当前版本，存档不受影响。");
            notice.setVisibility(View.VISIBLE);
        } else if (checkError != null && !ready) {
            notice.setText(checkError);
            notice.setVisibility(View.VISIBLE);
        } else notice.setVisibility(View.GONE);

        buttons.removeAllViews();
        if (installing) {
            setPrimary("暂停下载", () -> cancel.set(true));
        } else if (!ready) {
            if (localTest != null) setPrimary("从测试目录安装", this::startInstall);
            else if (offline) setPrimary("退出离线模式以下载", () -> setOffline(false));
            else if (t == null) setPrimary("登录 Steam 下载游戏", this::showLogin);
            else {
                long bytes = c != null && c.target != 0 ? c.bytesToGet : GameData.port(this).bundleBytes;
                setPrimary("下载游戏数据  " + gb(bytes), this::startInstall);
            }
        } else if (c != null && !c.upToDate && !c.needsNewApk && c.target != 0 && c.filesToGet > 0) {
            setPrimary("开始游戏", this::play);
            addSecondary("更新 " + mb(c.bytesToGet), this::startInstall);
        } else {
            setPrimary("开始游戏", this::play);
        }
        addSecondary(t == null ? "登录 Steam" : "账户", t == null ? this::showLogin : this::showAccount);
        addSecondary("设置", this::showSettings);
        if (overlay.getVisibility() != View.VISIBLE && !primary.hasFocus()) primary.requestFocus();
    }

    private void setPrimary(String label, Runnable r) {
        primary.setText(label);
        primary.setOnClickListener(v -> r.run());
    }

    private void addSecondary(String label, Runnable r) {
        TextView b = button(label, false);
        b.setOnClickListener(v -> r.run());
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(0, dp(42), 1f);
        if (buttons.getChildCount() > 0) lp.setMargins(dp(10), 0, 0, 0);
        buttons.addView(b, lp);
    }

    // ---------------------------------------------------------------- cloud sync

    private void startSync(Runnable then) {
        if (then != null) afterSync = then;
        if (syncing) return;
        syncing = true;
        refresh();
        new Thread(() -> {
            String err = null;
            try {
                CloudSync.Result r = CloudSync.sync(this, s -> { }, true);
                Log.i("PrincessCloud", "sync: " + r);
            } catch (Throwable e) {
                err = message(e);
                Log.w("PrincessCloud", "sync failed: " + e);
            }
            final String error = err;
            final boolean rejected = false;
            ui.post(() -> {
                syncing = false;
                syncedOk = error == null;
                refresh();
                Runnable next = afterSync;
                afterSync = null;
                if (error != null && next != null) {
                    showMessage("云存档同步失败", error + "\n本机存档没有改动。可以先离线游玩，下次启动会再同步。",
                            new String[] { "离线进入游戏", "取消" }, new Runnable[] { this::enterGame, this::closeOverlay });
                } else if (next != null) next.run();
            });
        }, "PrincessCloud-sync").start();
    }

    private void play() {
        if (!GameData.ready(this)) return;
        if (TokenStore.load(this) == null || syncedOk || Settings.offline(this)) {
            enterGame();
            return;
        }
        showProgress("正在同步 Steam 云存档…", -1, "");
        startSync(this::enterGame);
    }

    private void enterGame() {
        // A new APK may carry better converted shader bundles than the device has: put them in first.
        if (GameData.shadersStale(this)) {
            showProgress("正在更新着色器…", -1, "");
            new Thread(() -> {
                try {
                    GameData.refreshShaders(this);
                } catch (Exception e) {
                    Log.w("PrincessLauncher", "shader refresh failed: " + e);
                }
                ui.post(this::launchGame);
            }, "PrincessData-shaders").start();
            return;
        }
        launchGame();
    }

    private void launchGame() {
        Intent i = new Intent();
        i.setClassName(this, "com.unity3d.player.UnityPlayerActivity");
        startActivity(i);
        finish();
    }

    // ---------------------------------------------------------------- game data

    private void startCheck() {
        new Thread(() -> {
            try {
                GameData.Check c = GameData.check(this, new GameData.Listener() {
                    @Override public void status(String s) { }
                    @Override public void progress(long done, long total, int files, int filesTotal) { }
                });
                check = c;
                checkError = null;
                Log.i("PrincessData", "check: installed " + c.installed + " apk " + c.apkBase + " latest " + c.latest + " needsNewApk " + c.needsNewApk
                        + " files " + c.filesToGet + " bytes " + c.bytesToGet);
            } catch (Throwable e) {
                checkError = "无法向 Steam 查询游戏数据：" + message(e);
                Log.w("PrincessData", "check failed: " + e);
            }
            ui.post(this::refresh);
        }, "PrincessData-check").start();
    }

    private void startInstall() {
        if (installing) return;
        GameData.Check c = check;
        long target = c != null && c.target != 0 ? c.target : GameData.port(this).manifest;
        installing = true;
        cancel.set(false);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        refresh();
        showProgress("正在连接 Steam…", -1, "下载时请保持在这个页面，可以随时暂停，下次会接着下载。");
        final long[] lastT = { System.currentTimeMillis() }, lastB = { 0 };
        final double[] speed = { 0 };
        new Thread(() -> {
            String err = null;
            boolean paused = false;
            try {
                GameData.install(this, target, localTest, new GameData.Listener() {
                    @Override
                    public void status(String s) {
                        ui.post(() -> progressLabel.setText(s));
                    }

                    @Override
                    public void progress(long done, long total, int files, int filesTotal) {
                        long now = System.currentTimeMillis();
                        if (now - lastT[0] >= 1000) {
                            double inst = (done - lastB[0]) * 1000.0 / (now - lastT[0]);
                            speed[0] = speed[0] == 0 ? inst : speed[0] * 0.7 + inst * 0.3;
                            lastT[0] = now;
                            lastB[0] = done;
                        }
                        float f = total > 0 ? (float) done / total : 0;
                        String eta = speed[0] > 1 ? "，约 " + duration((long) ((total - done) / speed[0])) : "";
                        String d = String.format(Locale.CHINA, "%s / %s  ·  %s/s%s  ·  %d / %d 个文件", gb(done), gb(total), mb((long) speed[0]), eta, files, filesTotal);
                        ui.post(() -> {
                            progressLabel.setText("正在下载游戏数据  " + Math.round(f * 100) + "%");
                            progressBar.set(f);
                            progressDetail.setText(d);
                        });
                    }
                }, cancel);
            } catch (InterruptedException e) {
                paused = true;
            } catch (Throwable e) {
                err = message(e);
                Log.w("PrincessData", "install failed", e);
            }
            final String error = err;
            final boolean wasPaused = paused;
            ui.post(() -> {
                installing = false;
                getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
                hideProgress();
                if (error == null && !wasPaused) {
                    check = null;
                    if (TokenStore.load(this) != null) startCheck();
                }
                refresh();
                if (error != null) showMessage("下载没有完成", error, new String[] { "重试", "关闭" }, new Runnable[] { () -> { closeOverlay(); startInstall(); }, this::closeOverlay });
                else if (wasPaused) toast("下载已暂停，下次会从这里继续");
                else toast("游戏数据已就绪");
            });
        }, "PrincessData-install").start();
    }

    private void showProgress(String label, float f, String detail) {
        progressBox.setVisibility(View.VISIBLE);
        progressLabel.setText(label);
        progressBar.set(f);
        progressDetail.setText(detail);
    }

    private void hideProgress() {
        progressBox.setVisibility(View.GONE);
    }

    // ---------------------------------------------------------------- overlays

    private LinearLayout card(String title, String body) {
        overlay.removeAllViews();
        LinearLayout c = new LinearLayout(this);
        c.setOrientation(LinearLayout.VERTICAL);
        c.setPadding(dp(32), dp(26), dp(32), dp(24));
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(CARD);
        bg.setCornerRadius(dp(18));
        bg.setStroke(dp(1), Color.argb(60, 255, 255, 255));
        c.setBackground(bg);
        TextView t = text(22, TEXT, true);
        t.setText(title);
        c.addView(t);
        if (body != null) {
            TextView b = text(15, SUB, false);
            b.setText(body);
            b.setLineSpacing(dp(3), 1f);
            b.setPadding(0, dp(10), 0, 0);
            c.addView(b);
        }
        FrameLayout.LayoutParams lp = new FrameLayout.LayoutParams(Math.min(dp(620), (int) (getResources().getDisplayMetrics().widthPixels * 0.86f)),
                ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.CENTER);
        overlay.addView(c, lp);
        overlay.setVisibility(View.VISIBLE);
        return c;
    }

    private LinearLayout cardButtons(LinearLayout card, String[] labels, Runnable[] actions) {
        LinearLayout row = new LinearLayout(this);
        row.setGravity(Gravity.END);
        for (int k = 0; k < labels.length; k++) {
            TextView b = button(labels[k], k == 0);
            final Runnable a = actions[k];
            b.setOnClickListener(v -> a.run());
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(44));
            if (k > 0) lp.setMargins(dp(12), 0, 0, 0);
            b.setPadding(dp(24), 0, dp(24), 0);
            row.addView(b, lp);
        }
        LinearLayout.LayoutParams rl = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        rl.setMargins(0, dp(22), 0, 0);
        card.addView(row, rl);
        if (row.getChildCount() > 0) row.getChildAt(0).requestFocus();
        return row;
    }

    private void showMessage(String title, String body, String[] labels, Runnable[] actions) {
        LinearLayout c = card(title, body);
        cardButtons(c, labels, actions);
    }

    private void closeOverlay() {
        overlay.setVisibility(View.GONE);
        overlay.removeAllViews();
        loginCancel.set(true);
        refresh();
        primary.requestFocus();
    }

    private final AtomicBoolean loginCancel = new AtomicBoolean();

    private void showLogin() {
        LinearLayout c = card("用 Steam 手机应用扫码登录", "打开手机上的 Steam，点「Steam 令牌」页的扫码按钮，扫描下面的二维码。\n登录后可以下载游戏数据、同步云存档、记录游戏时长和成就。");
        ImageView qr = new ImageView(this);
        qr.setBackgroundColor(Color.WHITE);
        int pad = dp(10);
        qr.setPadding(pad, pad, pad, pad);
        LinearLayout.LayoutParams ql = new LinearLayout.LayoutParams(dp(220), dp(220));
        ql.gravity = Gravity.CENTER_HORIZONTAL;
        ql.setMargins(0, dp(18), 0, 0);
        c.addView(qr, ql);
        TextView st = text(14, MUTED, false);
        st.setGravity(Gravity.CENTER);
        st.setText("正在向 Steam 申请二维码…");
        st.setPadding(0, dp(10), 0, 0);
        c.addView(st);
        cardButtons(c, new String[] { "取消" }, new Runnable[] { this::closeOverlay });
        loginCancel.set(false);
        AtomicBoolean myCancel = loginCancel;
        new Thread(() -> {
            try (SteamSession s = new SteamSession()) {
                String[] r = QrLogin.run(s, url -> ui.post(() -> {
                    Bitmap bmp = qrBitmap(url);
                    if (bmp != null) qr.setImageBitmap(bmp);
                    st.setText("等待扫码确认…");
                }));
                if (myCancel.get()) return;
                TokenStore.save(this, r[0], r[1]);
                ui.post(() -> {
                    closeOverlay();
                    toast("已登录 Steam：" + r[0]);
                    startSync(null);
                    startCheck();
                });
            } catch (Throwable t) {
                if (myCancel.get()) return;
                ui.post(() -> showMessage("登录没有完成", message(t), new String[] { "重试", "关闭" }, new Runnable[] { this::showLogin, this::closeOverlay }));
            }
        }, "PrincessCloud-login").start();
    }

    private void showAccount() {
        TokenStore t = TokenStore.load(this);
        if (t == null) {
            showLogin();
            return;
        }
        LinearLayout c = card("Steam 账户", "已登录：" + t.account + "\n\n云存档会在启动游戏前下载、存档后自动上传；游戏时长和成就会记到这个账号上。"
                + (Settings.offline(this) ? "\n\n现在是离线模式，不会连接 Steam。可以在「设置」里切回在线。" : ""));
        cardButtons(c, new String[] { "关闭", "退出登录" }, new Runnable[] { this::closeOverlay, () -> {
            TokenStore.clear(this);
            CloudSync.forget(this);
            SteamPlay.forget(this);
            syncedOk = false;
            check = null;
            closeOverlay();
            toast("已退出 Steam 登录");
        } });
    }

    private void showSettings() {
        LinearLayout c = card("设置", null);
        ScrollView sv = new ScrollView(this);
        LinearLayout list = new LinearLayout(this);
        list.setOrientation(LinearLayout.VERTICAL);
        sv.addView(list);
        // Fixed height, rows scroll inside: the card must leave room for its buttons on a 1080p handheld.
        int listH = getResources().getDisplayMetrics().heightPixels - dp(210);
        LinearLayout.LayoutParams sl = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, listH);
        sl.setMargins(0, dp(12), 0, 0);
        c.addView(sv, sl);

        list.addView(choiceRow("帧率上限", "游戏原本会跟随屏幕刷新率；手持设备上 60 帧更省电、更稳定。", Settings.FPS, Settings.FPS_LABEL, "fps", 60));
        list.addView(choiceRow("渲染分辨率", "低于 100% 时画面略软，但 GPU 负担更小、更凉快。", Settings.SCALE, Settings.SCALE_LABEL, "scale", 100));
        list.addView(choiceRow("画面后处理", "泛光、调色等效果。关掉后更省电、更凉快，但颜色和光晕会和原版不同。", new int[] { 1, 0 }, new String[] { "开（原版效果）", "关（更省电）" }, "post", 1));
        list.addView(choiceRow("Steam 连接", "离线时不连接 Steam、直接进游戏，不同步云存档、不记时长和成就；切回在线后会补传存档、补记成就。", new int[] { 0, 1 }, new String[] { "在线", "离线模式" }, "offline", 0,
                v -> { syncedOk = false; if (v == 0) goOnline(); refresh(); }));
        list.addView(choiceRow("资源加载", "快速：资源一次加载完，启动快很多；兼容：按游戏原来的方式逐帧加载，启动慢，但游戏中加载大资源时不会卡一下。", new int[] { 1, 0 }, new String[] { "快速（推荐）", "兼容" }, "syncload", 1));
        list.addView(choiceRow("开发者日志", "排查问题时打开，游戏日志会写进 logcat。", new int[] { 0, 1 }, new String[] { "关", "开" }, "devlog", 0));

        GameData.State st = GameData.state(this);
        GameData.Port p = GameData.port(this);
        String data = GameData.ready(this)
                ? (st.complete ? "已安装（Steam 清单 " + Long.toUnsignedString(st.manifest) + "，" + st.count() + " 个文件）" : "已就绪（开发者推送的数据）")
                : "未安装";
        TextView about = text(13, MUTED, false);
        about.setText("游戏数据：" + data + "\n移植包：基于 Steam build " + p.build + "，depot 清单 " + Long.toUnsignedString(p.manifest)
                + "\n可用存储空间：" + gb(GameData.freeBytes(this)));
        about.setPadding(0, dp(14), 0, 0);
        list.addView(about);

        cardButtons(c, new String[] { "完成", "检查游戏数据" }, new Runnable[] { this::closeOverlay, () -> {
            closeOverlay();
            if (Settings.offline(this) && localTest == null) toast("离线模式下不能检查游戏数据，请先在设置里切回在线");
            else if (TokenStore.load(this) == null && localTest == null) toast("检查和补全游戏数据需要先登录 Steam");
            else startInstall();
        } });
    }

    private View choiceRow(String title, String hint, int[] values, String[] labels, String key, int def) {
        return choiceRow(title, hint, values, labels, key, def, null);
    }

    private void setOffline(boolean on) {
        Settings.put(this, "offline", on ? 1 : 0);
        syncedOk = false;
        if (!on) {
            toast("已回到在线模式");
            goOnline();
        }
        refresh();
    }

    private View choiceRow(String title, String hint, int[] values, String[] labels, String key, int def, java.util.function.IntConsumer changed) {
        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);
        row.setPadding(dp(16), dp(12), dp(16), dp(12));
        row.setFocusable(true);
        row.setBackground(rowBackground());
        LinearLayout texts = new LinearLayout(this);
        texts.setOrientation(LinearLayout.VERTICAL);
        TextView t = text(16, TEXT, true);
        t.setText(title);
        TextView h = text(12, MUTED, false);
        h.setText(hint);
        texts.addView(t);
        texts.addView(h);
        row.addView(texts, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        TextView v = text(15, PINK, true);
        row.addView(v);
        final int[] idx = { 0 };
        int cur = Settings.get(this, key, def);
        for (int i = 0; i < values.length; i++) if (values[i] == cur) idx[0] = i;
        v.setText("‹  " + labels[idx[0]] + "  ›");
        Runnable step = () -> {
            idx[0] = (idx[0] + 1) % values.length;
            Settings.put(this, key, values[idx[0]]);
            v.setText("‹  " + labels[idx[0]] + "  ›");
            if (changed != null) changed.accept(values[idx[0]]);
        };
        row.setOnClickListener(x -> step.run());
        row.setOnKeyListener((x, code, ev) -> {
            if (ev.getAction() != KeyEvent.ACTION_DOWN) return false;
            if (code == KeyEvent.KEYCODE_DPAD_RIGHT) { step.run(); return true; }
            if (code == KeyEvent.KEYCODE_DPAD_LEFT) {
                idx[0] = (idx[0] + values.length - 2) % values.length;
                step.run();
                return true;
            }
            return false;
        });
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        lp.setMargins(0, dp(4), 0, dp(4));
        row.setLayoutParams(lp);
        return row;
    }

    private void toast(String s) {
        TextView t = text(15, TEXT, false);
        t.setText(s);
        t.setPadding(dp(20), dp(10), dp(20), dp(10));
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(Color.argb(230, 30, 36, 70));
        bg.setCornerRadius(dp(22));
        t.setBackground(bg);
        FrameLayout.LayoutParams lp = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.TOP | Gravity.CENTER_HORIZONTAL);
        lp.setMargins(0, dp(24), 0, 0);
        root.addView(t, lp);
        t.setAlpha(0f);
        t.setTranslationY(-dp(12));
        t.animate().alpha(1f).translationY(0).setDuration(220).start();
        ui.postDelayed(() -> t.animate().alpha(0f).translationY(-dp(12)).setDuration(220).withEndAction(() -> root.removeView(t)).start(), 2600);
    }

    // ---------------------------------------------------------------- input

    @Override
    public boolean dispatchKeyEvent(KeyEvent e) {
        int code = e.getKeyCode();
        if (code == KeyEvent.KEYCODE_BUTTON_A) {
            if (e.getAction() == KeyEvent.ACTION_UP) {
                // Right after launch the window is in touch mode and nothing has focus: A then means
                // the main button (or the first button of an open card).
                View f = getCurrentFocus();
                if (f == null) f = overlay.getVisibility() == View.VISIBLE ? firstFocusable(overlay) : primary;
                if (f != null) {
                    f.requestFocus();
                    f.performClick();
                }
            }
            return true;
        }
        if (getCurrentFocus() == null && e.getAction() == KeyEvent.ACTION_DOWN
                && (code == KeyEvent.KEYCODE_DPAD_UP || code == KeyEvent.KEYCODE_DPAD_DOWN || code == KeyEvent.KEYCODE_DPAD_LEFT || code == KeyEvent.KEYCODE_DPAD_RIGHT)) {
            View f = overlay.getVisibility() == View.VISIBLE ? firstFocusable(overlay) : primary;
            if (f != null) f.requestFocus();
            return true;
        }
        if (code == KeyEvent.KEYCODE_BUTTON_B || code == KeyEvent.KEYCODE_BACK) {
            if (e.getAction() == KeyEvent.ACTION_UP) {
                if (overlay.getVisibility() == View.VISIBLE) closeOverlay();
                else if (code == KeyEvent.KEYCODE_BACK) finish();
            }
            return true;
        }
        return super.dispatchKeyEvent(e);
    }

    private static View firstFocusable(View v) {
        if (v.isFocusable() && v.getVisibility() == View.VISIBLE && !(v instanceof ViewGroup && ((ViewGroup) v).getChildCount() > 0 && !(v instanceof LinearLayout && v.isClickable()))) return v;
        if (v instanceof ViewGroup) {
            ViewGroup g = (ViewGroup) v;
            for (int i = 0; i < g.getChildCount(); i++) {
                View f = firstFocusable(g.getChildAt(i));
                if (f != null) return f;
            }
        }
        return v.isFocusable() ? v : null;
    }

    // ---------------------------------------------------------------- widgets

    private TextView text(int sp, int color, boolean bold) {
        TextView t = new TextView(this);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, sp);
        t.setTextColor(color);
        if (bold) t.setTypeface(Typeface.create("sans-serif-medium", Typeface.BOLD));
        return t;
    }

    private TextView chip() {
        TextView t = text(13, TEXT, false);
        t.setPadding(dp(12), dp(5), dp(14), dp(5));
        t.setSingleLine(true);
        t.setEllipsize(TextUtils.TruncateAt.END);
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(CHIP);
        bg.setCornerRadius(dp(16));
        t.setBackground(bg);
        t.setCompoundDrawablePadding(dp(7));
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        lp.setMargins(0, 0, dp(10), 0);
        t.setLayoutParams(lp);
        return t;
    }

    private void setChip(TextView chip, int dot, String label) {
        GradientDrawable d = new GradientDrawable();
        d.setShape(GradientDrawable.OVAL);
        d.setColor(dot);
        d.setSize(dp(8), dp(8));
        chip.setCompoundDrawablesRelativeWithIntrinsicBounds(d, null, null, null);
        chip.setText(label);
    }

    private TextView button(String label, boolean main) {
        TextView b = text(main ? 18 : 15, TEXT, true);
        b.setText(label);
        b.setGravity(Gravity.CENTER);
        b.setFocusable(true);
        b.setClickable(true);
        b.setSingleLine(true);
        StateListDrawable s = new StateListDrawable();
        int r = dp(main ? 29 : 21);
        GradientDrawable normal = main ? gradient(PINK, VIOLET, r) : solid(Color.argb(60, 255, 255, 255), r);
        GradientDrawable focused = main ? gradient(PINK, VIOLET, r) : solid(Color.argb(110, 255, 255, 255), r);
        focused.setStroke(dp(3), Color.WHITE);
        GradientDrawable pressed = main ? gradient(Color.rgb(230, 100, 160), Color.rgb(130, 100, 230), r) : solid(Color.argb(140, 255, 255, 255), r);
        s.addState(new int[] { android.R.attr.state_pressed }, pressed);
        s.addState(new int[] { android.R.attr.state_focused }, focused);
        s.addState(new int[0], normal);
        b.setBackground(s);
        b.setOnFocusChangeListener((v, has) -> v.animate().scaleX(has ? 1.04f : 1f).scaleY(has ? 1.04f : 1f).setDuration(120).start());
        return b;
    }

    private StateListDrawable rowBackground() {
        StateListDrawable s = new StateListDrawable();
        GradientDrawable f = solid(Color.argb(40, 255, 255, 255), dp(12));
        f.setStroke(dp(2), PINK);
        s.addState(new int[] { android.R.attr.state_focused }, f);
        s.addState(new int[0], solid(Color.argb(16, 255, 255, 255), dp(12)));
        return s;
    }

    private static GradientDrawable gradient(int a, int b, int r) {
        GradientDrawable g = new GradientDrawable(GradientDrawable.Orientation.LEFT_RIGHT, new int[] { a, b });
        g.setCornerRadius(r);
        return g;
    }

    private static GradientDrawable solid(int c, int r) {
        GradientDrawable g = new GradientDrawable();
        g.setColor(c);
        g.setCornerRadius(r);
        return g;
    }

    // Rounded progress bar; f < 0 shows an indeterminate sweep.
    private final class Bar extends View {
        private float f = -1, sweep;
        private final Paint track = new Paint(Paint.ANTI_ALIAS_FLAG), fill = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final RectF rect = new RectF();

        Bar(Context c) {
            super(c);
            track.setColor(Color.argb(50, 255, 255, 255));
        }

        void set(float v) {
            f = v;
            invalidate();
        }

        @Override
        protected void onDraw(Canvas c) {
            float w = getWidth(), h = getHeight(), r = h / 2;
            rect.set(0, 0, w, h);
            c.drawRoundRect(rect, r, r, track);
            fill.setShader(new android.graphics.LinearGradient(0, 0, w, 0, PINK, VIOLET, android.graphics.Shader.TileMode.CLAMP));
            if (f >= 0) {
                rect.set(0, 0, Math.max(h, w * Math.min(1f, f)), h);
                c.drawRoundRect(rect, r, r, fill);
            } else {
                sweep = (sweep + 0.012f) % 1.3f;
                float x = (sweep - 0.3f) * w;
                rect.set(Math.max(0, x), 0, Math.min(w, x + w * 0.3f), h);
                if (rect.width() > 0) c.drawRoundRect(rect, r, r, fill);
                postInvalidateOnAnimation();
            }
        }
    }

    private Bitmap qrBitmap(String url) {
        try {
            Map<EncodeHintType, Object> hints = new HashMap<>();
            hints.put(EncodeHintType.MARGIN, 0);
            BitMatrix m = new QRCodeWriter().encode(url, BarcodeFormat.QR_CODE, 400, 400, hints);
            int w = m.getWidth(), h = m.getHeight();
            int[] px = new int[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = m.get(x, y) ? Color.rgb(11, 16, 38) : Color.WHITE;
            return Bitmap.createBitmap(px, w, h, Bitmap.Config.RGB_565);
        } catch (Throwable t) {
            return null;
        }
    }

    // ---------------------------------------------------------------- helpers

    private FrameLayout.LayoutParams match() {
        return new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT);
    }

    private int dp(int v) {
        return Math.round(v * getResources().getDisplayMetrics().density);
    }

    static String gb(long b) {
        return b >= 1e9 ? String.format(Locale.CHINA, "%.1f GB", b / 1e9) : mb(b);
    }

    static String mb(long b) {
        return String.format(Locale.CHINA, "%.0f MB", b / 1e6);
    }

    static String duration(long s) {
        if (s < 60) return s + " 秒";
        if (s < 3600) return (s / 60) + " 分钟";
        return (s / 3600) + " 小时 " + (s % 3600 / 60) + " 分";
    }

    private static String message(Throwable t) {
        while (t.getCause() != null && (t.getMessage() == null || t.getMessage().isEmpty())) t = t.getCause();
        String m = t.getMessage();
        return m == null || m.isEmpty() ? t.getClass().getSimpleName() : m;
    }

    private void hideBars() {
        if (android.os.Build.VERSION.SDK_INT >= 30) {
            WindowInsetsController c = getWindow().getInsetsController();
            if (c != null) {
                c.hide(WindowInsets.Type.systemBars());
                c.setSystemBarsBehavior(WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
            }
        } else {
            getWindow().getDecorView().setSystemUiVisibility(0x1706);
        }
    }
}
