package dev.kmprincess.cloud;

import in.dragonbra.javasteam.types.DepotManifest;
import in.dragonbra.javasteam.types.FileData;
import java.io.File;
import java.io.RandomAccessFile;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicLong;

// PC test of the SteamDepot download path with an anonymous account and a free depot.
// usage: DepotTest <app> <depot> <out dir> [max files]
public class DepotTest {
    public static void main(String[] a) throws Exception {
        int app = Integer.parseInt(a[0]), depot = Integer.parseInt(a[1]);
        File out = new File(a[2]);
        int max = a.length > 3 ? Integer.parseInt(a[3]) : 5;
        out.mkdirs();
        try (SteamSession s = new SteamSession()) {
            s.logOnAnonymous();
            System.out.println("anonymous logon ok");
            try (SteamDepot d = new SteamDepot(s, app, depot)) {
                long gid = d.branchManifest("public");
                System.out.println("public manifest " + gid);
                DepotManifest m = d.manifest(gid);
                System.out.println("manifest files " + m.getFiles().size() + ", " + m.getTotalUncompressedSize() + " bytes");
                int n = 0;
                AtomicLong bytes = new AtomicLong();
                long t0 = System.currentTimeMillis();
                for (FileData f : m.getFiles()) {
                    if (f.getChunks().isEmpty()) continue;
                    if (n++ >= max) break;
                    File dst = new File(out, f.getFileName().replace('\\', '_').replace('/', '_'));
                    try (RandomAccessFile r = new RandomAccessFile(dst, "rw")) {
                        d.download(f, r, bytes::addAndGet, new AtomicBoolean());
                    }
                    System.out.println("  ok " + f.getFileName() + " " + f.getTotalSize() + " (sha1 verified)");
                }
                System.out.printf("%d bytes in %d ms%n", bytes.get(), System.currentTimeMillis() - t0);
            }
        }
        System.exit(0);
    }
}
