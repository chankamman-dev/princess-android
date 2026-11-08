import in.dragonbra.javasteam.types.DepotManifest;
import in.dragonbra.javasteam.types.FileData;
import java.util.*;

// Diffs two Steam depot manifests (e.g. the ones the Steam client caches in depotcache).
// usage: ManifestDiff <old.manifest> <new.manifest>
public class ManifestDiff {
    public static void main(String[] a) throws Exception {
        DepotManifest o = DepotManifest.loadFromFile(a[0]), n = DepotManifest.loadFromFile(a[1]);
        System.out.println("old " + o.getManifestGID() + " " + o.getCreationTime() + " files=" + o.getFiles().size() + " enc=" + o.getFilenamesEncrypted());
        System.out.println("new " + n.getManifestGID() + " " + n.getCreationTime() + " files=" + n.getFiles().size() + " enc=" + n.getFilenamesEncrypted());
        Map<String, FileData> om = new HashMap<>();
        for (FileData f : o.getFiles()) om.put(f.getFileName(), f);
        long changedBytes = 0; int added = 0, changed = 0, removed = 0;
        Map<String, int[]> byDir = new TreeMap<>();
        for (FileData f : n.getFiles()) {
            FileData p = om.remove(f.getFileName());
            String kind = p == null ? "A" : Arrays.equals(p.getFileHash(), f.getFileHash()) ? null : "M";
            if (kind == null) continue;
            if (kind.equals("A")) added++; else changed++;
            changedBytes += f.getTotalSize();
            String name = f.getFileName().replace((char) 92, '/');
            String dir = name.contains("/StreamingAssets/aa/") ? (name.endsWith(".bundle") ? "aa/*.bundle" : name) : name;
            byDir.computeIfAbsent(dir, k -> new int[2])[kind.equals("A") ? 0 : 1]++;
        }
        for (String r : om.keySet()) { removed++; System.out.println("D " + r); }
        for (Map.Entry<String, int[]> e : byDir.entrySet()) System.out.println("  " + e.getKey() + "  added " + e.getValue()[0] + " modified " + e.getValue()[1]);
        System.out.println("added " + added + ", modified " + changed + ", removed " + removed + ", bytes " + changedBytes);
    }
}
