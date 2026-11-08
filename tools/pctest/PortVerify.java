import in.dragonbra.javasteam.types.DepotManifest;
import in.dragonbra.javasteam.types.FileData;
import java.nio.file.*;
import java.util.*;

// Checks port.txt against a depot manifest (from the Steam client's depotcache): every "base" file
// must be in the manifest with the same SHA-1, every "shader" bundle too, and the bundle count must
// match. With --fix, base lines missing from the manifest are dropped (they were never Steam files).
// usage: PortVerify <port.txt> <manifest> [--fix]
public class PortVerify {
    public static void main(String[] a) throws Exception {
        DepotManifest m = DepotManifest.loadFromFile(a[1]);
        Map<String, String> hashes = new HashMap<>();
        for (FileData f : m.getFiles()) hashes.put(f.getFileName(), hex(f.getFileHash()));
        List<String> lines = Files.readAllLines(Paths.get(a[0]));
        List<String> out = new ArrayList<>();
        String prefix = null;
        int bad = 0, dropped = 0, ok = 0, bundles = 0;
        for (String l : lines) {
            if (l.startsWith("prefix ")) prefix = l.substring(7);
            if (l.startsWith("base ")) {
                String sha = l.substring(5, 45), path = l.substring(46);
                String h = hashes.get(path);
                if (h == null) { dropped++; System.out.println("not in depot: " + path); continue; }
                if (!h.equals(sha)) { bad++; System.out.println("DIFFERENT: " + path); }
                else ok++;
            } else if (l.startsWith("shader ")) {
                String sha = l.substring(7, 47), name = l.substring(48);
                String h = hashes.get(prefix + name);
                if (h == null || !h.equals(sha)) { bad++; System.out.println("SHADER MISMATCH: " + name); }
            }
            out.add(l);
        }
        for (String k : hashes.keySet()) if (prefix != null && k.startsWith(prefix) && k.endsWith(".bundle")) bundles++;
        System.out.println("manifest " + m.getManifestGID() + ": base ok " + ok + ", different " + bad + ", not in depot " + dropped + ", bundles in depot " + bundles);
        if (a.length > 2 && a[2].equals("--fix")) Files.write(Paths.get(a[0]), out);
        if (bad > 0) System.exit(1);
    }

    static String hex(byte[] b) {
        StringBuilder sb = new StringBuilder();
        for (byte x : b) sb.append(String.format("%02x", x & 0xFF));
        return sb.toString();
    }
}
