// Device photo loading, cut-out mask, and theme recoloring (black, ranger green, arid, camo ...).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace TacOverlay
{
    // One entry per device picture the tablet can wear. Everything is measured on a small "logical" picture
    // (LW x LH); the real photo may have any number of pixels with the same shape.
    class LayoutDef
    {
        public string Id, Name, Resource, NativeTheme;
        public int LW, LH, HoleRadius;
        public Rectangle Screen;        // the screen opening, logical units
        public Point Gear;              // settings button, logical units
        public bool AlphaMask;          // the picture already has its background cut out (transparent)
        public bool PlateRecolor;       // themes recolour a dark plate instead of a tan body
        public bool CompactLabels;      // status text goes in a small centred block (no room for a long line)
        public Point LabelAt;           // centre of that block (logical)
        public string PageTag;          // reported to the web page: data-overlay="..."
    }

    static class Layouts
    {
        public static readonly LayoutDef Atak = new LayoutDef
        {
            Id = "atak", Name = "ATAK tablet", Resource = "TacOverlay.device.png", NativeTheme = "Coyote Tan",
            LW = 774, LH = 396, HoleRadius = 13, Screen = new Rectangle(126, 69, 522, 250), Gear = new Point(692, 50),
            PageTag = "tablet"
        };
        public static readonly LayoutDef Pilot = new LayoutDef
        {
            Id = "pilot", Name = "Pilot display", Resource = "TacOverlay.mfd.png", NativeTheme = "Black",
            LW = 780, LH = 569, HoleRadius = 5, Screen = new Rectangle(100, 95, 582, 387), Gear = new Point(687, 28),
            AlphaMask = true, PlateRecolor = true, CompactLabels = true, LabelAt = new Point(390, 46), PageTag = "mfd"
        };
        public static readonly LayoutDef[] All = new LayoutDef[] { Atak, Pilot };

        public static LayoutDef Get(string id)
        {
            foreach (LayoutDef d in All) if (string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)) return d;
            return Atak;
        }
    }

    static class DeviceArt
    {
        public static LayoutDef Cur = Layouts.Atak;
        public static int LW = 774, LH = 396;
        public static int W = 774, H = 396;     // pixel size of the photo actually loaded
        public static double K = 1.0;           // photo pixels per logical pixel
        public static int MinW { get { return 3 * LW; } }   // small photos are enlarged to this first (3x): themes, camo and edges are then drawn smooth
        public const int MaxW = 3096;           // bigger photos are shrunk to this (keeps memory and start-up sane)
        public static double SrcK = 1.0;        // real pixels per logical unit in the supplied photo (decides how much sharpening helps)
        // The screen inside the photo (where the web page is shown), in logical units.
        public static Rectangle ScreenArea = Layouts.Atak.Screen;
        public static Rectangle ScreenPx = Layouts.Atak.Screen;   // the same, in photo pixels

        public static void Use(LayoutDef d)
        {
            Cur = d; LW = d.LW; LH = d.LH; ScreenArea = d.Screen;
            W = LW; H = LH; K = 1.0; SrcK = 1.0; ScreenPx = ScreenArea;
        }

        public static Bitmap LoadOriginal()
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(Cur.Resource))
            using (Bitmap tmp = new Bitmap(s))
            {
                SrcK = Math.Max(1.0, tmp.Width / (double)LW);
                W = Math.Max(MinW, Math.Min(tmp.Width, MaxW));
                H = (int)Math.Round(W * (double)LH / LW);
                K = W / (double)LW;
                ScreenPx = new Rectangle((int)Math.Round(ScreenArea.X * K), (int)Math.Round(ScreenArea.Y * K),
                                         (int)Math.Round(ScreenArea.Width * K), (int)Math.Round(ScreenArea.Height * K));
                Bitmap b = new Bitmap(W, H, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(tmp, 0, 0, W, H);
                }
                return b;
            }
        }

        // Grow the "outside" area by n pixels (4-neighbour). Pixels on the image border count as outside.
        static bool[] Grow(bool[] outs, int n, bool borderIsOutside)
        {
            bool[] cur = (bool[])outs.Clone();
            if (borderIsOutside)
            {
                for (int x = 0; x < W; x++) { cur[x] = true; cur[(H - 1) * W + x] = true; }
                for (int y = 0; y < H; y++) { cur[y * W] = true; cur[y * W + W - 1] = true; }
            }
            for (int it = 0; it < n; it++)
            {
                bool[] nxt = (bool[])cur.Clone();
                for (int y = 1; y < H - 1; y++)
                    for (int x = 1; x < W - 1; x++)
                    {
                        int k = y * W + x;
                        if (!cur[k] && (cur[k - 1] || cur[k + 1] || cur[k - W] || cur[k + W])) nxt[k] = true;
                    }
                cur = nxt;
            }
            return cur;
        }

        static int Px(double logical) { return Math.Max(1, (int)Math.Round(logical * K)); }

        static byte[] ReadPixels(Bitmap bmp, out int stride)
        {
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] px = new byte[bd.Stride * H];
            Marshal.Copy(bd.Scan0, px, 0, px.Length);
            stride = bd.Stride;
            bmp.UnlockBits(bd);
            return px;
        }

        // Background = near-grey/white pixels connected to the image border (flood fill), so the
        // white photo backdrop and soft shadow are cut away but the tablet itself is kept.
        public static bool[] BuildMask(Bitmap bmp)
        {
            int stride;
            byte[] px = ReadPixels(bmp, out stride);
            if (Cur.AlphaMask)
            {
                // the picture already has its outline (and any holes) cut out: transparent = outside
                bool[] cut = new bool[W * H];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++) cut[y * W + x] = px[y * stride + x * 4 + 3] < 128;
                return cut;
            }
            bool[] bg = new bool[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * stride + x * 4;
                    int b = px[i], g = px[i + 1], r = px[i + 2];
                    int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                    bg[y * W + x] = (max - min) < 24 && (r + g + b) / 3 > 90;
                }

            bool[] outs = new bool[W * H];
            Stack<int> st = new Stack<int>();
            Action<int, int> seed = delegate (int x, int y)
            {
                int k = y * W + x;
                if (bg[k] && !outs[k]) { outs[k] = true; st.Push(k); }
            };
            for (int x = 0; x < W; x++) { seed(x, 0); seed(x, H - 1); }
            for (int y = 0; y < H; y++) { seed(0, y); seed(W - 1, y); }
            while (st.Count > 0)
            {
                int k = st.Pop();
                int x = k % W, y = k / W;
                if (x > 0) seed(x - 1, y);
                if (x < W - 1) seed(x + 1, y);
                if (y > 0) seed(x, y - 1);
                if (y < H - 1) seed(x, y + 1);
            }

            // shave one (logical) pixel off the edge to remove the light anti-aliasing halo
            return Grow(outs, Px(1), false);
        }

        // Soft (anti-aliased) edge: erode one more pixel to drop the light halo, blur, then smoothstep.
        public static byte[] BuildSoftAlpha(bool[] outside)
        {
            bool[] eroded = Grow(outside, Px(1), true);
            float[] e = new float[W * H];
            for (int k = 0; k < e.Length; k++) e[k] = eroded[k] ? 0f : 1f;
            float[] b = e;
            int passes = 2 * Px(1);
            for (int i = 0; i < passes; i++) b = Blur3(b);
            byte[] res = new byte[W * H];
            for (int k = 0; k < res.Length; k++)
            {
                float t = (b[k] - 0.25f) / 0.5f;
                t = t < 0f ? 0f : (t > 1f ? 1f : t);
                t = t * t * (3f - 2f * t);
                res[k] = (byte)(t * 255f + 0.5f);
            }
            return res;
        }

        static float[] Blur3(float[] s)
        {
            // 3x3 box blur done as two 1-D passes (same result, much faster); edges average over the pixels that exist
            float[] t = new float[W * H], d = new float[W * H];
            for (int y = 0; y < H; y++)
            {
                int row = y * W;
                for (int x = 0; x < W; x++)
                {
                    float sum = s[row + x]; int n = 1;
                    if (x > 0) { sum += s[row + x - 1]; n++; }
                    if (x < W - 1) { sum += s[row + x + 1]; n++; }
                    t[row + x] = sum / n;
                }
            }
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int k = y * W + x;
                    float sum = t[k]; int n = 1;
                    if (y > 0) { sum += t[k - W]; n++; }
                    if (y < H - 1) { sum += t[k + W]; n++; }
                    d[k] = sum / n;
                }
            return d;
        }

        // Themed (opaque) photo + soft alpha -> premultiplied ARGB bitmap, ready for per-pixel-alpha scaling.
        public static Bitmap MakeSource(Bitmap themed, byte[] alpha)
        {
            int stride;
            byte[] px = ReadPixels(themed, out stride);
            Bitmap res = new Bitmap(W, H, PixelFormat.Format32bppPArgb);
            BitmapData rd = res.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            byte[] dst = new byte[rd.Stride * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int a = alpha[y * W + x];
                    int si = y * stride + x * 4, di = y * rd.Stride + x * 4;
                    dst[di] = (byte)(px[si] * a / 255);
                    dst[di + 1] = (byte)(px[si + 1] * a / 255);
                    dst[di + 2] = (byte)(px[si + 2] * a / 255);
                    dst[di + 3] = (byte)a;
                }
            Marshal.Copy(dst, 0, rd.Scan0, dst.Length);
            res.UnlockBits(rd);
            return res;
        }

        // Light unsharp mask on fully opaque pixels only (keeps the soft edge clean). Premultiplied ARGB in/out.
        public static void Sharpen(Bitmap bmp, float amount)
        {
            int w = bmp.Width, h = bmp.Height;
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
            int stride = bd.Stride;
            byte[] px = new byte[stride * h];
            Marshal.Copy(bd.Scan0, px, 0, px.Length);
            byte[] src = (byte[])px.Clone();
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int i = y * stride + x * 4;
                    if (src[i + 3] != 255 || src[i - 1] != 255 || src[i + 7] != 255 ||
                        src[i - stride + 3] != 255 || src[i + stride + 3] != 255 ||
                        src[i - stride - 4 + 3] != 255 || src[i - stride + 4 + 3] != 255 ||
                        src[i + stride - 4 + 3] != 255 || src[i + stride + 4 + 3] != 255) continue;
                    for (int c = 0; c < 3; c++)
                    {
                        int tl = src[i - stride - 4 + c], t = src[i - stride + c], tr = src[i - stride + 4 + c];
                        int l = src[i - 4 + c], m = src[i + c], r = src[i + 4 + c];
                        int bl = src[i + stride - 4 + c], b = src[i + stride + c], br = src[i + stride + 4 + c];
                        float blur = (tl + tr + bl + br + 2f * (t + l + r + b) + 4f * m) / 16f;
                        float v = m + amount * (m - blur);
                        px[i + c] = (byte)(v < 0f ? 0f : (v > 255f ? 255f : v));
                    }
                }
            Marshal.Copy(px, 0, bd.Scan0, px.Length);
            bmp.UnlockBits(bd);
        }

        // Per source row: [x0,x1, x0,x1, ...] runs of pixels that belong to the tablet. Used for the window region.
        public static int[][] BuildRuns(bool[] outside)
        {
            int[][] runs = new int[H][];
            for (int y = 0; y < H; y++)
            {
                List<int> l = new List<int>();
                int x = 0;
                while (x < W)
                {
                    if (outside[y * W + x]) { x++; continue; }
                    int x0 = x;
                    while (x < W && !outside[y * W + x]) x++;
                    l.Add(x0); l.Add(x);
                }
                runs[y] = l.ToArray();
            }
            return runs;
        }
    }

    static class Themes
    {
        public const string Default = "Coyote Tan";
        public const string CustomName = "Custom Wrap";
        public static readonly string[] Names = new string[] { "Coyote Tan", "Black", "Ranger Green", "Arid", "JG Camo", "MultiCam", CustomName };

        // ---- custom wrap: the person's own picture, painted over the tablet's body ----
        static byte[] wrapPx;            // the picture fitted to the photo, BGRA
        static bool wrapDark;
        static int wrapW, wrapH;

        // Where the picture is kept (a copy, so deleting the original file doesn't break it).
        public static string WrapPath
        {
            get
            {
                string dir = Environment.GetEnvironmentVariable("TACOVERLAY_DATA");
                if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TacOverlay");
                return Path.Combine(dir, "wrap.png");
            }
        }

        public static bool HasWrap() { return File.Exists(WrapPath); }

        // Reads any picture the person chose, shrinks it if it is huge and keeps our own PNG copy.
        public static bool SaveWrap(string srcPath, out string error)
        {
            error = null;
            try
            {
                using (Image img = LoadPicture(srcPath))
                {
                    double k = Math.Min(1.0, 2600.0 / Math.Max(img.Width, img.Height));
                    int w = Math.Max(1, (int)Math.Round(img.Width * k)), h = Math.Max(1, (int)Math.Round(img.Height * k));
                    using (Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics g = Graphics.FromImage(b))
                        {
                            g.Clear(Color.White);   // transparent pictures get a white backing
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                            g.DrawImage(img, 0, 0, w, h);
                        }
                        string path = WrapPath;
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        b.Save(path, ImageFormat.Png);
                    }
                }
                ClearWrapCache();
                return true;
            }
            catch
            {
                error = "That file could not be read as a picture. Use a PNG or JPG (WebP works if Windows has the WebP codec).";
                return false;
            }
        }

        // PNG / JPG / BMP / GIF load directly; anything else (notably WebP) goes through Windows' own image codecs if it has them.
        static Image LoadPicture(string path)
        {
            try
            {
                using (Image raw = Image.FromFile(path)) return new Bitmap(raw);
            }
            catch
            {
                Image viaWic = LoadViaWic(path);
                if (viaWic == null) throw new InvalidDataException("unreadable picture");
                return viaWic;
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static Image LoadViaWic(string path)
        {
            try
            {
                System.Windows.Media.Imaging.BitmapDecoder dec = System.Windows.Media.Imaging.BitmapDecoder.Create(
                    new Uri(path), System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                System.Windows.Media.Imaging.PngBitmapEncoder enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(dec.Frames[0]));
                MemoryStream ms = new MemoryStream();
                enc.Save(ms);
                ms.Position = 0;
                return new Bitmap(ms);
            }
            catch { return null; }
        }

        public static void ClearWrapCache() { wrapPx = null; wrapDark = false; }

        // Fit the picture over the whole tablet ("cover": it fills the tablet, extra is cropped from the centre).
        static bool EnsureWrap()
        {
            if (wrapPx != null && wrapW == DeviceArt.W && wrapH == DeviceArt.H) return true;
            try
            {
                if (!HasWrap()) return false;
                int W = DeviceArt.W, H = DeviceArt.H;
                using (Bitmap src = new Bitmap(WrapPath))
                using (Bitmap fit = new Bitmap(W, H, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(fit))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        double s = Math.Max(W / (double)src.Width, H / (double)src.Height);
                        float dw = (float)(src.Width * s), dh = (float)(src.Height * s);
                        g.DrawImage(src, (W - dw) / 2f, (H - dh) / 2f, dw, dh);
                    }
                    BitmapData bd = fit.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    byte[] px = new byte[bd.Stride * H];
                    Marshal.Copy(bd.Scan0, px, 0, px.Length);
                    int stride = bd.Stride;
                    fit.UnlockBits(bd);
                    if (stride != W * 4)   // repack so index = (y*W+x)*4
                    {
                        byte[] packed = new byte[W * H * 4];
                        for (int y = 0; y < H; y++) Buffer.BlockCopy(px, y * stride, packed, y * W * 4, W * 4);
                        px = packed;
                    }
                    long sum = 0; int n = 0;
                    for (int i = 0; i < px.Length; i += 4 * 97) { sum += (px[i + 2] * 299 + px[i + 1] * 587 + px[i] * 114) / 1000; n++; }
                    wrapDark = n > 0 && sum / n < 105;
                    wrapPx = px; wrapW = W; wrapH = H;
                }
                return true;
            }
            catch { wrapPx = null; return false; }
        }

        // colours used for the little swatches in the settings window
        public static Color[] Swatch(string name)
        {
            switch (name)
            {
                case CustomName: return new Color[] { Color.FromArgb(232, 96, 150), Color.FromArgb(96, 150, 232), Color.FromArgb(150, 110, 220), Color.FromArgb(90, 210, 190) };
                case "Black": return new Color[] { Color.FromArgb(58, 60, 58) };
                case "Ranger Green": return new Color[] { Color.FromArgb(78, 90, 58) };
                case "Arid": return new Color[] { Color.FromArgb(206, 186, 146) };
                case "JG Camo": return new Color[] { Color.FromArgb(84, 98, 52), Color.FromArgb(50, 62, 34), Color.FromArgb(104, 80, 48), Color.FromArgb(28, 30, 22) };
                case "MultiCam": return new Color[] { Color.FromArgb(178, 160, 122), Color.FromArgb(128, 134, 92), Color.FromArgb(112, 88, 60), Color.FromArgb(80, 68, 46) };
                default: return new Color[] { Color.FromArgb(150, 125, 88) };
            }
        }

        // dark themes get light text on the bezel
        public static bool IsDark(string name)
        {
            name = Normalize(name);
            if (name == CustomName) return wrapPx != null && wrapDark;
            return name == "Black" || name == "Ranger Green" || name == "JG Camo";
        }

        public static string Normalize(string name)
        {
            foreach (string n in Names) if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return n;
            return Default;
        }

        // ---- procedural camo noise ----
        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }
        static float VNoise(float x, float y, int seed)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            float xf = x - xi, yf = y - yi;
            float u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
            float a = Hash(xi, yi, seed), b = Hash(xi + 1, yi, seed), c = Hash(xi, yi + 1, seed), d = Hash(xi + 1, yi + 1, seed);
            return (a + (b - a) * u) + ((c + (d - c) * u) - (a + (b - a) * u)) * v;
        }
        static float Fbm(float x, float y, int seed)
        {
            return 0.6f * VNoise(x, y, seed) + 0.3f * VNoise(x * 2f, y * 2f, seed + 1) + 0.1f * VNoise(x * 4f, y * 4f, seed + 2);
        }

        static void BaseColor(string n, float x, float y, out float r, out float g, out float b)
        {
            switch (n)
            {
                case "Black": r = 64; g = 66; b = 64; return;
                case "Ranger Green": r = 78; g = 90; b = 58; return;
                case "Arid": r = 206; g = 186; b = 146; return;
                case "JG Camo":
                    {
                        float n1 = Fbm(x / 46f, y / 46f, 11), n2 = Fbm(x / 32f + 9f, y / 32f, 23), n3 = Fbm(x / 22f, y / 22f + 5f, 37);
                        if (n3 > 0.60f) { r = 28; g = 30; b = 22; }
                        else if (n2 > 0.57f) { r = 104; g = 80; b = 48; }
                        else if (n1 > 0.52f) { r = 50; g = 62; b = 34; }
                        else { r = 84; g = 98; b = 52; }
                        return;
                    }
                case "MultiCam":
                    {
                        float n1 = Fbm(x / 40f, y / 40f, 5), n2 = Fbm(x / 28f + 3f, y / 28f, 17), n3 = Fbm(x / 18f, y / 18f + 8f, 29);
                        if (n3 > 0.62f) { r = 80; g = 68; b = 46; }
                        else if (n2 > 0.56f) { r = 112; g = 88; b = 60; }
                        else if (n1 > 0.53f) { r = 128; g = 134; b = 92; }
                        else { r = 178; g = 160; b = 122; }
                        return;
                    }
                default: r = 150; g = 125; b = 88; return;
            }
        }

        // Recolour the tan body of the tablet, keeping the original shading, the black rubber,
        // the screen and everything that isn't tan.
        public static Bitmap Build(Bitmap orig, bool[] outside, string name)
        {
            name = Normalize(name);
            if (name == DeviceArt.Cur.NativeTheme) return new Bitmap(orig);   // the photo's own colour
            bool custom = name == CustomName;
            bool plate = DeviceArt.Cur.PlateRecolor;
            if (custom && !EnsureWrap()) return new Bitmap(orig);   // no picture chosen yet: plain tablet
            byte[] wrap = wrapPx;

            BitmapData bd = orig.LockBits(new Rectangle(0, 0, DeviceArt.W, DeviceArt.H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] px = new byte[bd.Stride * DeviceArt.H];
            Marshal.Copy(bd.Scan0, px, 0, px.Length);
            int stride = bd.Stride;
            orig.UnlockBits(bd);

            byte[] dst = (byte[])px.Clone();
            float invK = (float)(1.0 / DeviceArt.K);   // camo patterns keep the same size whatever the photo resolution
            Rectangle screenPx = DeviceArt.ScreenPx;
            for (int y = 0; y < DeviceArt.H; y++)
                for (int x = 0; x < DeviceArt.W; x++)
                {
                    if (outside[y * DeviceArt.W + x] || screenPx.Contains(x, y)) continue;
                    int i = y * stride + x * 4;
                    float b = px[i], g = px[i + 1], r = px[i + 2];
                    float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                    if (max < 10) continue;
                    float sat = (max - min) / max;
                    float lum = 0.299f * r + 0.587f * g + 0.114f * b;
                    float tan;
                    if (plate)
                    {
                        // dark metal plate: grey-ish pixels of middling brightness (not the black outlines, not the white markings)
                        tan = Smooth(lum, 25f, 36f) * (1f - Smooth(lum, 78f, 100f)) * (1f - Clamp01((sat - 0.22f) / 0.15f));
                    }
                    else tan = Clamp01((sat - 0.10f) / 0.10f) * Clamp01((lum - 18f) / 20f);
                    if (tan <= 0f) continue;

                    float br, bgc, bb;
                    if (custom)
                    {
                        int wi = (y * DeviceArt.W + x) * 4;
                        bb = wrap[wi]; bgc = wrap[wi + 1]; br = wrap[wi + 2];
                    }
                    else BaseColor(name, x * invK, y * invK, out br, out bgc, out bb);
                    float shade = plate ? Math.Max(0.30f, Math.Min(1.7f, lum / 56f)) : Math.Max(0.25f, Math.Min(1.8f, lum / 120f));
                    float nr = Math.Min(255f, br * shade), ng = Math.Min(255f, bgc * shade), nb = Math.Min(255f, bb * shade);
                    dst[i + 2] = (byte)(r + (nr - r) * tan);
                    dst[i + 1] = (byte)(g + (ng - g) * tan);
                    dst[i] = (byte)(b + (nb - b) * tan);
                }

            Bitmap res = new Bitmap(DeviceArt.W, DeviceArt.H, PixelFormat.Format32bppArgb);
            BitmapData rd = res.LockBits(new Rectangle(0, 0, DeviceArt.W, DeviceArt.H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(dst, 0, rd.Scan0, Math.Min(dst.Length, rd.Stride * DeviceArt.H));
            res.UnlockBits(rd);
            return res;
        }

        static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
        static float Smooth(float v, float a, float b) { float t = Clamp01((v - a) / (b - a)); return t * t * (3f - 2f * t); }
    }
}
