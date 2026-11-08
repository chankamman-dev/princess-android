package dev.kmprincess.cloud;

import android.content.Context;
import android.content.res.AssetManager;
import android.os.StatFs;
import android.util.Log;

import java.io.BufferedInputStream;
import java.io.BufferedOutputStream;
import java.io.BufferedReader;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.io.OutputStreamWriter;
import java.io.RandomAccessFile;
import java.io.Writer;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicLong;

import in.dragonbra.javasteam.types.DepotManifest;
import in.dragonbra.javasteam.types.FileData;

// The game's Addressables bundles (about 6.4 GB) live in files/aa. They come from the Windows depot on
// Steam: each bundle is downloaded and converted on the device (BundleStamp), except the few hundred
// that carry shaders, which the port converted on the PC and ships in the APK (assets/port/shaders).
//
// The APK is built from one depot manifest (port.txt: "manifest"). A first install downloads exactly
// that version. When Steam has a newer one, it can be installed here only if nothing the APK carries
// (code, core data) changed; otherwise the port needs a new APK, and the bundles stay as they are.
// files/aa/.princess-state records the manifest and the SHA-1 of every installed bundle, so an
// interrupted download resumes and a new manifest only fetches what changed.
public final class GameData {
    private static final String TAG = "PrincessData";

    // ---- what the APK was built from ----
    public static final class Port {
        public int app, depot, bundleCount;
        public long manifest, build;
        public String prefix, shaderSet;
        public long bundleBytes;
        final Map<String, String> base = new HashMap<>();     // depot path -> sha1 (files baked into the APK)
        final Map<String, String> shaders = new HashMap<>();  // bundle name -> sha1 of the Windows original
    }

    private static Port port;

    public static synchronized Port port(Context c) {
        if (port != null) return port;
        Port p = new Port();
        try (BufferedReader r = new BufferedReader(new InputStreamReader(c.getAssets().open("port/port.txt"), StandardCharsets.UTF_8))) {
            for (String line; (line = r.readLine()) != null; ) {
                int sp = line.indexOf(' ');
                if (sp < 0) continue;
                String k = line.substring(0, sp), v = line.substring(sp + 1);
                switch (k) {
                    case "app": p.app = Integer.parseInt(v); break;
                    case "depot": p.depot = Integer.parseInt(v); break;
                    case "manifest": p.manifest = Long.parseUnsignedLong(v); break;
                    case "build": p.build = Long.parseLong(v); break;
                    case "prefix": p.prefix = v; break;
                    case "bundles": p.bundleCount = Integer.parseInt(v); break;
                    case "bundlebytes": p.bundleBytes = Long.parseLong(v); break;
                    case "base": p.base.put(v.substring(41), v.substring(0, 40)); break;
                    case "shader": p.shaders.put(v.substring(41), v.substring(0, 40)); break;
                    case "shaderset": p.shaderSet = v.trim(); break;
                    default: break;
                }
            }
        } catch (IOException e) {
            throw new IllegalStateException("APK 里缺少 port/port.txt", e);
        }
        port = p;
        return p;
    }

    // ---- installed state ----
    public static final class State {
        public long manifest;          // 0 = unknown / incomplete
        public long apk;               // the APK base manifest this install was made for
        public boolean complete;
        final Map<String, String> files = new HashMap<>(); // bundle name -> sha1 of its Windows original

        public int count() {
            return files.size();
        }
    }

    public static File dir(Context c) {
        return new File(c.getFilesDir(), "aa");
    }

    private static File stateFile(Context c) {
        return new File(dir(c), ".princess-state");
    }

    public static State state(Context c) {
        State s = new State();
        File f = stateFile(c);
        if (f.exists()) {
            try (BufferedReader r = new BufferedReader(new InputStreamReader(new FileInputStream(f), StandardCharsets.UTF_8))) {
                for (String line; (line = r.readLine()) != null; ) {
                    if (line.startsWith("manifest ")) s.manifest = Long.parseUnsignedLong(line.substring(9));
                    else if (line.startsWith("apk ")) s.apk = Long.parseUnsignedLong(line.substring(4));
                    else if (line.equals("complete")) s.complete = true;
                    else if (line.length() > 41) s.files.put(line.substring(41), line.substring(0, 40));
                }
            } catch (Exception e) {
                Log.w(TAG, "state unreadable: " + e);
            }
        }
        return s;
    }

    private static synchronized void saveState(Context c, State s) {
        File f = stateFile(c), tmp = new File(f.getPath() + ".tmp");
        f.getParentFile().mkdirs();
        try (Writer w = new OutputStreamWriter(new BufferedOutputStream(new FileOutputStream(tmp)), StandardCharsets.UTF_8)) {
            w.write("manifest " + Long.toUnsignedString(s.manifest) + "\n");
            w.write("apk " + Long.toUnsignedString(s.apk) + "\n");
            if (s.complete) w.write("complete\n");
            synchronized (s.files) {
                for (Map.Entry<String, String> e : s.files.entrySet()) w.write(e.getValue() + " " + e.getKey() + "\n");
            }
        } catch (IOException e) {
            Log.w(TAG, "state not saved: " + e);
            return;
        }
        if (!tmp.renameTo(f)) Log.w(TAG, "state rename failed");
    }

    // Is the game playable as installed? A complete install of a manifest the APK accepts, or bundles
    // pushed over adb by the developer (no state file, but every bundle present).
    public static boolean ready(Context c) {
        State s = state(c);
        // A complete install counts only for the APK it was made for: a new APK (new game version)
        // needs its bundles brought up to date first.
        if (s.complete) return s.apk == port(c).manifest;
        if (s.manifest == 0 && s.files.isEmpty()) {
            String[] names = dir(c).list();
            int n = 0;
            if (names != null) for (String x : names) if (x.endsWith(".bundle")) n++;
            return n >= port(c).bundleCount && n > 0;
        }
        return false;
    }

    public static long freeBytes(Context c) {
        return new StatFs(c.getFilesDir().getPath()).getAvailableBytes();
    }

    // ---- sources ----
    public static final class Entry {
        final String name;   // bundle file name
        final long size;
        String sha;          // hex sha1 of the Windows original, or null when unknown (local test source)
        final Object ref;

        Entry(String name, long size, String sha, Object ref) {
            this.name = name;
            this.size = size;
            this.sha = sha;
            this.ref = ref;
        }
    }

    public interface Listener {
        void status(String s);

        void progress(long done, long total, int files, int filesTotal);
    }

    interface Source extends AutoCloseable {
        List<Entry> bundles(long manifest) throws Exception;

        // Every other file of the manifest (path -> sha1), to compare with what the APK carries.
        Map<String, String> others(long manifest) throws Exception;

        void fetch(Entry e, File dst, java.util.function.LongConsumer bytes, AtomicBoolean cancel) throws Exception;

        @Override
        void close();
    }

    static final class SteamSource implements Source {
        final SteamSession session;
        final SteamDepot depot;
        final Port port;
        private final Map<Long, DepotManifest> manifests = new HashMap<>();

        SteamSource(Context c, Port port) throws Exception {
            this.port = port;
            TokenStore t = TokenStore.load(c);
            if (t == null) throw new IOException("需要先登录 Steam");
            session = new SteamSession();
            try {
                session.logOn(t.account, t.refreshToken, TokenStore.loginId(c) ^ 0x20000);
            } catch (Exception e) {
                session.close();
                throw e;
            }
            depot = new SteamDepot(session, port.app, port.depot);
        }

        long publicManifest() throws Exception {
            return depot.branchManifest("public");
        }

        private DepotManifest manifest(long gid) throws Exception {
            DepotManifest m = manifests.get(gid);
            if (m == null) {
                m = depot.manifest(gid);
                manifests.put(gid, m);
            }
            return m;
        }

        @Override
        public List<Entry> bundles(long gid) throws Exception {
            List<Entry> out = new ArrayList<>();
            for (FileData f : manifest(gid).getFiles()) {
                String n = f.getFileName();
                if (!n.startsWith(port.prefix) || !n.endsWith(".bundle")) continue;
                out.add(new Entry(n.substring(port.prefix.length()), f.getTotalSize(), hex(f.getFileHash()), f));
            }
            return out;
        }

        @Override
        public Map<String, String> others(long gid) throws Exception {
            Map<String, String> out = new HashMap<>();
            for (FileData f : manifest(gid).getFiles()) {
                String n = f.getFileName();
                if (n.startsWith(port.prefix) || f.getChunks().isEmpty()) continue;
                out.put(n, hex(f.getFileHash()));
            }
            return out;
        }

        @Override
        public void fetch(Entry e, File dst, java.util.function.LongConsumer bytes, AtomicBoolean cancel) throws Exception {
            try (RandomAccessFile r = new RandomAccessFile(dst, "rw")) {
                depot.download((FileData) e.ref, r, bytes::accept, cancel);
            }
        }

        @Override
        public void close() {
            depot.close();
            session.close();
        }
    }

    // Developer test: the Windows bundles copied to a folder on the device stand in for Steam.
    static final class LocalSource implements Source {
        final File dir;
        final Port port;

        LocalSource(File dir, Port port) {
            this.dir = dir;
            this.port = port;
        }

        @Override
        public List<Entry> bundles(long manifest) throws Exception {
            List<Entry> out = new ArrayList<>();
            File[] fs = dir.listFiles();
            if (fs == null) throw new IOException("测试目录不存在：" + dir);
            for (File f : fs) {
                if (!f.getName().endsWith(".bundle")) continue;
                String sha = port.shaders.containsKey(f.getName()) ? sha1(f) : null;
                out.add(new Entry(f.getName(), f.length(), sha, f));
            }
            return out;
        }

        @Override
        public Map<String, String> others(long manifest) {
            return new HashMap<>(port.base);
        }

        @Override
        public void fetch(Entry e, File dst, java.util.function.LongConsumer bytes, AtomicBoolean cancel) throws Exception {
            try (InputStream in = new FileInputStream((File) e.ref); OutputStream out = new FileOutputStream(dst)) {
                byte[] b = new byte[1 << 16];
                for (int r; (r = in.read(b)) > 0; ) {
                    if (cancel.get()) throw new InterruptedException();
                    out.write(b, 0, r);
                    bytes.accept(r);
                }
            }
        }

        @Override
        public void close() { }
    }

    // ---- update check ----
    public static final class Check {
        public long installed, apkBase, latest;
        public boolean upToDate;       // nothing to do
        public boolean needsNewApk;    // Steam's version changed code/data the APK carries
        public int changedBaseFiles;
        public int filesToGet;
        public long bytesToGet;
        public long target;            // what install() should fetch (0 = nothing)
    }

    // Compares the installed bundles with the APK's manifest and Steam's current one.
    public static Check check(Context c, Listener l) throws Exception {
        Port p = port(c);
        State s = state(c);
        Check r = new Check();
        r.installed = s.complete ? s.manifest : 0;
        r.apkBase = p.manifest;
        try (SteamSource src = new SteamSource(c, p)) {
            l.status("正在向 Steam 查询最新版本…");
            r.latest = src.publicManifest();
            long target = p.manifest;
            if (r.latest != p.manifest) {
                Map<String, String> others = src.others(r.latest);
                for (Map.Entry<String, String> e : p.base.entrySet()) {
                    String now = others.get(e.getKey());
                    if (now == null || !now.equals(e.getValue())) r.changedBaseFiles++;
                }
                if (r.changedBaseFiles == 0) target = r.latest;
                else r.needsNewApk = true;
            }
            if (s.complete && s.manifest == target && s.apk == p.manifest) {
                r.upToDate = true;
                return r;
            }
            l.status("正在读取版本清单…");
            Plan plan = plan(c, src.bundles(target), s, p, target);
            r.filesToGet = plan.todo.size();
            r.bytesToGet = plan.downloadBytes;
            r.target = target;
            if (plan.todo.isEmpty()) {
                // Everything is already there (bundles pushed by the developer, or a manifest whose
                // bundles did not change): record it, so later versions only fetch what differs.
                s.manifest = target;
                s.apk = p.manifest;
                s.complete = true;
                saveState(c, s);
                r.upToDate = true;
            }
        }
        return r;
    }

    static final class Plan {
        final List<Entry> all, todo = new ArrayList<>();
        long downloadBytes, totalBytes;

        Plan(List<Entry> all) {
            this.all = all;
        }
    }

    private static Plan plan(Context c, List<Entry> bundles, State s, Port p, long target) {
        Plan plan = new Plan(bundles);
        File d = dir(c);
        boolean adopt = s.manifest == 0 && s.files.isEmpty() && target == p.manifest;
        for (Entry e : bundles) {
            File f = new File(d, e.name);
            String have = s.files.get(e.name);
            if (adopt && f.exists()) {
                // Bundles pushed by the developer for this exact manifest: take them as installed.
                if (e.sha != null) s.files.put(e.name, e.sha);
                continue;
            }
            if (have != null && f.exists() && (e.sha == null || have.equals(e.sha))) continue;
            plan.todo.add(e);
            plan.totalBytes += e.size;
            if (!(e.sha != null && e.sha.equals(p.shaders.get(e.name)))) plan.downloadBytes += e.size;
        }
        return plan;
    }

    // ---- install ----
    public static void install(Context c, long target, File localTest, Listener l, AtomicBoolean cancel) throws Exception {
        Port p = port(c);
        Source src = localTest != null ? new LocalSource(localTest, p) : new SteamSource(c, p);
        try {
            install(c, p, src, target, l, cancel);
        } finally {
            src.close();
        }
    }

    private static void install(Context c, Port p, Source src, long target, Listener l, AtomicBoolean cancel) throws Exception {
        File d = dir(c), tmpDir = new File(d, ".dl");
        d.mkdirs();
        tmpDir.mkdirs();
        State s = state(c);
        l.status("正在读取版本清单…");
        List<Entry> bundles = src.bundles(target);
        if (s.manifest != target) {
            s.manifest = target;
            s.complete = false;
            saveState(c, s);
        }
        Plan plan = plan(c, bundles, s, p, target);
        long need = plan.totalBytes + plan.totalBytes / 20 + (300L << 20);
        long free = freeBytes(c);
        if (free < need) throw new IOException(String.format("存储空间不够：需要约 %.1f GB，现在只剩 %.1f GB", need / 1e9, free / 1e9));

        AtomicLong done = new AtomicLong();
        AtomicInteger files = new AtomicInteger();
        int filesTotal = plan.todo.size();
        long total = plan.totalBytes;
        l.status(filesTotal == 0 ? "正在整理…" : "正在下载游戏数据");
        AtomicLong lastSave = new AtomicLong(System.currentTimeMillis());
        AtomicBoolean failed = new AtomicBoolean();
        final Exception[] firstError = new Exception[1];
        ExecutorService pool = Executors.newFixedThreadPool(8, r -> {
            Thread t = new Thread(r, "PrincessData-dl");
            t.setDaemon(true);
            return t;
        });
        List<Future<?>> jobs = new ArrayList<>();
        AssetManager assets = c.getAssets();
        for (Entry e : plan.todo) {
            jobs.add(pool.submit(() -> {
                if (cancel.get() || failed.get()) return;
                File out = new File(d, e.name), part = new File(d, e.name + ".part");
                AtomicLong mine = new AtomicLong();
                try {
                    String packed = p.shaders.get(e.name);
                    if (e.sha == null && packed != null && e.ref instanceof File) e.sha = sha1((File) e.ref);
                    if (packed != null && packed.equals(e.sha)) {
                        try (InputStream in = assets.open("port/shaders/" + e.name); OutputStream o = new FileOutputStream(part)) {
                            byte[] b = new byte[1 << 16];
                            for (int r; (r = in.read(b)) > 0; ) o.write(b, 0, r);
                        }
                        done.addAndGet(e.size);
                    } else {
                        if (packed != null) Log.w(TAG, e.name + " changed on Steam; its shaders stay Windows-only until a new APK");
                        File raw = new File(tmpDir, e.name);
                        src.fetch(e, raw, delta -> {
                            done.addAndGet(delta);
                            mine.addAndGet(delta);
                        }, cancel);
                        try (InputStream in = new BufferedInputStream(new FileInputStream(raw), 1 << 16);
                             OutputStream o = new BufferedOutputStream(new FileOutputStream(part), 1 << 16)) {
                            BundleStamp.Result r = BundleStamp.convert(in, o);
                            if (r.patched != r.serialized) Log.w(TAG, e.name + ": " + r.patched + "/" + r.serialized + " serialized files retargeted");
                        }
                        raw.delete();
                    }
                    if (out.exists() && !out.delete()) throw new IOException("无法替换 " + e.name);
                    if (!part.renameTo(out)) throw new IOException("无法写入 " + e.name);
                    synchronized (s.files) {
                        s.files.put(e.name, e.sha != null ? e.sha : "0000000000000000000000000000000000000000");
                    }
                    int n = files.incrementAndGet();
                    l.progress(done.get(), total, n, filesTotal);
                    long now = System.currentTimeMillis();
                    if (now - lastSave.get() > 5000) {
                        lastSave.set(now);
                        saveState(c, s);
                    }
                } catch (Exception ex) {
                    part.delete();
                    done.addAndGet(-mine.get());
                    if (cancel.get()) return;
                    if (failed.compareAndSet(false, true)) firstError[0] = ex;
                    Log.w(TAG, "failed " + e.name + ": " + ex);
                }
            }));
        }
        pool.shutdown();
        while (!pool.isTerminated()) {
            l.progress(done.get(), total, files.get(), filesTotal);
            pool.awaitTermination(500, java.util.concurrent.TimeUnit.MILLISECONDS);
        }
        saveState(c, s);
        if (cancel.get()) throw new InterruptedException("已暂停，下次会从这里继续");
        if (failed.get()) throw new IOException("下载没有完成：" + SteamDepot.rootMessage(firstError[0]) + "\n已下载的部分会保留，重试会接着下载。", firstError[0]);

        // Remove bundles the manifest no longer has, and leftovers.
        l.status("正在整理…");
        Set<String> keep = new HashSet<>();
        for (Entry e : bundles) keep.add(e.name);
        String[] names = d.list();
        if (names != null)
            for (String n : names)
                if ((n.endsWith(".bundle") && !keep.contains(n)) || n.endsWith(".part")) new File(d, n).delete();
        synchronized (s.files) {
            s.files.keySet().retainAll(keep);
        }
        deleteTree(tmpDir);
        s.manifest = target;
        s.apk = p.manifest;
        s.complete = true;
        saveState(c, s);
        // The shader bundles just copied are this APK's.
        writeShaderMark(c, p);
        l.progress(total, total, filesTotal, filesTotal);
    }

    // ---- shader bundles of a newer APK ----
    // A new APK can carry better converted shader bundles for the same Windows originals; the install
    // state cannot see that (it keys bundles by the original's SHA-1). port.txt "shaderset" names the
    // set the APK ships, files/aa/.princess-shaders the set on the device.
    private static File shaderMark(Context c) {
        return new File(dir(c), ".princess-shaders");
    }

    private static void writeShaderMark(Context c, Port p) {
        if (p.shaderSet == null) return;
        try (Writer w = new OutputStreamWriter(new FileOutputStream(shaderMark(c)), StandardCharsets.UTF_8)) {
            w.write(p.shaderSet + "\n");
        } catch (IOException e) {
            Log.w(TAG, "shader mark not saved: " + e);
        }
    }

    public static boolean shadersStale(Context c) {
        String want = port(c).shaderSet;
        if (want == null) return false;
        try (BufferedReader r = new BufferedReader(new InputStreamReader(new FileInputStream(shaderMark(c)), StandardCharsets.UTF_8))) {
            return !want.equals(r.readLine());
        } catch (IOException e) {
            return true;
        }
    }

    // Copies the APK's shader bundles over the installed ones. A bundle not installed yet is left to
    // the normal install; one Steam has since replaced keeps the Steam version.
    public static int refreshShaders(Context c) throws IOException {
        Port p = port(c);
        State s = state(c);
        File d = dir(c);
        AssetManager assets = c.getAssets();
        byte[] b = new byte[1 << 16];
        int n = 0;
        for (Map.Entry<String, String> e : p.shaders.entrySet()) {
            File out = new File(d, e.getKey());
            if (!out.exists()) continue;
            String installed = s.files.get(e.getKey());
            if (installed != null && !installed.equals(e.getValue())) continue;
            File part = new File(d, e.getKey() + ".part");
            try (InputStream in = assets.open("port/shaders/" + e.getKey()); OutputStream o = new FileOutputStream(part)) {
                for (int r; (r = in.read(b)) > 0; ) o.write(b, 0, r);
            }
            if (!part.renameTo(out)) {
                out.delete();
                if (!part.renameTo(out)) throw new IOException("无法替换 " + e.getKey());
            }
            n++;
        }
        writeShaderMark(c, p);
        Log.i(TAG, "shader bundles refreshed from the APK: " + n);
        return n;
    }

    // ---- helpers ----
    static String hex(byte[] b) {
        if (b == null) return null;
        StringBuilder sb = new StringBuilder(b.length * 2);
        for (byte x : b) sb.append(String.format("%02x", x & 0xFF));
        return sb.toString();
    }

    static String sha1(File f) throws Exception {
        MessageDigest md = MessageDigest.getInstance("SHA-1");
        try (InputStream in = new FileInputStream(f)) {
            byte[] b = new byte[1 << 16];
            for (int r; (r = in.read(b)) > 0; ) md.update(b, 0, r);
        }
        return hex(md.digest());
    }

    private static void deleteTree(File f) {
        File[] kids = f.listFiles();
        if (kids != null) for (File k : kids) deleteTree(k);
        f.delete();
    }

    private GameData() { }
}
