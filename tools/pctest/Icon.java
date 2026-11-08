import javax.imageio.ImageIO;
import java.awt.*;
import java.awt.geom.*;
import java.awt.image.BufferedImage;
import java.io.*;
import java.nio.*;
import java.nio.file.*;

// Android launcher icons from the game's own 256x256 Windows icon (.rsrc/ICON/9.ico, extracted by 7-Zip):
//   Icon <ico> <res dir>         mipmap-*/app_icon.png (legacy, rounded square) and the adaptive icon's
//                                 foreground/background layers (108 dp, artwork filling the full layer)
//   Icon <ico> --png <out.png>   just the decoded 256x256 image
public class Icon {
    static BufferedImage decode(byte[] f) {
        ByteBuffer b = ByteBuffer.wrap(f).order(ByteOrder.LITTLE_ENDIAN);
        int off = b.getInt(18);
        if ((f[off] & 0xff) == 0x89) {
            try { return ImageIO.read(new ByteArrayInputStream(f, off, f.length - off)); } catch (IOException e) { throw new UncheckedIOException(e); }
        }
        int w = b.getInt(off + 4), h = b.getInt(off + 8) / 2, bpp = b.getShort(off + 14);
        if (bpp != 32) throw new IllegalArgumentException("bpp " + bpp);
        BufferedImage img = new BufferedImage(w, h, BufferedImage.TYPE_INT_ARGB);
        int p = off + b.getInt(off);
        for (int y = h - 1; y >= 0; y--)
            for (int x = 0; x < w; x++, p += 4) {
                int bl = f[p] & 0xff, g = f[p + 1] & 0xff, r = f[p + 2] & 0xff, a = f[p + 3] & 0xff;
                img.setRGB(x, y, a << 24 | r << 16 | g << 8 | bl);
            }
        return img;
    }

    static BufferedImage scale(BufferedImage src, int size, double fill, Shape clip) {
        BufferedImage d = new BufferedImage(size, size, BufferedImage.TYPE_INT_ARGB);
        Graphics2D g = d.createGraphics();
        g.setRenderingHint(RenderingHints.KEY_INTERPOLATION, RenderingHints.VALUE_INTERPOLATION_BICUBIC);
        g.setRenderingHint(RenderingHints.KEY_RENDERING, RenderingHints.VALUE_RENDER_QUALITY);
        g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
        if (clip != null) g.setClip(clip);
        double s = size * fill, o = (size - s) / 2;
        // Downscale in halving steps: one bicubic pass from 256 to 48 aliases.
        BufferedImage cur = src;
        while (cur.getWidth() / 2 >= s) {
            BufferedImage h = new BufferedImage(cur.getWidth() / 2, cur.getHeight() / 2, BufferedImage.TYPE_INT_ARGB);
            Graphics2D hg = h.createGraphics();
            hg.setRenderingHint(RenderingHints.KEY_INTERPOLATION, RenderingHints.VALUE_INTERPOLATION_BILINEAR);
            hg.drawImage(cur, 0, 0, h.getWidth(), h.getHeight(), null);
            hg.dispose();
            cur = h;
        }
        g.drawImage(cur, (int) Math.round(o), (int) Math.round(o), (int) Math.round(s), (int) Math.round(s), null);
        g.dispose();
        return d;
    }

    public static void main(String[] a) throws Exception {
        BufferedImage src = decode(Files.readAllBytes(Paths.get(a[0])));
        if (a[1].equals("--png")) { ImageIO.write(src, "png", new File(a[2])); return; }
        File res = new File(a[1]);
        String[] dens = {"mdpi", "hdpi", "xhdpi", "xxhdpi", "xxxhdpi"};
        double[] mul = {1, 1.5, 2, 3, 4};
        for (int i = 0; i < dens.length; i++) {
            File dir = new File(res, "mipmap-" + dens[i]);
            dir.mkdirs();
            int legacy = (int) Math.round(48 * mul[i]);
            // Legacy launchers: the artwork in a rounded square with the usual 1/12 margin.
            double m = legacy / 12.0, side = legacy - 2 * m;
            Shape rr = new RoundRectangle2D.Double(m, m, side, side, side * 0.3, side * 0.3);
            ImageIO.write(scale(src, legacy, 10 / 12.0, rr), "png", new File(dir, "app_icon.png"));
            ImageIO.write(scale(src, legacy, 10 / 12.0, new Ellipse2D.Double(m, m, side, side)), "png", new File(dir, "app_icon_round.png"));
            // Adaptive: 108 dp layers, the launcher masks the middle ~72 dp. The artwork is scaled so its
            // whole square just covers that window (0.70 > 72/108); the background is the artwork itself, blurred out to the edge.
            int ad = (int) Math.round(108 * mul[i]);
            ImageIO.write(scale(src, ad, 0.70, null), "png", new File(dir, "ic_launcher_foreground.png"));
            ImageIO.write(scale(src, ad, 1.6, null), "png", new File(dir, "ic_launcher_background.png"));
        }
    }
}
