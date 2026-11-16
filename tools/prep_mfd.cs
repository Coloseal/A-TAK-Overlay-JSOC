// One-off helper: turns the pilot-display photo into assets\mfd.png (background and mounting holes cut out,
// enlarged 1.5x and sharpened) and prints the screen rectangle. Build/run is done by tools\prep_mfd.ps1.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

static class Prep
{
    static int W, H;

    static byte[] Read(Bitmap b, out int stride)
    {
        BitmapData d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] p = new byte[d.Stride * b.Height];
        Marshal.Copy(d.Scan0, p, 0, p.Length);
        stride = d.Stride;
        b.UnlockBits(d);
        return p;
    }

    static bool[] Grow(bool[] m, int n)
    {
        bool[] cur = (bool[])m.Clone();
        for (int it = 0; it < n; it++)
        {
            bool[] nx = (bool[])cur.Clone();
            for (int y = 1; y < H - 1; y++)
                for (int x = 1; x < W - 1; x++)
                {
                    int k = y * W + x;
                    if (!cur[k] && (cur[k - 1] || cur[k + 1] || cur[k - W] || cur[k + W])) nx[k] = true;
                }
            cur = nx;
        }
        return cur;
    }

    static void Main(string[] a)
    {
        string inPath = a[0], outPath = a[1], dbgPath = a.Length > 2 ? a[2] : null;
        Bitmap src = new Bitmap(inPath);
        W = src.Width; H = src.Height;
        int stride;
        byte[] px = Read(src, out stride);
        int[] lum = new int[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * stride + x * 4;
                lum[y * W + x] = (px[i] + px[i + 1] + px[i + 2]) / 3;
            }

        // 1) backdrop: bright pixels connected to the image border
        bool[] bg = new bool[W * H];
        Stack<int> st = new Stack<int>();
        Action<int, int> seed = delegate (int x, int y) { int k = y * W + x; if (!bg[k] && lum[k] >= 120) { bg[k] = true; st.Push(k); } };
        for (int x = 0; x < W; x++) { seed(x, 0); seed(x, H - 1); }
        for (int y = 0; y < H; y++) { seed(0, y); seed(W - 1, y); }
        while (st.Count > 0)
        {
            int k = st.Pop(); int x = k % W, y = k / W;
            if (x > 0) seed(x - 1, y); if (x < W - 1) seed(x + 1, y); if (y > 0) seed(x, y - 1); if (y < H - 1) seed(x, y + 1);
        }

        // 2) mounting holes in the corner ears: bright blobs that are not connected to the border
        bool[] holes = new bool[W * H];
        bool[] seen = new bool[W * H];
        int holeCount = 0;
        for (int s = 0; s < W * H; s++)
        {
            if (seen[s] || bg[s] || lum[s] < 110) continue;
            List<int> comp = new List<int>();
            seen[s] = true; st.Push(s);
            int x0 = W, x1 = 0, y0 = H, y1 = 0;
            while (st.Count > 0)
            {
                int k = st.Pop(); comp.Add(k); int x = k % W, y = k / W;
                if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
                int[] nb = new int[] { x > 0 ? k - 1 : -1, x < W - 1 ? k + 1 : -1, y > 0 ? k - W : -1, y < H - 1 ? k + W : -1 };
                foreach (int n in nb) if (n >= 0 && !seen[n] && !bg[n] && lum[n] >= 110) { seen[n] = true; st.Push(n); }
            }
            int bw = x1 - x0 + 1, bh = y1 - y0 + 1, cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
            bool nearSide = cx < 140 || cx > W - 140;
            bool earRow = cy < 100 || cy > H - 90;
            if (comp.Count >= 200 && bw >= 14 && bh >= 14 && bw <= 70 && bh <= 70 && nearSide && earRow)
            {
                holeCount++;
                Console.WriteLine("hole at " + cx + "," + cy + " area " + comp.Count);
                foreach (int k in comp) holes[k] = true;
            }
        }
        holes = Grow(holes, 3);

        bool[] gone = new bool[W * H];
        for (int k = 0; k < gone.Length; k++) gone[k] = bg[k] || holes[k];
        gone = Grow(gone, 2);   // eat the light halo around the cut edge

        // 3) screen rectangle: the flat grey fill, found from the middle of the picture
        int sx = W / 2, sy = H / 2;
        int sr = px[sy * stride + sx * 4 + 2], sg = px[sy * stride + sx * 4 + 1], sb = px[sy * stride + sx * 4];
        bool[] scr = new bool[W * H];
        st.Push(sy * W + sx); scr[sy * W + sx] = true;
        int minx = W, maxx = 0, miny = H, maxy = 0;
        while (st.Count > 0)
        {
            int k = st.Pop(); int x = k % W, y = k / W;
            if (x < minx) minx = x; if (x > maxx) maxx = x; if (y < miny) miny = y; if (y > maxy) maxy = y;
            int[] nb = new int[] { x > 0 ? k - 1 : -1, x < W - 1 ? k + 1 : -1, y > 0 ? k - W : -1, y < H - 1 ? k + W : -1 };
            foreach (int n in nb)
            {
                if (n < 0 || scr[n]) continue;
                int i = (n / W) * stride + (n % W) * 4;
                if (Math.Abs(px[i + 2] - sr) <= 9 && Math.Abs(px[i + 1] - sg) <= 9 && Math.Abs(px[i] - sb) <= 9) { scr[n] = true; st.Push(n); }
            }
        }
        Console.WriteLine("screen leaky bbox (source px): x " + minx + ".." + maxx + "  y " + miny + ".." + maxy);
        {
            int[] rows = new int[H], cols = new int[W];
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (scr[y * W + x]) { rows[y]++; cols[x]++; }
            int rmax = 0, cmax = 0; foreach (int r in rows) rmax = Math.Max(rmax, r); foreach (int c in cols) cmax = Math.Max(cmax, c);
            int ry0 = -1, ry1 = -1, cx0 = -1, cx1 = -1;
            for (int y = 0; y < H; y++) if (rows[y] > rmax * 0.9) { if (ry0 < 0) ry0 = y; ry1 = y; }
            for (int x = 0; x < W; x++) if (cols[x] > cmax * 0.9) { if (cx0 < 0) cx0 = x; cx1 = x; }
            Console.WriteLine("screen solid core (source px): x " + cx0 + ".." + cx1 + "  y " + ry0 + ".." + ry1 + "   size " + (cx1 - cx0 + 1) + "x" + (ry1 - ry0 + 1));
        }

        // 4) enlarge 1.5x (bicubic), sharpen, apply the cut-out as alpha
        int OW = W * 3 / 2, OH = (int)Math.Round(H * 1.5);
        Console.WriteLine("output " + OW + "x" + OH);
        Bitmap big = new Bitmap(OW, OH, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(big))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(src, new Rectangle(0, 0, OW, OH), 0, 0, W, H, GraphicsUnit.Pixel);
        }
        // the cut-out mask, enlarged smoothly (grey bitmap) so its edge follows the picture
        Bitmap mk = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        {
            BitmapData md = mk.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            byte[] mp = new byte[md.Stride * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * md.Stride + x * 4; byte v = gone[y * W + x] ? (byte)0 : (byte)255;
                    mp[i] = v; mp[i + 1] = v; mp[i + 2] = v; mp[i + 3] = 255;
                }
            Marshal.Copy(mp, 0, md.Scan0, mp.Length);
            mk.UnlockBits(md);
        }
        Bitmap mbig = new Bitmap(OW, OH, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(mbig))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(mk, new Rectangle(0, 0, OW, OH), 0, 0, W, H, GraphicsUnit.Pixel);
        }

        int bs, ms;
        byte[] bp = Read(big, out bs);
        byte[] mpx = Read(mbig, out ms);
        byte[] copy = (byte[])bp.Clone();
        // unsharp mask (radius ~1.2px) - makes the lettering and edges crisper after enlarging
        for (int y = 2; y < OH - 2; y++)
            for (int x = 2; x < OW - 2; x++)
            {
                int i = y * bs + x * 4;
                for (int c = 0; c < 3; c++)
                {
                    float sum = 0f, wsum = 0f;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            float w = (float)Math.Exp(-(dx * dx + dy * dy) / 2.2f);
                            sum += copy[i + dy * bs + dx * 4 + c] * w; wsum += w;
                        }
                    float blur = sum / wsum, m = copy[i + c];
                    float v = m + 0.8f * (m - blur);
                    bp[i + c] = (byte)(v < 0f ? 0f : (v > 255f ? 255f : v));
                }
            }
        for (int y = 0; y < OH; y++)
            for (int x = 0; x < OW; x++)
            {
                int i = y * bs + x * 4;
                bp[i + 3] = mpx[y * ms + x * 4] >= 128 ? (byte)255 : (byte)0;
            }
        Bitmap res = new Bitmap(OW, OH, PixelFormat.Format32bppArgb);
        BitmapData rd = res.LockBits(new Rectangle(0, 0, OW, OH), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        byte[] outp = new byte[rd.Stride * OH];
        for (int y = 0; y < OH; y++) Buffer.BlockCopy(bp, y * bs, outp, y * rd.Stride, OW * 4);
        Marshal.Copy(outp, 0, rd.Scan0, outp.Length);
        res.UnlockBits(rd);
        res.Save(outPath, ImageFormat.Png);

        if (dbgPath != null)
        {
            using (Bitmap dbg = new Bitmap(OW, OH, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(dbg))
            {
                g.Clear(Color.FromArgb(255, 0, 170));
                g.DrawImage(res, 0, 0);
                dbg.Save(dbgPath, ImageFormat.Png);
            }
        }
    }
}
