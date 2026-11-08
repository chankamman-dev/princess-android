import javax.imageio.ImageIO;
import java.awt.image.BufferedImage;
import java.io.File;

// Mean and max per-channel difference between two same-sized screenshots, and the share of pixels
// that differ by more than 8.
public class ImgDiff {
    public static void main(String[] a) throws Exception {
        BufferedImage x = ImageIO.read(new File(a[0])), y = ImageIO.read(new File(a[1]));
        long sum = 0, n = 0, diff = 0; int max = 0;
        for (int j = 0; j < x.getHeight(); j++)
            for (int i = 0; i < x.getWidth(); i++) {
                int p = x.getRGB(i, j), q = y.getRGB(i, j), d = 0;
                for (int s = 0; s < 24; s += 8) d = Math.max(d, Math.abs((p >> s & 255) - (q >> s & 255)));
                sum += d; n++; max = Math.max(max, d); if (d > 8) diff++;
            }
        System.out.printf("mean %.2f max %d, %.1f%% of pixels differ by >8%n", (double) sum / n, max, 100.0 * diff / n);
    }
}
