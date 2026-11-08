package dev.kmprincess.cloud;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.DataInputStream;
import java.io.DataOutputStream;
import java.io.EOFException;
import java.io.FilterOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;

// Turns a Windows (StandaloneWindows64) UnityFS AssetBundle into one the Android player accepts.
// Unity refuses bundles whose serialized files name another build target, and for bundles without
// shaders that 4-byte target-platform field (19 -> 13), right after the Unity version string in each
// serialized file's metadata, is the only difference that matters.
//
// The data blocks are LZ4 chunks (128 KB each), so only the block(s) holding a serialized file's
// header are decompressed, patched and stored uncompressed; every other block is copied byte for
// byte. The block table is written uncompressed. A 6 GB set converts at disk speed on a phone.
public final class BundleStamp {
    public static final int WINDOWS64 = 19, ANDROID = 13;

    public static final class Result {
        public int serialized;  // serialized files in the bundle
        public int patched;     // of those, platform field now says Android
        public long bytesOut;
    }

    private static final class Block { int usize, csize; short flags; boolean patch; }
    private static final class Node { long offset, size; int flags; String path; }

    // Reads a whole bundle from `raw` and writes the converted bundle to `rawOut`.
    public static Result convert(InputStream raw, OutputStream rawOut) throws IOException {
        DataInputStream in = new DataInputStream(raw);
        Counting co = new Counting(rawOut);
        DataOutputStream out = new DataOutputStream(co);
        long pos = 0;

        byte[] sig = cstring(in);
        pos += sig.length + 1;
        if (!"UnityFS".equals(new String(sig, StandardCharsets.US_ASCII))) throw new IOException("不是 UnityFS 包");
        int version = in.readInt();
        pos += 4;
        byte[] player = cstring(in);
        pos += player.length + 1;
        byte[] engine = cstring(in);
        pos += engine.length + 1;
        in.readLong(); // total size
        int ciSize = in.readInt(), uiSize = in.readInt(), flags = in.readInt();
        pos += 20;
        if (version < 6) throw new IOException("不支持的 UnityFS 版本 " + version);
        if ((flags & 0x80) != 0) throw new IOException("块表在文件末尾，暂不支持");
        if (version >= 7) pos += skip(in, align16(pos) - pos);
        byte[] ci = new byte[ciSize];
        in.readFully(ci);
        pos += ciSize;
        byte[] info = decompress(flags & 0x3F, ci, uiSize);
        if ((flags & 0x200) != 0) pos += skip(in, align16(pos) - pos);

        DataInputStream bi = new DataInputStream(new ByteArrayInputStream(info));
        byte[] hash = new byte[16];
        bi.readFully(hash);
        int nb = bi.readInt();
        List<Block> blocks = new ArrayList<>();
        for (int i = 0; i < nb; i++) {
            Block b = new Block();
            b.usize = bi.readInt();
            b.csize = bi.readInt();
            b.flags = bi.readShort();
            blocks.add(b);
        }
        int nn = bi.readInt();
        List<Node> nodes = new ArrayList<>();
        for (int i = 0; i < nn; i++) {
            Node n = new Node();
            n.offset = bi.readLong();
            n.size = bi.readLong();
            n.flags = bi.readInt();
            n.path = new String(cstring(bi), StandardCharsets.UTF_8);
            nodes.add(n);
        }

        // Every block that overlaps the first 512 bytes of a serialized file (node flag 4) is patched.
        List<long[]> heads = new ArrayList<>();
        for (Node n : nodes) if ((n.flags & 4) != 0) heads.add(new long[] { n.offset, Math.min(n.size, 512) });
        long u = 0;
        for (Block b : blocks) {
            for (long[] h : heads) if (h[0] < u + b.usize && h[0] + h[1] > u) b.patch = true;
            u += b.usize;
        }

        ByteArrayOutputStream nbOut = new ByteArrayOutputStream(info.length);
        DataOutputStream nbi = new DataOutputStream(nbOut);
        nbi.write(hash);
        nbi.writeInt(nb);
        long dataSize = 0;
        for (Block b : blocks) {
            nbi.writeInt(b.usize);
            int cs = b.patch ? b.usize : b.csize;
            nbi.writeInt(cs);
            nbi.writeShort(b.patch ? (b.flags & ~0x3F) : b.flags);
            dataSize += cs;
        }
        nbi.writeInt(nn);
        for (Node n : nodes) {
            nbi.writeLong(n.offset);
            nbi.writeLong(n.size);
            nbi.writeInt(n.flags);
            nbi.write(n.path.getBytes(StandardCharsets.UTF_8));
            nbi.write(0);
        }
        byte[] newInfo = nbOut.toByteArray();

        // Same header layout; the block table is stored raw.
        out.write(sig); out.write(0);
        out.writeInt(version);
        out.write(player); out.write(0);
        out.write(engine); out.write(0);
        long sizeField = sig.length + 1 + 4 + player.length + 1 + engine.length + 1 + 8 + 12;
        long infoAt = version >= 7 ? align16(sizeField) : sizeField;
        long dataAt = infoAt + newInfo.length;
        if ((flags & 0x200) != 0) dataAt = align16(dataAt);
        out.writeLong(dataAt + dataSize);
        out.writeInt(newInfo.length);
        out.writeInt(newInfo.length);
        out.writeInt(flags & ~0x3F);
        for (long p = sizeField; p < infoAt; p++) out.write(0);
        out.write(newInfo);
        for (long p = infoAt + newInfo.length; p < dataAt; p++) out.write(0);

        Result r = new Result();
        r.serialized = heads.size();
        long uoff = 0;
        byte[] buf = new byte[1 << 17];
        for (Block b : blocks) {
            if (!b.patch) {
                copy(in, out, b.csize, buf);
            } else {
                byte[] c = new byte[b.csize];
                in.readFully(c);
                byte[] d = decompress(b.flags & 0x3F, c, b.usize);
                for (long[] h : heads) {
                    long rel = h[0] - uoff;
                    if (rel >= 0 && rel < d.length) r.patched += stamp(d, (int) rel, engine);
                }
                out.write(d);
            }
            uoff += b.usize;
        }
        out.flush();
        r.bytesOut = co.n;
        return r;
    }

    // In a serialized file header (version 22+), the metadata starts with the Unity version string
    // followed by the target platform as a little-endian uint32.
    private static int stamp(byte[] d, int start, byte[] engine) {
        int end = Math.min(d.length - engine.length - 5, start + 256);
        outer:
        for (int i = start; i <= end; i++) {
            for (int k = 0; k < engine.length; k++) if (d[i + k] != engine[k]) continue outer;
            int p = i + engine.length;
            if (d[p] != 0) continue;
            p++;
            int v = (d[p] & 0xFF) | (d[p + 1] & 0xFF) << 8 | (d[p + 2] & 0xFF) << 16 | (d[p + 3] & 0xFF) << 24;
            if (v == ANDROID) return 1;
            if (v != WINDOWS64) return 0;
            d[p] = ANDROID;
            d[p + 1] = 0;
            d[p + 2] = 0;
            d[p + 3] = 0;
            return 1;
        }
        return 0;
    }

    static byte[] decompress(int type, byte[] c, int usize) throws IOException {
        if (type == 0) return c;
        if (type == 2 || type == 3) {
            byte[] d = new byte[usize];
            int n = lz4(c, 0, c.length, d, 0, usize);
            if (n != usize) throw new IOException("LZ4 解压长度不对");
            return d;
        }
        throw new IOException("不支持的压缩类型 " + type);
    }

    // Plain LZ4 block decoder.
    static int lz4(byte[] src, int sOff, int sLen, byte[] dst, int dOff, int dLen) throws IOException {
        int s = sOff, sEnd = sOff + sLen, d = dOff, dEnd = dOff + dLen;
        try {
            while (s < sEnd) {
                int token = src[s++] & 0xFF;
                int lit = token >>> 4;
                if (lit == 15) {
                    int b;
                    do { b = src[s++] & 0xFF; lit += b; } while (b == 255);
                }
                System.arraycopy(src, s, dst, d, lit);
                s += lit;
                d += lit;
                if (s >= sEnd) break;
                int off = (src[s] & 0xFF) | (src[s + 1] & 0xFF) << 8;
                s += 2;
                int ml = token & 15;
                if (ml == 15) {
                    int b;
                    do { b = src[s++] & 0xFF; ml += b; } while (b == 255);
                }
                ml += 4;
                int m = d - off;
                if (off == 0 || m < dOff || d + ml > dEnd) throw new IOException("LZ4 数据损坏");
                if (off >= ml) {
                    System.arraycopy(dst, m, dst, d, ml);
                    d += ml;
                } else {
                    for (int i = 0; i < ml; i++) dst[d++] = dst[m++];
                }
            }
        } catch (ArrayIndexOutOfBoundsException e) {
            throw new IOException("LZ4 数据损坏");
        }
        return d - dOff;
    }

    private static long align16(long p) {
        return (p + 15) & ~15L;
    }

    private static long skip(DataInputStream in, long n) throws IOException {
        for (long k = 0; k < n; k++) in.readByte();
        return n;
    }

    private static void copy(InputStream in, OutputStream out, long n, byte[] buf) throws IOException {
        while (n > 0) {
            int r = in.read(buf, 0, (int) Math.min(buf.length, n));
            if (r < 0) throw new EOFException();
            out.write(buf, 0, r);
            n -= r;
        }
    }

    private static byte[] cstring(DataInputStream in) throws IOException {
        ByteArrayOutputStream b = new ByteArrayOutputStream();
        for (int c; (c = in.read()) != 0; ) {
            if (c < 0) throw new EOFException();
            b.write(c);
        }
        return b.toByteArray();
    }

    private static final class Counting extends FilterOutputStream {
        long n;

        Counting(OutputStream o) {
            super(o);
        }

        @Override
        public void write(int b) throws IOException {
            out.write(b);
            n++;
        }

        @Override
        public void write(byte[] b, int off, int len) throws IOException {
            out.write(b, off, len);
            n += len;
        }
    }
}
