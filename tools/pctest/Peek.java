package dev.kmprincess.cloud;
import java.nio.file.*;
// Unpacks a bundle and prints which of the given strings occur in it.
public class Peek {
    public static void main(String[] a) throws Exception {
        byte[] u = StampCheck.unpack(Files.readAllBytes(Paths.get(a[0])));
        String s = new String(u, java.nio.charset.StandardCharsets.ISO_8859_1);
        System.out.println("unpacked " + u.length + " bytes");
        for (int i = 1; i < a.length; i++) System.out.println(a[i] + ": " + (s.indexOf(a[i]) >= 0));
        java.util.regex.Matcher m = java.util.regex.Pattern.compile("[ -~]{12,}").matcher(s.substring(0, Math.min(s.length(), 400000)));
        int n = 0; while (m.find() && n++ < 25) System.out.println("  " + m.group());
    }
}
