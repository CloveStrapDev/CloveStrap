// Small build helper: cut out a plain white background, trim, pad to square, and build a multi-size .ico.
// ArtTool cutout <in.png> <out.png> [square]
// ArtTool ico <in.png> <out.ico>
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

static class ArtTool
{
    static int Thr = 228;
    static int Main(string[] a)
    {
        if (a.Length >= 3 && a[0] == "cutout") { if (a.Length > 4) Thr = int.Parse(a[4]); Cutout(a[1], a[2], a.Length > 3 && a[3] == "square"); return 0; }
        if (a.Length >= 3 && a[0] == "ico") { Ico(a[1], a[2]); return 0; }
        Console.WriteLine("usage: cutout <in> <out> [square] | ico <in> <out>");
        return 1;
    }

    static int[] Load(Bitmap b)
    {
        var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var px = new int[b.Width * b.Height];
        for (int y = 0; y < b.Height; y++) Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), px, y * b.Width, b.Width);
        b.UnlockBits(d);
        return px;
    }

    static Bitmap Save(int[] px, int w, int h)
    {
        var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var d = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < h; y++) Marshal.Copy(px, y * w, new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), w);
        b.UnlockBits(d);
        return b;
    }

    static bool Whitish(int c, int thr)
    {
        int r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
        int mn = Math.Min(r, Math.Min(g, b)), mx = Math.Max(r, Math.Max(g, b));
        return mn >= thr && mx - mn <= 30;
    }

    static void Cutout(string inFile, string outFile, bool square)
    {
        Bitmap src;
        using (var fs = File.OpenRead(inFile)) using (var tmp = Image.FromStream(fs)) src = new Bitmap(tmp);
        int w = src.Width, h = src.Height;
        var px = Load(src);
        var gone = new bool[w * h];
        var q = new Queue<int>();
        Action<int> seed = i => { if (!gone[i] && Whitish(px[i], Thr)) { gone[i] = true; q.Enqueue(i); } };
        for (int x = 0; x < w; x++) { seed(x); seed((h - 1) * w + x); }
        for (int y = 0; y < h; y++) { seed(y * w); seed(y * w + w - 1); }
        while (q.Count > 0)
        {
            int i = q.Dequeue(), x = i % w, y = i / w;
            if (x > 0) seed(i - 1); if (x < w - 1) seed(i + 1);
            if (y > 0) seed(i - w); if (y < h - 1) seed(i + w);
        }
        // soft edge: light pixels touching the removed area fade out by how white they are
        var outPx = new int[w * h];
        for (int i = 0; i < px.Length; i++)
        {
            if (gone[i]) { outPx[i] = 0; continue; }
            int x = i % w, y = i / w;
            bool edge = (x > 0 && gone[i - 1]) || (x < w - 1 && gone[i + 1]) || (y > 0 && gone[i - w]) || (y < h - 1 && gone[i + w]);
            int c = px[i];
            int alpha = 255;
            if (edge)
            {
                int r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
                int mn = Math.Min(r, Math.Min(g, b));
                alpha = Math.Max(60, Math.Min(255, (int)((255 - mn) * 255.0 / 70)));
            }
            alpha = Math.Min(alpha, (c >> 24) & 255);   // keep any transparency the picture already has
            outPx[i] = (alpha << 24) | (c & 0xFFFFFF);
        }
        // trim to content
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int i = 0; i < outPx.Length; i++)
            if (((outPx[i] >> 24) & 255) > 16) { int x = i % w, y = i / w; if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
        using (var full = Save(outPx, w, h))
        {
            int cw = maxX - minX + 1, ch = maxY - minY + 1;
            int outW = cw, outH = ch, ox = 0, oy = 0;
            if (square) { int s = (int)(Math.Max(cw, ch) * 1.04); outW = outH = s; ox = (s - cw) / 2; oy = (s - ch) / 2; }
            using (var res = new Bitmap(outW, outH, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(res))
                {
                    g.Clear(Color.Transparent);
                    g.DrawImage(full, new Rectangle(ox, oy, cw, ch), new Rectangle(minX, minY, cw, ch), GraphicsUnit.Pixel);
                }
                res.Save(outFile, ImageFormat.Png);
                Console.WriteLine("cutout " + Path.GetFileName(outFile) + " " + outW + "x" + outH);
            }
        }
    }

    static void Ico(string inFile, string outFile)
    {
        Bitmap src;
        using (var fs = File.OpenRead(inFile)) using (var tmp = Image.FromStream(fs)) src = new Bitmap(tmp);
        int[] sizes = { 256, 128, 64, 48, 40, 32, 24, 20, 16 };
        var pngs = new List<byte[]>();
        foreach (int s in sizes)
        {
            using (var b = new Bitmap(s, s, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(b))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    float sc = Math.Min((float)s / src.Width, (float)s / src.Height);
                    float dw = src.Width * sc, dh = src.Height * sc;
                    g.DrawImage(src, (s - dw) / 2, (s - dh) / 2, dw, dh);
                }
                using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); pngs.Add(ms.ToArray()); }
                if (s == 32 || s == 16) b.Save(Path.Combine(Path.GetDirectoryName(outFile), "icon_" + s + ".png"), ImageFormat.Png);
            }
        }
        using (var fs = new FileStream(outFile, FileMode.Create))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                w.Write(pngs[i].Length); w.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (var p in pngs) w.Write(p);
        }
        Console.WriteLine("ico " + Path.GetFileName(outFile) + " " + sizes.Length + " sizes");
    }
}
