package dev.kmprincess.cloud;

import java.io.*;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;

// PC check for BundleStamp: converts every bundle in a folder in memory, then unpacks original and
// result completely and compares them. They may differ only in the 4-byte platform fields.
// usage: StampCheck <bundle dir> [threads] [out dir (optional: write converted bundles)]
public class StampCheck {
    public static void main(String[] a) throws Exception {
        File[] files = new File(a[0]).listFiles((d, n) -> n.endsWith(".bundle"));
        Arrays.sort(files);
        int threads = a.length > 1 ? Integer.parseInt(a[1]) : 8;
        File outDir = a.length > 2 ? new File(a[2]) : null;
        AtomicInteger ok = new AtomicInteger(), bad = new AtomicInteger(), done = new AtomicInteger();
        AtomicLong inBytes = new AtomicLong(), outBytes = new AtomicLong();
        ExecutorService ex = Executors.newFixedThreadPool(threads);
        long t0 = System.currentTimeMillis();
        for (File f : files) ex.submit(() -> {
            try {
                byte[] src = Files.readAllBytes(f.toPath());
                ByteArrayOutputStream bo = new ByteArrayOutputStream(src.length + 4096);
                BundleStamp.Result r = BundleStamp.convert(new ByteArrayInputStream(src), bo);
                byte[] dst = bo.toByteArray();
                byte[] u1 = unpack(src), u2 = unpack(dst);
                int diff = 0;
                if (u1.length != u2.length) diff = -1;
                else for (int i = 0; i < u1.length; i++) if (u1[i] != u2[i]) diff++;
                if (r.patched != r.serialized || diff < 0 || diff > r.serialized || r.serialized == 0) {
                    bad.incrementAndGet();
                    System.out.println("BAD " + f.getName() + " serialized=" + r.serialized + " patched=" + r.patched + " diff=" + diff);
                } else ok.incrementAndGet();
                inBytes.addAndGet(src.length);
                outBytes.addAndGet(dst.length);
                if (outDir != null) Files.write(new File(outDir, f.getName()).toPath(), dst);
            } catch (Throwable t) {
                bad.incrementAndGet();
                System.out.println("ERR " + f.getName() + " " + t);
            }
            int n = done.incrementAndGet();
            if (n % 2000 == 0) System.out.println(n + " / " + files.length);
        });
        ex.shutdown();
        ex.awaitTermination(1, TimeUnit.DAYS);
        System.out.printf("ok %d, bad %d, in %.2f GB, out %.2f GB, %d s%n", ok.get(), bad.get(), inBytes.get() / 1e9, outBytes.get() / 1e9, (System.currentTimeMillis() - t0) / 1000);
    }

    // Full unpack of a UnityFS bundle's data area (all blocks concatenated, uncompressed).
    static byte[] unpack(byte[] b) throws IOException {
        DataInputStream in = new DataInputStream(new ByteArrayInputStream(b));
        int pos = 0;
        pos += cs(in);
        int version = in.readInt(); pos += 4;
        pos += cs(in); pos += cs(in);
        in.readLong();
        int ci = in.readInt(), ui = in.readInt(), flags = in.readInt();
        pos += 20;
        if (version >= 7) { int p2 = (pos + 15) & ~15; in.skipBytes(p2 - pos); pos = p2; }
        byte[] c = new byte[ci]; in.readFully(c); pos += ci;
        byte[] info = BundleStamp.decompress(flags & 0x3F, c, ui);
        if ((flags & 0x200) != 0) { int p2 = (pos + 15) & ~15; in.skipBytes(p2 - pos); pos = p2; }
        DataInputStream bi = new DataInputStream(new ByteArrayInputStream(info));
        bi.skipBytes(16);
        int nb = bi.readInt();
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        int[][] bl = new int[nb][3];
        for (int i = 0; i < nb; i++) { bl[i][0] = bi.readInt(); bl[i][1] = bi.readInt(); bl[i][2] = bi.readShort(); }
        for (int[] x : bl) {
            byte[] cc = new byte[x[1]]; in.readFully(cc);
            out.write(BundleStamp.decompress(x[2] & 0x3F, cc, x[0]));
        }
        return out.toByteArray();
    }

    static int cs(DataInputStream in) throws IOException { int n = 0; while (in.read() != 0) n++; return n + 1; }
}
