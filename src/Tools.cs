// Offline tools panel (bearing / distance between grids, 9-line template, checklist) and the actions that go with the
// pilot-display buttons: mission log, bookmarks, layout presets.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace TacOverlay
{
    // Anything that slides in beside the page (notes pad, tools).
    abstract class SidePanel : Panel
    {
        public abstract void Relayout(int w, int h, double sc);
    }

    static class FontUtil
    {
        // Give controls a font of this pixel size. A new Font object is made only when the size really changes (WinForms ignores
        // an "equal" font and would keep pointing at the old object - which must not be disposed then).
        public static void Apply(ref Font field, string family, float px, FontStyle style, System.Collections.Generic.IEnumerable<Control> targets)
        {
            if (field != null && Math.Abs(field.Size - px) < 0.01f) return;
            Font old = field;
            field = new Font(family, px, style, GraphicsUnit.Pixel);
            foreach (Control c in targets) c.Font = field;
            if (old != null) old.Dispose();
        }
    }

    class ToolsPanel : SidePanel
    {
        public event Action CloseClicked;
        public event Action EscPressed;
        readonly Button[] tabs = new Button[3];
        readonly Button close = new Button();
        int tab;
        string clearArmed;
        Font uiFont, boxFont;
        System.Windows.Forms.Timer saveTimer;

        // tab 0: bearing / distance
        readonly Label lFrom = new Label(), lTo = new Label(), lHelp = new Label();
        readonly TextBox tFrom = new TextBox(), tTo = new TextBox(), tRes = new TextBox();
        readonly Button bCopyRes = new Button();

        // tab 1: 9-line
        static readonly string[] NineLabels = new string[] { "1 IP / BP", "2 HEADING", "3 DISTANCE", "4 TGT ELEV", "5 TGT DESC", "6 TGT LOC", "7 MARK", "8 FRIENDLIES", "9 EGRESS", "REMARKS" };
        readonly Label[] nl = new Label[10];
        readonly TextBox[] nt = new TextBox[10];
        readonly Button bCopyNine = new Button(), bClearNine = new Button();

        // tab 2: checklist
        readonly CheckedListBox chk = new CheckedListBox();
        readonly TextBox edit = new TextBox();
        readonly Button bEdit = new Button(), bReset = new Button();
        bool editing, loadingChk;

        static readonly Color Bg = Color.FromArgb(24, 27, 22), BtnBg = Color.FromArgb(40, 45, 36), Field = Color.FromArgb(14, 17, 12),
            Fg = Color.FromArgb(205, 215, 190), Green = Color.FromArgb(150, 205, 90), Dim = Color.FromArgb(130, 140, 115);

        static string FileFor(string name) { return Path.Combine(NotesPanel.Dir, name); }

        public ToolsPanel()
        {
            BackColor = Bg;
            string[] names = new string[] { "BRG/DIST", "9-LINE", "CHECKLIST" };
            for (int i = 0; i < 3; i++)
            {
                Button b = tabs[i] = new Button(); b.Text = names[i]; b.Tag = i;
                b.Click += delegate (object s, EventArgs e) { SelectTab((int)((Button)s).Tag); };
                Controls.Add(b);
            }
            close.Text = "✕"; close.Click += delegate { SaveAll(); if (CloseClicked != null) CloseClicked(); };
            Controls.Add(close);

            // --- bearing / distance ---
            tFrom.Name = "brgFrom"; tTo.Name = "brgTo"; tRes.Name = "brgRes";
            lFrom.Text = "FROM GRID"; lTo.Text = "TO GRID";
            lHelp.Text = "Type grids like 123456 or 123 456 (digits are split in half). 6 digits = 100 m squares, 8 = 10 m.";
            tRes.Multiline = true; tRes.ReadOnly = true; tRes.ScrollBars = ScrollBars.None;
            bCopyRes.Text = "COPY";
            bCopyRes.Click += delegate { try { if (tRes.Text.Length > 0) Clipboard.SetText(tRes.Text.Replace("\r\n", "  |  ")); } catch { } };
            tFrom.TextChanged += delegate { Compute(); }; tTo.TextChanged += delegate { Compute(); };
            Controls.AddRange(new Control[] { lFrom, lTo, lHelp, tFrom, tTo, tRes, bCopyRes });

            // --- 9-line ---
            string[] saved = new string[10];
            try { if (File.Exists(FileFor("nineline.txt"))) { string[] ln = File.ReadAllLines(FileFor("nineline.txt")); for (int i = 0; i < 10 && i < ln.Length; i++) saved[i] = ln[i]; } } catch { }
            for (int i = 0; i < 10; i++)
            {
                nl[i] = new Label(); nl[i].Text = NineLabels[i];
                nt[i] = new TextBox(); nt[i].Name = "nine" + i; nt[i].Text = saved[i] ?? "";
                nt[i].TextChanged += delegate { if (saveTimer != null) { saveTimer.Stop(); saveTimer.Start(); } };
                Controls.Add(nl[i]); Controls.Add(nt[i]);
            }
            bCopyNine.Text = "COPY 9-LINE"; bClearNine.Text = "CLEAR";
            bCopyNine.Click += delegate { try { Clipboard.SetText(NineLineText()); } catch { } };
            bClearNine.Click += delegate
            {
                if (clearArmed == null) { clearArmed = "x"; bClearNine.Text = "SURE?"; bClearNine.ForeColor = Color.FromArgb(255, 150, 40); return; }
                clearArmed = null; bClearNine.Text = "CLEAR"; bClearNine.ForeColor = Fg;
                foreach (TextBox t in nt) t.Text = "";
            };
            Controls.AddRange(new Control[] { bCopyNine, bClearNine });

            // --- checklist ---
            chk.Name = "chkList";
            chk.CheckOnClick = true; chk.BorderStyle = BorderStyle.None; chk.IntegralHeight = false;
            edit.Multiline = true; edit.AcceptsReturn = true; edit.ScrollBars = ScrollBars.Vertical; edit.BorderStyle = BorderStyle.None; edit.Visible = false;
            bEdit.Text = "EDIT LIST"; bReset.Text = "UNTICK ALL";
            bEdit.Click += delegate { ToggleEdit(); };
            bReset.Click += delegate { loadingChk = true; for (int i = 0; i < chk.Items.Count; i++) chk.SetItemChecked(i, false); loadingChk = false; SaveChecks(); };
            chk.ItemCheck += delegate (object s, ItemCheckEventArgs e) { if (!loadingChk) BeginInvoke((MethodInvoker)delegate { SaveChecks(); }); };
            Controls.AddRange(new Control[] { chk, edit, bEdit, bReset });
            LoadChecklist();

            foreach (Control c in Controls)
            {
                Button b = c as Button;
                if (b != null)
                {
                    b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.UseVisualStyleBackColor = false;
                    b.BackColor = BtnBg; b.ForeColor = Fg; b.TabStop = false; b.Cursor = Cursors.Hand; b.Padding = Padding.Empty;
                }
                else if (c is TextBox) { TextBox t = (TextBox)c; t.BorderStyle = BorderStyle.FixedSingle; t.BackColor = Field; t.ForeColor = Fg; }
                else if (c is Label) { c.ForeColor = Dim; }
            }
            tRes.ForeColor = Green; edit.BorderStyle = BorderStyle.FixedSingle;
            chk.BackColor = Field; chk.ForeColor = Fg;
            foreach (Control c in new Control[] { tFrom, tTo, tRes }) c.KeyDown += NavKeys;
            foreach (TextBox t in nt) t.KeyDown += NavKeys;
            edit.KeyDown += NavKeys;

            saveTimer = new System.Windows.Forms.Timer(); saveTimer.Interval = 800;
            saveTimer.Tick += delegate { saveTimer.Stop(); SaveAll(); };
            SelectTab(0);
        }

        void NavKeys(object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; if (EscPressed != null) EscPressed(); }
            else if (e.Control && e.KeyCode == Keys.A && s is TextBox) { ((TextBox)s).SelectAll(); e.SuppressKeyPress = true; }
        }

        public int Tab { get { return tab; } }

        public void SelectTab(int i)
        {
            tab = i; clearArmed = null; bClearNine.Text = "CLEAR"; bClearNine.ForeColor = Fg;
            for (int k = 0; k < 3; k++) { tabs[k].ForeColor = k == i ? Green : Fg; tabs[k].BackColor = k == i ? Color.FromArgb(52, 60, 42) : BtnBg; }
            foreach (Control c in new Control[] { lFrom, lTo, lHelp, tFrom, tTo, tRes, bCopyRes }) c.Visible = i == 0;
            for (int k = 0; k < 10; k++) { nl[k].Visible = i == 1; nt[k].Visible = i == 1; }
            bCopyNine.Visible = i == 1; bClearNine.Visible = i == 1;
            chk.Visible = i == 2 && !editing; edit.Visible = i == 2 && editing; bEdit.Visible = i == 2; bReset.Visible = i == 2;
            if (i == 0) tFrom.Focus(); else if (i == 1) nt[0].Focus();
        }

        // ---------- bearing / distance ----------
        static bool ParseGrid(string s, out double e, out double n)
        {
            e = n = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            string[] t = s.Trim().Split(new char[] { ' ', ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries);
            string a, b;
            if (t.Length == 2) { a = t[0]; b = t[1]; }
            else if (t.Length == 1 && t[0].Length >= 2 && t[0].Length % 2 == 0) { a = t[0].Substring(0, t[0].Length / 2); b = t[0].Substring(t[0].Length / 2); }
            else return false;
            if (a.Length != b.Length || a.Length < 1 || a.Length > 5) return false;
            foreach (char c in a + b) if (c < '0' || c > '9') return false;
            double unit = Math.Pow(10, 5 - a.Length);   // metres per grid unit
            e = double.Parse(a) * unit; n = double.Parse(b) * unit;
            return true;
        }

        static readonly string[] Points = new string[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        void Compute()
        {
            double e1, n1, e2, n2;
            if (!ParseGrid(tFrom.Text, out e1, out n1) || !ParseGrid(tTo.Text, out e2, out n2)) { tRes.Text = ""; return; }
            double de = e2 - e1, dn = n2 - n1;
            double dist = Math.Sqrt(de * de + dn * dn);
            double brg = Math.Atan2(de, dn) * 180.0 / Math.PI; if (brg < 0) brg += 360.0;
            if (dist < 0.5) brg = 0;
            double back = (brg + 180.0) % 360.0;
            Func<double, string> mils = delegate (double d) { return ((int)Math.Round(d * 6400.0 / 360.0) % 6400).ToString("0000"); };
            Func<double, string> deg = delegate (double d) { return (((int)Math.Round(d)) % 360).ToString("000"); };
            tRes.Text = "DISTANCE  " + ((int)Math.Round(dist)).ToString("N0") + " m   (" + (dist / 1000.0).ToString("0.00") + " km)\r\n" +
                        "BEARING   " + deg(brg) + " deg   " + mils(brg) + " mils   " + Points[(int)Math.Round(brg / 45.0) % 8] + "\r\n" +
                        "BACK AZ   " + deg(back) + " deg   " + mils(back) + " mils";
        }

        // ---------- 9-line ----------
        string NineLineText()
        {
            StringBuilder sb = new StringBuilder("9-LINE CAS\r\n");
            for (int i = 0; i < 10; i++) if (nt[i].Text.Trim().Length > 0) sb.Append("LINE ").Append(NineLabels[i]).Append(": ").Append(nt[i].Text.Trim()).Append("\r\n");
            return sb.ToString();
        }

        // ---------- checklist ----------
        static readonly string[] DefaultItems = new string[] { "Radio check done", "Weapons and ammo loaded", "Medical kit", "Map and route loaded", "Brief understood (task, ROE)", "Comms plan / frequencies", "Rally and extraction points known", "Vehicle / aircraft pre-checks" };

        void LoadChecklist()
        {
            string[] items = DefaultItems;
            try { if (File.Exists(FileFor("checklist.txt"))) { string[] ln = File.ReadAllLines(FileFor("checklist.txt")); List<string> l = new List<string>(); foreach (string s in ln) if (s.Trim().Length > 0) l.Add(s.Trim()); if (l.Count > 0) items = l.ToArray(); } } catch { }
            HashSet<int> ticked = new HashSet<int>();
            try { if (File.Exists(FileFor("checklist_state.txt"))) foreach (string p in File.ReadAllText(FileFor("checklist_state.txt")).Split(',')) { int v; if (int.TryParse(p, out v)) ticked.Add(v); } } catch { }
            loadingChk = true;
            chk.Items.Clear();
            for (int i = 0; i < items.Length; i++) chk.Items.Add(items[i], ticked.Contains(i));
            loadingChk = false;
        }

        void SaveChecks()
        {
            try
            {
                Directory.CreateDirectory(NotesPanel.Dir);
                List<string> idx = new List<string>();
                for (int i = 0; i < chk.Items.Count; i++) if (chk.GetItemChecked(i)) idx.Add(i.ToString());
                File.WriteAllText(FileFor("checklist_state.txt"), string.Join(",", idx.ToArray()));
            }
            catch { }
        }

        void ToggleEdit()
        {
            if (!editing)
            {
                StringBuilder sb = new StringBuilder();
                foreach (object o in chk.Items) sb.Append(o.ToString()).Append("\r\n");
                edit.Text = sb.ToString(); editing = true; bEdit.Text = "SAVE LIST";
                chk.Visible = false; edit.Visible = true; edit.Focus();
            }
            else
            {
                try { Directory.CreateDirectory(NotesPanel.Dir); File.WriteAllText(FileFor("checklist.txt"), edit.Text); File.WriteAllText(FileFor("checklist_state.txt"), ""); } catch { }
                editing = false; bEdit.Text = "EDIT LIST";
                LoadChecklist();
                edit.Visible = false; chk.Visible = true;
            }
        }

        public void SaveAll()
        {
            try
            {
                Directory.CreateDirectory(NotesPanel.Dir);
                string[] ln = new string[10]; for (int i = 0; i < 10; i++) ln[i] = nt[i].Text.Replace("\r", " ").Replace("\n", " ");
                File.WriteAllLines(FileFor("nineline.txt"), ln);
            }
            catch { }
            SaveChecks();
        }

        public override void Relayout(int w, int h, double sc)
        {
            int row = (int)Math.Max(24, Math.Min(44, Math.Round(10.5 * sc))), p = Math.Max(3, row / 7);
            List<Control> uiCtl = new List<Control>(), boxCtl = new List<Control>();
            foreach (Control c in Controls) { if (c is Button || c is Label) uiCtl.Add(c); else if (c is TextBox || c is CheckedListBox) boxCtl.Add(c); }
            FontUtil.Apply(ref uiFont, "Consolas", Math.Max(10f, row * 0.44f), FontStyle.Bold, uiCtl);
            FontUtil.Apply(ref boxFont, "Consolas", (float)Math.Max(12.0, Math.Round(6.6 * sc)), FontStyle.Regular, boxCtl);

            int bs = row - 2 * p, x = p;
            for (int i = 0; i < 3; i++)
            {
                int tw = TextRenderer.MeasureText(tabs[i].Text, uiFont).Width + 14;
                tabs[i].SetBounds(x, p, tw, bs); x += tw + p;
            }
            close.SetBounds(w - p - bs, p, bs, bs);
            int top = row + p, cw = Math.Max(40, w - 2 * p);
            int fh = (int)Math.Max(22, Math.Round(boxFont.Size * 1.9));   // height of one text box

            // bearing / distance
            lFrom.SetBounds(p, top, cw, (int)(uiFont.Size * 1.5)); tFrom.SetBounds(p, lFrom.Bottom + 2, cw, fh);
            lTo.SetBounds(p, tFrom.Bottom + p * 2, cw, (int)(uiFont.Size * 1.5)); tTo.SetBounds(p, lTo.Bottom + 2, cw, fh);
            tRes.SetBounds(p, tTo.Bottom + p * 2, cw, (int)(boxFont.Size * 4.9));
            bCopyRes.SetBounds(p, tRes.Bottom + p, TextRenderer.MeasureText("COPY", uiFont).Width + 16, bs);
            lHelp.SetBounds(p, bCopyRes.Bottom + p * 2, cw, Math.Max(20, h - bCopyRes.Bottom - p * 3));

            // 9-line: label on the left, box on the right
            int lw = (int)(cw * 0.36), rh = Math.Max(fh + 2, (h - top - bs - p * 3) / 10);
            for (int i = 0; i < 10; i++)
            {
                int y = top + i * rh;
                nl[i].SetBounds(p, y + 2, lw, rh - 2);
                nt[i].SetBounds(p + lw, y, cw - lw, fh);
            }
            int by = top + 10 * rh + p;
            bCopyNine.SetBounds(p, by, TextRenderer.MeasureText("COPY 9-LINE", uiFont).Width + 16, bs);
            bClearNine.SetBounds(bCopyNine.Right + p, by, TextRenderer.MeasureText("SURE?", uiFont).Width + 16, bs);

            // checklist
            int cb = h - bs - p * 2;
            chk.SetBounds(p, top, cw, Math.Max(30, cb - top));
            edit.SetBounds(p, top, cw, Math.Max(30, cb - top));
            bEdit.SetBounds(p, cb + p, TextRenderer.MeasureText("SAVE LIST", uiFont).Width + 16, bs);
            bReset.SetBounds(bEdit.Right + p, cb + p, TextRenderer.MeasureText("UNTICK ALL", uiFont).Width + 16, bs);
            chk.ItemHeight = (int)Math.Max(18, Math.Round(boxFont.Size * 1.7));
        }
    }

    // A small square button with a drawn icon (crisp at any size, no font symbols).
    class IconButton : Control
    {
        public enum Kind { Restart, Close }
        readonly Kind kind;
        bool hot, down;

        public IconButton(Kind k)
        {
            kind = k;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false; Cursor = Cursors.Hand;
        }

        public void PerformClick() { OnClick(EventArgs.Empty); }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color back = down ? Color.FromArgb(72, 84, 56) : !hot ? Color.FromArgb(40, 45, 36) : kind == Kind.Close ? Color.FromArgb(128, 52, 38) : Color.FromArgb(62, 72, 48);
            float rad = Math.Max(3f, Width / 4f), w = Width - 1, h = Height - 1;
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddArc(0, 0, rad, rad, 180, 90); p.AddArc(w - rad, 0, rad, rad, 270, 90); p.AddArc(w - rad, h - rad, rad, rad, 0, 90); p.AddArc(0, h - rad, rad, rad, 90, 90);
                p.CloseFigure();
                using (Brush b = new SolidBrush(back)) g.FillPath(b, p);
            }
            float cx = Width / 2f, cy = Height / 2f, s = Math.Min(Width, Height) * 0.21f;
            Color ink = Color.FromArgb(220, 228, 205);
            using (Pen pen = new Pen(ink, Math.Max(1.5f, Width / 11f)))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                if (kind == Kind.Close) { g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s); g.DrawLine(pen, cx - s, cy + s, cx + s, cy - s); return; }
                float r = s * 1.2f;
                g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, -40, 285);
                double a = 245 * Math.PI / 180;   // where the arc ends: the arrow head goes there, pointing on round the circle
                float ex = cx + (float)(r * Math.Cos(a)), ey = cy + (float)(r * Math.Sin(a)), tx = (float)-Math.Sin(a), ty = (float)Math.Cos(a), n = s * 0.75f;
                using (Brush b = new SolidBrush(ink))
                    g.FillPolygon(b, new PointF[] { new PointF(ex + tx * n * 1.3f, ey + ty * n * 1.3f), new PointF(ex + (float)Math.Cos(a) * n, ey + (float)Math.Sin(a) * n), new PointF(ex - (float)Math.Cos(a) * n, ey - (float)Math.Sin(a) * n) });
            }
        }
    }

    // The game panel: a title strip (restart / close) and, under it, a second page view that fills the rest.
    class GamePanel : SidePanel
    {
        public event Action CloseClicked;
        public event Action ReloadClicked;
        readonly Label title = new Label();
        readonly IconButton reload = new IconButton(IconButton.Kind.Restart), close = new IconButton(IconButton.Kind.Close);
        readonly ToolTip tips = new ToolTip();
        Font uiFont;
        Rectangle viewArea = new Rectangle(0, 0, 10, 10);
        public Rectangle ViewArea { get { return viewArea; } }     // where the game's page view goes (in this panel's own coordinates)
        public void ClickClose() { close.PerformClick(); }          // (the self-test presses the button this way)

        public GamePanel()
        {
            BackColor = Color.FromArgb(24, 27, 22);
            title.Text = "RUN AWAY FROM NIVE"; title.ForeColor = Color.FromArgb(155, 212, 90); title.TextAlign = ContentAlignment.MiddleLeft; title.AutoEllipsis = true;
            reload.Click += delegate { if (ReloadClicked != null) ReloadClicked(); };
            close.Click += delegate { if (CloseClicked != null) CloseClicked(); };
            tips.SetToolTip(reload, "Restart the game"); tips.SetToolTip(close, "Close the game");
            Controls.AddRange(new Control[] { title, reload, close });
        }

        public override void Relayout(int w, int h, double sc)
        {
            int row = (int)Math.Max(26, Math.Min(46, Math.Round(11.5 * sc))), p = Math.Max(3, row / 8);
            FontUtil.Apply(ref uiFont, "Consolas", Math.Max(10f, row * 0.42f), FontStyle.Bold, new Control[] { title });
            int bs = row - 2 * p;
            close.SetBounds(w - p - bs, p, bs, bs);
            reload.SetBounds(close.Left - p - bs, p, bs, bs);
            title.SetBounds(p * 2, p, Math.Max(10, reload.Left - 3 * p), bs);
            viewArea = new Rectangle(0, row, Math.Max(10, w), Math.Max(20, h - row));
        }

        protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
    }

    // The game's sound files. They are NOT part of the program source: it looks in a "sounds" folder next to the program,
    // then in %LocalAppData%\TacOverlay\sounds, and hands what it finds to the game page, which plays it inside its sandbox.
    // A missing file just means that sound is silent.
    static class GameSounds
    {
        public static readonly string[][] Files = new string[][]
        {
            new string[] { "music", "game-music.mp3" },         // background music while you run (put your own here)
            new string[] { "enrique", "enrique.mp3" },          // you trip or hit something
            new string[] { "ambience", "fnaf2-ambience.mp3" },  // loops while the chaser is on screen
            new string[] { "slowed", "enrique-slowed.mp3" }     // caught
        };
        const long MaxBytes = 12 * 1024 * 1024;

        public static string[] Folders()
        {
            return new string[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sounds"),
                Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TacOverlay"), "sounds")
            };
        }

        // the file's contents as base64 text, or null when it isn't there (or is far too big to be a game sound)
        public static string Read(string file)
        {
            foreach (string folder in Folders())
            {
                try
                {
                    string p = Path.Combine(folder, file);
                    if (!File.Exists(p)) continue;
                    if (new FileInfo(p).Length > MaxBytes) { AppLog.Write("game sound too big, skipped: " + file); continue; }
                    return Convert.ToBase64String(File.ReadAllBytes(p));
                }
                catch (Exception ex) { AppLog.Write("game sound " + file + ": " + ex.Message); }
            }
            return null;
        }
    }

    // The game itself: a small original 3-lane runner, written for this app. It is one page that runs with no internet at all
    // (nothing is downloaded, there are no ads, and the page view it runs in is not allowed to reach the network).
    static class RunnerGame
    {
        public const string Html = @"<!doctype html><html><head><meta charset='utf-8'><meta http-equiv='Content-Security-Policy' content='default-src &#39;none&#39;; script-src &#39;unsafe-inline&#39;; style-src &#39;unsafe-inline&#39;; img-src data:'><meta name='viewport' content='width=device-width,initial-scale=1'>
<style>html,body{margin:0;height:100%;overflow:hidden;background:#0b0e09}canvas{display:block;width:100%;height:100%;outline:none;user-select:none}</style></head>
<body><canvas id='c' tabindex='0'></canvas><script>
(function(){
window.__err=[];window.onerror=function(m,s,l){window.__err.push(m+'@'+l);};
var cv=document.getElementById('c'),g=cv.getContext('2d');
var W=300,H=400,dpr=1,K=5,LS=90,cx=150,hor=100,py=320,ZMIN=-0.15,Z0=1.5,TH=1.5;
var skyC=document.createElement('canvas'),skyKey=-1,dirty=true;
function fit(){dpr=Math.min(2,window.devicePixelRatio||1);W=Math.max(120,innerWidth);H=Math.max(160,innerHeight);cv.width=Math.round(W*dpr);cv.height=Math.round(H*dpr);g.setTransform(dpr,0,0,dpr,0,0);cx=W/2;hor=H*0.27;py=H*0.80;LS=Math.min(W*0.30,(py-hor)*0.62);skyKey=-1;dirty=true;}
addEventListener('resize',fit);fit();
var COP=new Image();COP.onload=function(){dirty=true;};COP.src='data:image/jpeg;base64,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';
var best=0,mode='title',paused=false,countdown=0,score=0,coins=0,speed=0.55,speedMul=1,dist=0,time=0,overAt=0,shake=0,nextRow=0.6,safe=1,frozen=false;
var lane=1,prevLane=1,laneX=1,pos=0,vel=0,slide=0,supH=0,stumble=0,invuln=0,gap=1,copX=1,copT=0,tun=0,nextScn=0,bigCool=0,poleFlip=1,lastPower=0,newBest=false;
var items=[],scn=[],msgs=[],parts=[],sfxChase=false;
var SPEED0=0.55,SPEEDMAX=1.45,G=14,V0=5.4;
var PW={magnet:{dur:8,c:'#e0463c',n:'MAGNET'},sneakers:{dur:8,c:'#3fae4b',n:'SUPER SNEAKERS'},shield:{dur:12,c:'#3f8fe8',n:'SHIELD'},x2:{dur:10,c:'#e3a815',n:'2X SCORE'}};
var pw={magnet:0,sneakers:0,shield:0,x2:0};
function S(z){return 1/(1+K*z);}
function Y(z){return hor+(py-hor)*S(z);}
function X(lx,z){return cx+lx*LS*S(z);}
function clamp(v,a,b){return v<a?a:(v>b?b:v);}
function sh(c,d){var n=parseInt(c.slice(1),16),r=clamp((n>>16)+d,0,255),gg=clamp(((n>>8)&255)+d,0,255),b=clamp((n&255)+d,0,255);return 'rgb('+r+','+gg+','+b+')';}
function post(m){try{window.chrome.webview.postMessage(m);}catch(e){}}
function msg(t,c){msgs.push({t:t,c:c,life:1.1});}
function shuf(a){for(var i=a.length-1;i>0;i--){var j=Math.floor(Math.random()*(i+1)),t=a[i];a[i]=a[j];a[j]=t;}return a;}

/* ---------- sound: everything plays inside this sandboxed page; the app hands the mp3 files over ---------- */
var AC=window.AudioContext||window.webkitAudioContext,ac=null,master=null,raw={},buf={},snd={},sndLog=[],vol=60,muted=false;
function actx(){if(!ac&&AC){try{ac=new AC();master=ac.createGain();master.connect(ac.destination);applyVol();}catch(e){ac=null;}}return ac;}
function applyVol(){if(master)master.gain.value=muted?0:Math.pow(vol/100,1.6);}
function decode(name){var a=actx();if(!a||!raw[name])return;var s=atob(raw[name]),u=new Uint8Array(s.length);for(var i=0;i<s.length;i++)u[i]=s.charCodeAt(i);delete raw[name];a.decodeAudioData(u.buffer,function(b){buf[name]=b;sndLog.push('loaded '+name);},function(){sndLog.push('unreadable '+name);});}
function unlock(){var a=actx();if(a&&a.state==='suspended'&&!paused)a.resume();}
function sndStart(name,loop,gain){sndStop(name);if(!ac||!buf[name])return;var s=ac.createBufferSource(),gn=ac.createGain();s.buffer=buf[name];s.loop=loop;gn.gain.value=gain;s.connect(gn);gn.connect(master);var rec={s:s,g:gn,want:gain};s.onended=function(){if(snd[name]===rec)delete snd[name];};snd[name]=rec;s.start();sndLog.push('play '+name);}
function sndStop(name){var r=snd[name];if(!r)return;delete snd[name];try{r.s.stop();}catch(e){}sndLog.push('stop '+name);}
function sndGain(name,v){var r=snd[name];if(r&&r.want!==v){r.want=v;r.g.gain.setTargetAtTime(v,ac.currentTime,0.18);}}
/* what should be playing right now: music while you run (quieter while he chases), his theme for as long as he is on screen */
function syncAudio(){
 if(!ac)return;
 var run=mode==='play',chase=run&&gap<0.9;
 if(run){if(!snd.music)sndStart('music',true,chase?0.3:0.75);sndGain('music',chase?0.3:0.75);sndStop('slowed');}else sndStop('music');
 if(chase){if(!snd.ambience)sndStart('ambience',true,0.9);}else sndStop('ambience');
 if(mode==='title'){sndStop('enrique');sndStop('slowed');}
 if(run&&sfxChase&&gap>=0.9){sfxChase=false;sndStop('enrique');}
}
function setVol(v,save){vol=clamp(Math.round(v),0,100);if(muted&&vol>0){muted=false;post('mute:0');}applyVol();dirty=true;if(save)post('vol:'+vol);}
function toggleMute(){muted=!muted;applyVol();dirty=true;post('mute:'+(muted?1:0));}

/* ---------- scenery ---------- */
var PAL=['#2a3447','#3a2f3f','#2f3d36','#403a2c','#2d2f45','#4a3328'];
function spawnScn(z){
 var s,k;
 for(s=-1;s<=1;s+=2){
  var w=0.8+Math.random()*0.7,a=2.1+Math.random()*0.3;
  scn.push({t:'blk',z:z+Math.random()*0.1,len:0.22+Math.random()*0.3,lxa:s<0?-(a+w):a,lxb:s<0?-a:(a+w),h:1.4+Math.random()*3.2,c:PAL[Math.floor(Math.random()*PAL.length)],seed:Math.floor(Math.random()*1073741824)});
 }
 poleFlip=-poleFlip;
 scn.push({t:'pole',side:poleFlip,z:z,sign:Math.random()<0.28,c:['#c23b2c','#2c7fc2','#d9a21b','#3aa35a'][Math.floor(Math.random()*4)]});
 if(bigCool<=0){
  var r=Math.random();
  if(r<0.10){scn.push({t:'bridge',z:z+0.1});bigCool=0.7;}
  else if(r<0.20){var sd2=Math.random()<0.5?-1:1,pl=0.9;scn.push({t:'platform',side:sd2,z:z,len:pl});
   for(k=0;k<3;k++)scn.push({t:'blk',z:z+0.1+k*0.3,len:0.03,lxa:sd2<0?-1.78:1.70,lxb:sd2<0?-1.70:1.78,h:1.5,c:'#6a6d73',seed:0,plain:true});
   bigCool=pl+0.6;}
 }
}

/* ---------- drawing helpers (everything is cut into thin slices along the track and drawn far to near, so neighbouring
   trains, ramps and walls always overlap each other correctly) ---------- */
function quad(a,b,c,d,col){g.fillStyle=col;g.beginPath();g.moveTo(a[0],a[1]);g.lineTo(b[0],b[1]);g.lineTo(c[0],c[1]);g.lineTo(d[0],d[1]);g.closePath();g.fill();g.strokeStyle=col;g.lineWidth=0.7;g.stroke();}
function slice(lxa,lxb,za,zb,h0,h1,c,front,win,wi){
 var sn=S(za),sf=S(zb),yn=Y(za),yf=Y(zb);
 var ynb=yn-h0*LS*sn,ynt=yn-h1*LS*sn,yfb=yf-h0*LS*sf,yft=yf-h1*LS*sf;
 var xnl=X(lxa,za),xnr=X(lxb,za),xfl=X(lxa,zb),xfr=X(lxb,zb);
 if(lxb<=0)quad([xnr,ynt],[xfr,yft],[xfr,yfb],[xnr,ynb],c.S);else if(lxa>=0)quad([xnl,ynt],[xfl,yft],[xfl,yfb],[xnl,ynb],c.S);
 if(win&&(wi%2===0)&&(lxb<=0||lxa>=0)){var ex=lxb<=0?lxb:lxa,f1=0.55,f2=0.8;quad([X(ex,za),ynb-(ynb-ynt)*f2],[X(ex,zb),yfb-(yfb-yft)*f2],[X(ex,zb),yfb-(yfb-yft)*f1],[X(ex,za),ynb-(ynb-ynt)*f1],'#a8c4e0');}
 quad([xnl,ynt],[xnr,ynt],[xfr,yft],[xfl,yft],c.T);
 if(front){g.fillStyle=c.F;g.fillRect(xnl,ynt,xnr-xnl,ynb-ynt);return {xl:xnl,xr:xnr,yt:ynt,yb:ynb};}
 return null;
}
function fadeA(z){return z>1.0?clamp((Z0-z)/0.5,0,1):1;}
function addOp(ops,k,z,f){var a=fadeA(z);if(a>0)ops.push({k:k,a:a,f:f});}
function pushBox(ops,lxa,lxb,za,zb,h0,h1,c,o){
 o=o||{};var step=o.step||0.12,pri=Math.abs((lxa+lxb)*0.5)*0.001,idx=0,z=za;
 while(z<zb-1e-6){
  var b=Math.min(z+step,zb);
  if(b>ZMIN){(function(a,bb,first,ix){var a2=Math.max(a,ZMIN);addOp(ops,(a2+bb)*0.5+pri,a2,function(){var r=slice(lxa,lxb,a2,bb,h0,h1,c,first&&a2===a,o.win,ix);if(r&&o.front)o.front(r);});})(z,b,idx===0,idx);}
  z+=step;idx++;
 }
}
var CTRAIN={F:'#3f5c80',S:'#2c3f5a',T:'#5a7392'},CTRAIN2={F:'#7d3b3b',S:'#582a2a',T:'#9a5252'};
function trainFront(r){
 var ww=(r.xr-r.xl)*0.34,wh=(r.yb-r.yt)*0.26;g.fillStyle='#cfe3f7';g.fillRect(r.xl+(r.xr-r.xl)*0.10,r.yt+(r.yb-r.yt)*0.14,ww,wh);g.fillRect(r.xr-(r.xr-r.xl)*0.10-ww,r.yt+(r.yb-r.yt)*0.14,ww,wh);
 g.fillStyle='#ffe9a0';var hl=(r.xr-r.xl)*0.12;g.fillRect(r.xl+(r.xr-r.xl)*0.12,r.yb-(r.yb-r.yt)*0.22,hl,hl*0.7);g.fillRect(r.xr-(r.xr-r.xl)*0.12-hl,r.yb-(r.yb-r.yt)*0.22,hl,hl*0.7);
 g.fillStyle='rgba(255,255,255,0.55)';g.fillRect(r.xl,r.yt+(r.yb-r.yt)*0.50,r.xr-r.xl,(r.yb-r.yt)*0.05);
}
function pushRamp(ops,it){
 var lx=it.lane-1,w1=0.43,z=it.z,zb=it.z+it.len,step=0.1,idx=0,pri=Math.abs(lx)*0.001;
 function hf(zz){return TH*clamp((zz-it.z)/it.len,0,1);}
 while(z<zb-1e-6){
  var b=Math.min(z+step,zb);
  if(b>ZMIN){(function(a,bb,ix){var a2=Math.max(a,ZMIN);addOp(ops,(a2+bb)*0.5+pri,a2,function(){
   var sn=S(a2),sf=S(bb),yn=Y(a2),yf=Y(bb),ynt=yn-hf(a2)*LS*sn,yft=yf-hf(bb)*LS*sf;
   var xnl=X(lx-w1,a2),xnr=X(lx+w1,a2),xfl=X(lx-w1,bb),xfr=X(lx+w1,bb);
   if(lx<0)quad([xnr,ynt],[xfr,yft],[xfr,yf],[xnr,yn],'#6b5a2a');else if(lx>0)quad([xnl,ynt],[xfl,yft],[xfl,yf],[xnl,yn],'#6b5a2a');
   quad([xnl,ynt],[xnr,ynt],[xfr,yft],[xfl,yft],ix%2?'#c9a43a':'#b8942e');});})(z,b,idx);}
  z+=step;idx++;
 }
}
function icon(k,x,y,s){
 g.save();g.translate(x,y);g.fillStyle='#fff';g.strokeStyle='#fff';
 if(k==='magnet'){g.lineWidth=s*0.55;g.beginPath();g.arc(0,s*0.05,s*0.62,0,Math.PI);g.stroke();g.fillRect(-s*0.9,-s*0.75,s*0.55,s*0.8);g.fillRect(s*0.35,-s*0.75,s*0.55,s*0.8);g.fillStyle='#d8d8d8';g.fillRect(-s*0.9,-s,s*0.55,s*0.3);g.fillRect(s*0.35,-s,s*0.55,s*0.3);}
 else if(k==='sneakers'){g.beginPath();g.moveTo(-s,-s*0.55);g.lineTo(-s*0.35,-s*0.55);g.lineTo(0,0);g.lineTo(s*0.75,s*0.15);g.quadraticCurveTo(s,s*0.25,s,s*0.55);g.lineTo(-s,s*0.55);g.closePath();g.fill();g.fillStyle='#2b6e33';g.fillRect(-s,s*0.62,s*2,s*0.22);}
 else if(k==='shield'){g.beginPath();g.moveTo(0,-s);g.lineTo(s*0.85,-s*0.6);g.quadraticCurveTo(s*0.8,s*0.55,0,s);g.quadraticCurveTo(-s*0.8,s*0.55,-s*0.85,-s*0.6);g.closePath();g.fill();}
 else{g.font='bold '+(s*1.7)+'px Consolas,Menlo,monospace';g.textAlign='center';g.textBaseline='middle';g.fillText('2x',0,s*0.1);g.textBaseline='alphabetic';}
 g.restore();
}
function drawSimple(it){
 var lx=it.lane-1,s=S(it.z),x=X(lx,it.z),y=Y(it.z),u=LS*s;
 if(it.t==='coin'){var cw=Math.abs(Math.cos(time*6+it.z*9))*0.13+0.03,cy=y-u*(0.5+(it.h||0));g.fillStyle='#ffc933';g.strokeStyle='#a87a10';g.lineWidth=Math.max(1,u*0.03);g.beginPath();g.ellipse(x,cy,u*cw,u*0.13,0,0,6.3);g.fill();g.stroke();g.fillStyle='rgba(255,255,255,0.7)';g.fillRect(x-u*cw*0.35,cy-u*0.07,Math.max(1,u*cw*0.25),u*0.08);}
 else if(it.t==='power'){var P=PW[it.kind],bob=Math.sin(time*5)*u*0.08,r=u*0.24,yy=y-u*0.55+bob;g.fillStyle='rgba(255,255,255,'+(0.16+0.08*Math.sin(time*8))+')';g.beginPath();g.arc(x,yy,r*1.5,0,6.3);g.fill();g.fillStyle=P.c;g.beginPath();g.arc(x,yy,r,0,6.3);g.fill();icon(it.kind,x,yy,r*0.62);}
 else if(it.t==='bar'){var w=u*0.8,h=u*0.40;g.fillStyle='#e9e9e0';g.fillRect(x-w/2,y-h,w,h*0.5);g.fillStyle='#d23c2d';for(var k2=0;k2<4;k2++)g.fillRect(x-w/2+k2*w/4,y-h,w/8,h*0.5);g.fillStyle='#555';g.fillRect(x-w/2,y-h,u*0.06,h);g.fillRect(x+w/2-u*0.06,y-h,u*0.06,h);}
 else if(it.t==='over'){var hh=u*0.72,th=u*0.22,pw0=u*0.08,xl=X(-1.5,it.z),xr=X(1.5,it.z);g.fillStyle='#555';g.fillRect(xl-pw0,y-hh-th,pw0,hh+th);g.fillRect(xr,y-hh-th,pw0,hh+th);g.fillStyle='#f2a31b';g.fillRect(xl-pw0,y-hh-th,xr-xl+2*pw0,th);g.fillStyle='#222';for(var k3=0;k3<12;k3++){var sx=xl-pw0+k3*(xr-xl+2*pw0)/12;g.fillRect(sx,y-hh-th,(xr-xl+2*pw0)/24,th);}}
 else if(it.t==='duck'){
  var dw=u*0.86,dh=u*0.78,bt=u*0.20,pw2=u*0.07;
  g.fillStyle='#4a4d52';g.fillRect(x-dw/2,y-dh-bt,pw2,dh+bt);g.fillRect(x+dw/2-pw2,y-dh-bt,pw2,dh+bt);
  g.fillStyle='#f2c21b';g.fillRect(x-dw/2,y-dh-bt,dw,bt);g.fillStyle='#222';var nn=10,sw=dw/nn;for(var k4=0;k4<nn;k4+=2)g.fillRect(x-dw/2+k4*sw,y-dh-bt,sw,bt);
  g.fillStyle='rgba(255,255,255,0.85)';g.beginPath();g.moveTo(x-u*0.12,y-dh-bt-u*0.22);g.lineTo(x+u*0.12,y-dh-bt-u*0.22);g.lineTo(x,y-dh-bt-u*0.06);g.closePath();g.fill();
 }
}
function drawPole(e){
 var lx=e.side*1.82,za=Math.max(e.z,ZMIN),zb=e.z+0.02;if(zb<=za)return;
 var r=slice(lx-0.03,lx+0.03,za,zb,0,1.7,{F:'#555',S:'#444',T:'#777'},true);
 if(r){var u=LS*S(za),cxp=(r.xl+r.xr)/2;
  if(e.sign){var sx=cxp+(e.side>0?0:-u*0.95);g.fillStyle=e.c;g.fillRect(sx,r.yt-u*0.55,u*0.95,u*0.5);g.fillStyle='rgba(255,255,255,0.6)';g.fillRect(sx+u*0.08,r.yt-u*0.47,u*0.55,u*0.06);g.fillRect(sx+u*0.08,r.yt-u*0.33,u*0.4,u*0.05);}
  else{g.fillStyle='#ffd27a';g.beginPath();g.arc(cxp+(e.side>0?-u*0.12:u*0.12),r.yt,Math.max(1.5,u*0.07),0,6.3);g.fill();}}
}
function buildScnOps(ops,e){
 if(e.t==='blk'){
  var c={F:e.c,S:sh(e.c,-16),T:sh(e.c,16)};
  pushBox(ops,e.lxa,e.lxb,e.z,e.z+e.len,0,e.h,c,{front:function(r){
   if(e.plain)return;var cols=3,rows=Math.max(2,Math.floor(e.h*2.2)),ww=(r.xr-r.xl)/(cols*2+1),hh=(r.yb-r.yt)/(rows*2+1);
   for(var ry=0;ry<rows;ry++)for(var rx=0;rx<cols;rx++){if(((e.seed>>(ry*cols+rx))&1)===0)continue;g.fillStyle='rgba(255,214,120,'+winA+')';g.fillRect(r.xl+ww*(1+rx*2),r.yt+hh*(1+ry*2),ww,hh);}}});
 }
 else if(e.t==='pole'){addOp(ops,e.z,e.z,function(){drawPole(e);});}
 else if(e.t==='platform'){
  var a=e.side<0?-2.4:1.5,b=e.side<0?-1.5:2.4;
  pushBox(ops,a,b,e.z,e.z+e.len,0,0.22,{F:'#8d8b80',S:'#6e6c63',T:'#a09e92'});
  addOp(ops,e.z+e.len*0.5,e.z,function(){var zn=Math.max(e.z,ZMIN),zf=e.z+e.len;if(zf>zn){var ed=e.side<0?-1.5:1.5;g.strokeStyle='#e8c62a';g.lineWidth=Math.max(1,LS*S(zn)*0.04);g.beginPath();g.moveTo(X(ed,zn),Y(zn)-0.22*LS*S(zn));g.lineTo(X(ed,zf),Y(zf)-0.22*LS*S(zf));g.stroke();}});
 }
 else if(e.t==='bridge'){
  var cs={F:'#6b6e73',S:sh('#6b6e73',-20),T:'#8a8d92'};
  pushBox(ops,-2.1,-1.9,e.z,e.z+0.05,0,4.0,cs,{step:1});pushBox(ops,1.9,2.1,e.z,e.z+0.05,0,4.0,cs,{step:1});
  pushBox(ops,-2.1,2.1,e.z,e.z+0.05,3.55,4.0,{F:'#7a7d83',S:sh('#7a7d83',-20),T:'#9a9da3'},{step:1,front:function(r){g.fillStyle='#f2a31b';var n=14,wd=(r.xr-r.xl)/n;for(var k=0;k<n;k+=2)g.fillRect(r.xl+k*wd,r.yt+(r.yb-r.yt)*0.35,wd,(r.yb-r.yt)*0.3);}});
 }
 else if(e.t==='gate'){
  addOp(ops,e.z+e.depth,e.z,function(){gateInside(e);});
  addOp(ops,e.z,e.z,function(){gateFront(e);});
 }
}
var GW=3.4,GH=1.95,GO=0.52;
function gateInside(e){
 var za=Math.max(e.z,ZMIN),zb=e.z+e.depth;if(zb<=za)return;
 for(var l=0;l<3;l++){if(e.st[l]==='closed')continue;
  var lx=l-1,xl=lx-GO,xr=lx+GO,sn=S(za),sf=S(zb),yn=Y(za),yf=Y(zb),ynt=yn-GH*LS*sn,yft=yf-GH*LS*sf;
  quad([X(xl,za),ynt],[X(xl,zb),yft],[X(xl,zb),yf],[X(xl,za),yn],'#1a1c21');
  quad([X(xr,za),ynt],[X(xr,zb),yft],[X(xr,zb),yf],[X(xr,za),yn],'#121418');
  quad([X(xl,za),ynt],[X(xr,za),ynt],[X(xr,zb),yft],[X(xl,zb),yft],'#22252b');
 }
}
function gateFront(e){
 if(e.z<ZMIN)return;
 var za=e.z,zb=e.z+e.depth,al=clamp((e.z+0.12)/0.14,0,1);if(al<=0)return;
 var prevA=g.globalAlpha;g.globalAlpha=prevA*al;
 var sn=S(za),yn=Y(za),u=LS*sn,i;
 quad([X(-GW,za),yn-3.4*u],[X(GW,za),yn-3.4*u],[X(GW,zb),Y(zb)-3.4*LS*S(zb)],[X(-GW,zb),Y(zb)-3.4*LS*S(zb)],'#9a9ea6');
 var xL=X(-GW,za),xR=X(GW,za),yT=yn-3.4*u;
 g.fillStyle='#7b7f87';g.beginPath();g.rect(xL,yT,xR-xL,yn-yT);
 for(i=0;i<3;i++){var lx=i-1;g.rect(X(lx-GO,za),yn-GH*u,2*GO*u,GH*u);}
 g.fill('evenodd');
 g.strokeStyle='rgba(0,0,0,0.18)';g.lineWidth=Math.max(1,u*0.02);
 for(i=1;i<7;i++){var yy=yn-i*0.5*u;g.beginPath();g.moveTo(xL,yy);g.lineTo(xR,yy);g.stroke();}
 for(i=0;i<3;i++){
  var lx2=i-1,ox=X(lx2-GO,za),ow=2*GO*u,oy=yn-GH*u;
  g.fillStyle='#f2c21b';g.fillRect(ox-u*0.04,oy-u*0.20,ow+u*0.08,u*0.20);g.fillStyle='#222';var nn=8,sw=(ow+u*0.08)/nn;for(var k=0;k<nn;k+=2)g.fillRect(ox-u*0.04+k*sw,oy-u*0.20,sw,u*0.20);
  if(e.st[i]==='closed'){
   g.fillStyle='#8a5a32';g.fillRect(ox,oy,ow,GH*u);g.strokeStyle='rgba(30,16,6,0.55)';g.lineWidth=Math.max(1,u*0.02);
   for(var s2=1;s2<10;s2++){g.beginPath();g.moveTo(ox,oy+s2*GH*u/10);g.lineTo(ox+ow,oy+s2*GH*u/10);g.stroke();}
   g.fillStyle='rgba(242,194,27,0.9)';g.beginPath();g.moveTo(ox,oy+GH*u*0.42);g.lineTo(ox+ow*0.5,oy+GH*u*0.18);g.lineTo(ox+ow,oy+GH*u*0.42);g.lineTo(ox+ow,oy+GH*u*0.56);g.lineTo(ox+ow*0.5,oy+GH*u*0.32);g.lineTo(ox,oy+GH*u*0.56);g.closePath();g.fill();
   g.strokeStyle='#5a3a1e';g.lineWidth=Math.max(1.5,u*0.05);g.strokeRect(ox,oy,ow,GH*u);
  }
 }
 g.globalAlpha=prevA;
}
function buildOps(){
 var ops=[],i,it;
 for(i=0;i<items.length;i++){it=items[i];
  if(it.block)continue;
  if(it.t==='train')pushBox(ops,it.lane-1-0.43,it.lane-1+0.43,it.z,it.z+it.len,0,TH,it.red?CTRAIN2:CTRAIN,{win:true,front:trainFront});
  else if(it.t==='ramp')pushRamp(ops,it);
  else if(it.z>=ZMIN)(function(q){addOp(ops,q.z,q.z,function(){drawSimple(q);});})(it);
 }
 for(i=0;i<scn.length;i++)buildScnOps(ops,scn[i]);
 ops.sort(function(a,b){return b.k-a.k;});
 return ops;
}

/* ---------- gameplay ---------- */
function reset(){
 mode='play';paused=false;countdown=0;score=0;coins=0;speed=SPEED0;speedMul=1;dist=0;lane=1;prevLane=1;laneX=1;pos=0;vel=0;slide=0;supH=0;items=[];scn=[];msgs=[];parts=[];
 nextRow=0.5;safe=1;shake=0;stumble=0;invuln=0;gap=1;copX=1;copT=0;tun=0;bigCool=0.8;nextScn=0.3;sfxChase=false;lastPower=time-8;newBest=false;
 pw.magnet=0;pw.sneakers=0;pw.shield=0;pw.x2=0;
 sndStop('slowed');sndStop('enrique');if(ac)ac.resume();
 for(var z=0.05;z<Z0;z+=0.22)spawnScn(z);
 dirty=true;
}
function ended(){
 mode='over';overAt=time;shake=0.5;copT=0;sfxChase=false;pw.magnet=0;pw.sneakers=0;pw.shield=0;pw.x2=0;
 sndStop('enrique');sndStart('slowed',false,0.95);
 if(Math.floor(score)>best){best=Math.floor(score);newBest=true;post('best:'+best);}
}
function trip(){stumble=0.9;invuln=0.9;gap-=0.55;shake=0.3;msg('TRIPPED!','#ffb02e');sfxChase=true;if(!snd.enrique)sndStart('enrique',false,0.95);if(gap<=0.12){gap=0;ended();}}
function closeCall(){score+=25;gap=Math.max(0,gap-0.08);msg('CLOSE CALL!','#ffe45c');if(gap<=0.12){gap=0;ended();}}
function burst(x,y,c,n){for(var i=0;i<n;i++){var a=Math.random()*6.28,s=30+Math.random()*100;parts.push({x:x,y:y,vx:Math.cos(a)*s,vy:Math.sin(a)*s-40,life:0.4+Math.random()*0.35,c:c});}}
function takePower(k){pw[k]=PW[k].dur;msg(PW[k].n+'!',PW[k].c);burst(cx+(laneX-1)*LS,py-pos*LS-LS*0.5,PW[k].c,16);}
/* you hit something that would trip you: the shield takes it instead, if you have one */
function hurt(){
 if(pw.shield>0){pw.shield=0;invuln=1.0;msg('SHIELD SAVED YOU','#6db0ff');burst(cx+(laneX-1)*LS,py-pos*LS-LS*0.5,'#6db0ff',18);return;}
 trip();
}
/* you hit something head-on: with a shield you smash through it, otherwise you are caught */
function wreck(i){
 var it=items[i];
 if(pw.shield>0){
  pw.shield=0;invuln=1.0;shake=0.35;msg('SHIELD SMASH!','#6db0ff');burst(cx+(laneX-1)*LS,py-pos*LS-LS*0.7,'#6db0ff',26);
  items.splice(i,1);
  if(it.block)for(var k=0;k<scn.length;k++)if(scn[k].t==='gate'&&Math.abs(scn[k].z-it.z)<0.02)scn[k].st[it.lane]='open';
  return false;
 }
 ended();return true;
}
function support(){
 var h=0;
 for(var i=0;i<items.length;i++){var it=items[i];if(it.t!=='train'&&it.t!=='ramp')continue;if(it.block)continue;if(Math.abs(laneX-it.lane)>=0.52)continue;
  if(it.z<=0.005&&it.z+it.len>=-0.005){
   var th=it.th||TH,gh=it.t==='train'?th:TH*Math.min(1,(0-it.z)/it.len);
   if(it.t==='train'&&pos<gh-0.3)continue;
   if(gh>h)h=gh;}}
 return h;
}
function rampH(it){return TH*Math.min(1,Math.max(0,(0-it.z)/it.len));}
function addGate(z,st){
 var depth=0.30,l;scn.push({t:'gate',z:z,depth:depth,st:st});
 for(l=0;l<3;l++){
  if(st[l]==='closed')items.push({t:'train',lane:l,z:z,len:depth,th:3.4,block:1});
  else if(st[l]==='train')items.push({t:'train',lane:l,z:z+0.06,len:0.62,red:Math.random()<0.5});
 }
}
function spawnGate(){
 var st=[],i,r;
 for(i=0;i<3;i++){r=Math.random();st.push(r<0.34?'open':(r<0.67?'train':'closed'));}
 var cand=[];for(i=0;i<3;i++)if(Math.abs(i-safe)<=1)cand.push(i);
 var ok=false;for(i=0;i<cand.length;i++)if(st[cand[i]]==='open')ok=true;
 if(!ok)st[cand[Math.floor(Math.random()*cand.length)]]='open';
 var op=[];for(i=0;i<3;i++)if(st[i]==='open')op.push(i);
 safe=op[Math.floor(Math.random()*op.length)];
 addGate(Z0,st);
 nextRow=0.3+0.7+0.5;
}
function spawnRow(){
 var r=Math.random(),i,l,ls;
 if(dist>2&&time-lastPower>13&&Math.random()<0.22){var kinds=['magnet','sneakers','shield','x2'];items.push({t:'power',kind:kinds[Math.floor(Math.random()*4)],lane:safe,z:Z0,h:0});lastPower=time;nextRow=0.55;return;}
 if(r<0.18){ls=shuf([0,1,2]).slice(0,Math.random()<0.5?2:1);for(i=0;i<ls.length;i++)items.push({t:'bar',lane:ls[i],z:Z0});}
 else if(r<0.32){
  var c=[];for(i=0;i<3;i++)if(Math.abs(i-safe)<=1)c.push(i);
  var f=c[Math.floor(Math.random()*c.length)],others=[],any=false;for(i=0;i<3;i++)if(i!==f)others.push(i);
  for(i=0;i<others.length;i++){if(Math.random()<0.8){items.push({t:'train',lane:others[i],z:Z0,len:0.34,red:Math.random()<0.3});any=true;}}
  if(!any)items.push({t:'train',lane:others[0],z:Z0,len:0.34});
  safe=f;}
 else if(r<0.40){items.push({t:'over',lane:1,z:Z0});}
 else if(r<0.50){ls=shuf([0,1,2]).slice(0,Math.random()<0.4?2:1);for(i=0;i<ls.length;i++)items.push({t:'duck',lane:ls[i],z:Z0});}
 else if(r<0.64){
  var L=Math.floor(Math.random()*3),rl=0.28,tl=0.8+Math.random()*0.5;
  items.push({t:'ramp',lane:L,z:Z0,len:rl});items.push({t:'train',lane:L,z:Z0+rl,len:tl});
  for(i=0;i<4;i++)items.push({t:'coin',lane:L,z:Z0+rl+0.12+i*0.12,h:TH});
  var N=L===1?(Math.random()<0.5?0:2):1,nz=Z0+rl+tl*0.45+0.12+Math.random()*0.1,nl=0.7+Math.random()*0.4;
  items.push({t:'train',lane:N,z:nz,len:nl,red:true});
  for(i=0;i<3;i++)items.push({t:'coin',lane:N,z:nz+0.12+i*0.14,h:TH});
  safe=[0,1,2].filter(function(x){return x!==L&&x!==N;})[0];
  nextRow=Math.max(tl+rl,nz-Z0+nl)+0.6;return;}
 else if(r<0.80){spawnGate();return;}
 else{l=Math.floor(Math.random()*3);for(i=0;i<5;i++)items.push({t:'coin',lane:l,z:Z0+i*0.09,h:0});}
 nextRow=0.52+Math.random()*0.35;
}
function update(dt){
 time+=dt;
 if(shake>0)shake=Math.max(0,shake-dt);
 for(var m=msgs.length-1;m>=0;m--){msgs[m].life-=dt;if(msgs[m].life<=0)msgs.splice(m,1);}
 for(var q=parts.length-1;q>=0;q--){var p=parts[q];p.life-=dt;p.x+=p.vx*dt;p.y+=p.vy*dt;p.vy+=240*dt;if(p.life<=0)parts.splice(q,1);}
 if(countdown>0){countdown-=dt;if(countdown<=0){countdown=0;paused=false;if(ac)ac.resume();}return;}
 if(mode==='over'){
  gap=Math.max(0,gap-dt*2.2);if(gap<=0.02)copT+=dt;
  copX+=(laneX-copX)*Math.min(1,dt*8);
  if(pos>supH||vel>0){vel-=G*dt;pos+=vel*dt;if(pos<=supH){pos=supH;vel=0;}}
  return;}
 if(mode!=='play'||paused)return;
 var i,it,k;
 for(k in pw)if(pw[k]>0)pw[k]=Math.max(0,pw[k]-dt);
 var mult=pw.x2>0?2:1;
 stumble=Math.max(0,stumble-dt);invuln=Math.max(0,invuln-dt);
 speedMul=stumble>0?0.55+0.45*(1-stumble/0.9):1;
 speed=Math.min(SPEEDMAX,SPEED0+dist*0.0035)*speedMul;
 dist+=speed*dt;score+=speed*dt*10*mult;gap=Math.min(1,gap+dt*0.07);
 laneX+=(lane-laneX)*Math.min(1,dt*14);copX+=(laneX-copX)*Math.min(1,dt*3);
 if(slide>0)slide-=dt;
 for(i=0;i<items.length;i++)items[i].z-=speed*dt;
 for(i=0;i<scn.length;i++)scn[i].z-=speed*dt;
 for(i=scn.length-1;i>=0;i--){var e=scn[i];if(e.z+(e.len||e.depth||0.1)<ZMIN-0.1)scn.splice(i,1);}
 bigCool-=speed*dt;
 nextRow-=speed*dt;if(nextRow<=0)spawnRow();
 nextScn-=speed*dt;if(nextScn<=0){spawnScn(Z0+0.05);nextScn=0.2+Math.random()*0.16;}
 /* collisions (using where the player was this frame) */
 for(i=items.length-1;i>=0;i--){
  it=items[i];
  if(it.t==='coin'&&pw.magnet>0&&it.z<0.5&&it.z>-0.1){var mm=Math.min(1,dt*10);it.lane+=(laneX-it.lane)*mm;it.h=(it.h||0)+(pos-(it.h||0))*mm;}
  var d=Math.abs(laneX-it.lane),near=d<0.6,len=it.len||0,inW=it.z<0.04&&it.z>-0.04,th=it.th||TH;
  if(it.t==='coin'){if(it.z<0.05&&it.z>-0.07&&near&&Math.abs(pos-(it.h||0))<0.7){coins++;score+=10*mult;gap=Math.min(1,gap+0.012);burst(cx+(it.lane-1)*LS,py-((it.h||0)+0.5)*LS,'#ffd54a',5);items.splice(i,1);continue;}}
  else if(it.t==='power'){if(it.z<0.06&&it.z>-0.08&&near&&pos<1.1){items.splice(i,1);takePower(it.kind);continue;}}
  else if(it.t==='bar'){
   if(inW&&near){it.minPos=Math.min(it.minPos===undefined?9:it.minPos,pos);if(pos<0.45&&invuln<=0&&!it.hit){it.hit=1;hurt();if(mode==='over')return;}}
   if(it.z<=-0.04&&!it.hit&&!it.cc&&it.minPos!==undefined&&it.minPos<0.62){it.cc=1;closeCall();if(mode==='over')return;}
  }
  else if(it.t==='over'||it.t==='duck'){
   var okS=slide>0&&pos<0.3;
   if(inW&&(it.t==='over'||near)){
    if(!okS&&invuln<=0&&!it.hit){it.hit=1;hurt();if(mode==='over')return;}
    else if(okS&&slide<0.14&&!it.cc&&!it.hit){it.cc=1;closeCall();if(mode==='over')return;}
   }
  }
  else if(it.t==='train'){
   if(it.z<=0.005&&it.z>-0.04&&near&&pos<th-0.12&&!it.hit){if(wreck(i))return;continue;}
   if(inW&&d>=0.6&&d<0.85&&!it.cc&&pos<th-0.12){it.cc=1;closeCall();if(mode==='over')return;}
   if(it.z<=-0.04&&it.z+len>=0&&near&&pos<th-0.25){lane=prevLane;if(invuln<=0&&!it.hit){it.hit=1;hurt();if(mode==='over')return;}}
  }
  else if(it.t==='ramp'){
   if(it.z<=0&&it.z+len>=0&&near&&pos<rampH(it)-0.25){lane=prevLane;if(invuln<=0&&!it.hit){it.hit=1;hurt();if(mode==='over')return;}}
  }
  if(it.z+len<ZMIN-0.02)items.splice(i,1);
 }
 supH=support();
 if(pos>supH+0.0005||vel>0){vel-=G*dt;pos+=vel*dt;if(pos<=supH&&vel<=0){pos=supH;vel=0;}}
 else{pos=supH;vel=0;}
 var inT=false;for(i=0;i<scn.length;i++){if(scn[i].t==='gate'&&scn[i].z<=0&&scn[i].z+scn[i].depth>=0){inT=true;break;}}
 tun+=((inT?1:0)-tun)*Math.min(1,dt*8);
}

/* ---------- sky: dusk, night, dawn, day as you get further (drawn once per shade, then reused) ---------- */
var sky=[],stars=[],sd=7,winA=0.75;function rnd(){sd=(sd*16807)%2147483647;return sd/2147483647;}
for(var i=0;i<26;i++){sky.push({x:i/26,w:0.03+rnd()*0.06,h:0.04+rnd()*0.10,c:rnd(),l:rnd()});}
for(var j=0;j<40;j++){stars.push({x:rnd(),y:rnd()*0.6,r:rnd()});}
var TOD=[{t:[10,26,42],h:[217,138,74],a:[27,36,51],b:[35,45,63],gr:[20,23,15],w:0.75,s:0},
         {t:[3,6,16],h:[26,40,78],a:[10,14,26],b:[15,20,34],gr:[12,14,12],w:1,s:1},
         {t:[32,44,84],h:[236,158,116],a:[42,46,70],b:[54,58,82],gr:[22,24,18],w:0.45,s:0.2},
         {t:[66,132,196],h:[190,222,240],a:[92,108,136],b:[112,130,156],gr:[38,46,28],w:0.12,s:0}];
function mixc(a,b,t){return 'rgb('+Math.round(a[0]+(b[0]-a[0])*t)+','+Math.round(a[1]+(b[1]-a[1])*t)+','+Math.round(a[2]+(b[2]-a[2])*t)+')';}
var groundCol='#14170f';
function drawBg(){
 var p=(dist/14)%4,i0=Math.floor(p),f=clamp((p-i0-0.65)/0.35,0,1),qf=Math.round(f*24),key=i0*100+qf;
 var sh0=Math.ceil(hor+1);
 if(key!==skyKey){
  skyKey=key;var A=TOD[i0],B=TOD[(i0+1)%4],t=qf/24,k;
  skyC.width=Math.round(W*dpr);skyC.height=Math.round(sh0*dpr);var s=skyC.getContext('2d');s.setTransform(dpr,0,0,dpr,0,0);
  var gr=s.createLinearGradient(0,0,0,hor);gr.addColorStop(0,mixc(A.t,B.t,t));gr.addColorStop(1,mixc(A.h,B.h,t));s.fillStyle=gr;s.fillRect(0,0,W,sh0);
  var sa=A.s+(B.s-A.s)*t;if(sa>0.02){for(k=0;k<stars.length;k++){s.fillStyle='rgba(255,255,255,'+(sa*(0.35+stars[k].r*0.65))+')';s.fillRect(stars[k].x*W,stars[k].y*hor,1.5,1.5);}}
  winA=A.w+(B.w-A.w)*t;
  for(k=0;k<sky.length;k++){var b=sky[k];s.fillStyle=b.c<0.5?mixc(A.a,B.a,t):mixc(A.b,B.b,t);s.fillRect(b.x*W,hor-b.h*H,b.w*W,b.h*H);
   if(winA>0.3&&b.l>0.35){s.fillStyle='rgba(255,214,120,'+(winA*0.5)+')';for(var wy=0;wy<3;wy++)s.fillRect(b.x*W+b.w*W*0.3,hor-b.h*H+5+wy*7,2,2);}}
  groundCol=mixc(A.gr,B.gr,t);
 }
 g.drawImage(skyC,0,0,skyC.width,skyC.height,0,0,W,sh0);
 g.fillStyle=groundCol;g.fillRect(0,hor,W,H-hor);
}
function drawTrack(){
 var zn=ZMIN,zf=3,i;
 quad([X(-1.5,zf),Y(zf)],[X(1.5,zf),Y(zf)],[X(1.5,zn),Y(zn)],[X(-1.5,zn),Y(zn)],'#383a31');
 for(i=-1;i<15;i++){var z=i*0.22-(dist%0.22);if(z<zn||z>zf)continue;g.strokeStyle='rgba(80,62,44,0.95)';g.lineWidth=Math.max(1,4*S(z));g.beginPath();g.moveTo(X(-1.7,z),Y(z));g.lineTo(X(1.7,z),Y(z));g.stroke();}
 var ls=[-0.5,0.5,-1.5,1.5];
 for(i=0;i<4;i++){g.strokeStyle=i<2?'rgba(255,255,255,0.20)':'#9aa084';g.lineWidth=i<2?1.2:Math.max(2,LS*0.035);g.beginPath();g.moveTo(X(ls[i],zf),Y(zf));g.lineTo(X(ls[i],zn),Y(zn));g.stroke();}
}
function drawSpeed(){
 if(mode!=='play'||speed<1.05)return;
 g.strokeStyle='rgba(255,255,255,'+clamp((speed-1.05)*0.55,0,0.22)+')';g.lineWidth=1.5;
 for(var i=0;i<10;i++){var side=i%2?1:-1,yy=((time*700+i*173)%(H-hor))+hor,xx=W/2+side*(W*0.40+((i*37)%9)*W*0.012);g.beginPath();g.moveTo(xx,yy);g.lineTo(xx+side*5,yy+H*0.05);g.stroke();}
}
function drawPlayer(){
 var over=mode==='over',x=cx+(laneX-1)*LS,ground=py-supH*LS,yb=py-pos*LS,sl=slide>0,w=LS*0.30,h=LS*(sl?0.42:0.86),ph=time*13,sh2=clamp(1-(pos-supH)*0.3,0.4,1);
 g.fillStyle='rgba(0,0,0,0.35)';g.beginPath();g.ellipse(x,ground+2,LS*0.2*sh2,LS*0.06*sh2,0,0,6.3);g.fill();
 if(over){
  var sq=1;if(gap<=0.02){var jp=Math.abs(Math.sin(copT*8));if(jp<0.12)sq=0.82;}
  g.save();g.translate(x,yb);g.scale(1,sq);
  g.fillStyle='#2c3a4d';g.fillRect(-w*1.2,-LS*0.10,w*1.0,LS*0.10);
  g.fillStyle='#5d8f3a';g.beginPath();g.ellipse(0,-LS*0.14,w*0.75,LS*0.13,0,0,6.3);g.fill();
  g.fillStyle='#e8c39a';g.beginPath();g.arc(w*0.85,-LS*0.14,LS*0.11,0,6.3);g.fill();
  g.fillStyle='#ee7a1c';g.beginPath();g.arc(w*0.85,-LS*0.17,LS*0.12,3.14,6.28);g.fill();
  g.restore();return;
 }
 if(pw.shield>0){g.fillStyle='#2f6fc0';g.beginPath();g.ellipse(x,yb+LS*0.02,LS*0.27,LS*0.07,0,0,6.3);g.fill();g.fillStyle='rgba(120,190,255,0.8)';g.fillRect(x-LS*0.2,yb+LS*0.06,LS*0.4,2);}
 if(invuln>0&&Math.floor(time*16)%2===0)g.globalAlpha=0.45;
 g.save();
 if(stumble>0){g.translate(x,yb);g.rotate(Math.sin(time*34)*0.10);g.translate(-x,-yb);}
 var air=pos>supH+0.02,shoe=pw.sneakers>0?'#57e06a':'#e8e8e8';
 if(!sl){
  var sw=air?0.3:Math.sin(ph)*0.35,bob=air?0:Math.abs(Math.cos(ph))*LS*0.02;g.strokeStyle='#2c3a4d';g.lineWidth=LS*0.09;g.lineCap='round';
  var f1x=x-w*0.2+sw*LS*0.3,f2x=x+w*0.2-sw*LS*0.3;
  g.beginPath();g.moveTo(x-w*0.2,yb-h*0.38);g.lineTo(f1x,yb-LS*0.02);g.stroke();
  g.beginPath();g.moveTo(x+w*0.2,yb-h*0.38);g.lineTo(f2x,yb-LS*0.02);g.stroke();
  g.fillStyle=shoe;g.beginPath();g.ellipse(f1x,yb-LS*0.01,LS*0.065,LS*0.04,0,0,6.3);g.fill();g.beginPath();g.ellipse(f2x,yb-LS*0.01,LS*0.065,LS*0.04,0,0,6.3);g.fill();
  var tb=yb-bob;
  g.fillStyle='#5d8f3a';g.beginPath();g.moveTo(x-w/2,tb-h*0.38);g.lineTo(x-w*0.42,tb-h*0.92);g.lineTo(x+w*0.42,tb-h*0.92);g.lineTo(x+w/2,tb-h*0.38);g.closePath();g.fill();
  g.fillStyle='#e8c39a';g.lineWidth=LS*0.05;g.strokeStyle='#4d7a30';var arm=air?-0.3:sw;
  g.beginPath();g.moveTo(x-w*0.42,tb-h*0.84);g.lineTo(x-w*0.7,tb-h*0.56+arm*LS*0.22);g.stroke();g.beginPath();g.moveTo(x+w*0.42,tb-h*0.84);g.lineTo(x+w*0.7,tb-h*0.56-arm*LS*0.22);g.stroke();
  g.fillStyle='#34522a';g.fillRect(x-w*0.27,tb-h*0.84,w*0.54,h*0.34);g.fillStyle='#e0a22b';g.fillRect(x-w*0.27,tb-h*0.70,w*0.54,h*0.04);
  g.fillStyle='#e8c39a';g.beginPath();g.arc(x,tb-h*1.04,LS*0.13,0,6.3);g.fill();
  g.fillStyle='#ee7a1c';g.beginPath();g.arc(x,tb-h*1.06,LS*0.14,3.14,6.28);g.fill();g.fillRect(x-LS*0.14,tb-h*1.07,LS*0.30,LS*0.035);
 }else{
  g.fillStyle='#2c3a4d';g.fillRect(x-w*0.6,yb-h*0.30,w*1.3,h*0.30);
  g.fillStyle='#5d8f3a';g.beginPath();g.ellipse(x,yb-h*0.62,w*0.62,h*0.34,0,0,6.3);g.fill();
  g.fillStyle='#e8c39a';g.beginPath();g.arc(x+w*0.55,yb-h*0.66,LS*0.11,0,6.3);g.fill();
  g.fillStyle='#ee7a1c';g.beginPath();g.arc(x+w*0.55,yb-h*0.70,LS*0.12,3.14,6.28);g.fill();
  g.fillStyle=shoe;g.beginPath();g.ellipse(x-w*0.6,yb-h*0.15,LS*0.065,LS*0.045,0,0,6.3);g.fill();
 }
 g.restore();g.globalAlpha=1;
 var mid=yb-LS*(sl?0.2:0.55);
 if(pw.shield>0){g.strokeStyle='rgba(120,190,255,'+(0.5+0.25*Math.sin(time*9))+')';g.lineWidth=2;g.beginPath();g.ellipse(x,mid,LS*0.36,LS*(sl?0.3:0.58),0,0,6.3);g.stroke();}
 if(pw.magnet>0){g.strokeStyle='rgba(255,90,80,0.55)';g.lineWidth=1.5;for(var m=0;m<2;m++){var rr=LS*(0.35+((time*0.9+m*0.5)%1)*0.4);g.beginPath();g.arc(x,mid,rr,Math.PI*1.15,Math.PI*1.85);g.stroke();}}
}
function drawCop(){
 var gn=Math.min(1,gap/0.9);
 if(mode!=='over'&&gn>=1)return;
 var size=LS*(1.0+0.45*(1-gn)),x=cx+(copX-1)*LS,over=mode==='over';
 var base=over?(py-supH*LS+LS*0.04):(py+LS*0.35+(H+size*1.4-(py+LS*0.35))*gn);
 var jp=0,sq=1;
 if(over&&gap<=0.02){var ph0=Math.abs(Math.sin(copT*8));jp=ph0*LS*0.85;if(ph0<0.12)sq=0.85;}
 var bob=over?0:Math.abs(Math.sin(time*13))*size*0.06,yb=base-jp-bob,sw=Math.sin(time*13)*0.35;
 g.fillStyle='rgba(0,0,0,0.3)';g.beginPath();g.ellipse(x,base+2,size*0.3,size*0.08,0,0,6.3);g.fill();
 g.save();g.translate(x,yb);g.scale(1,sq);
 g.strokeStyle='#1b2a4a';g.lineWidth=size*0.11;g.lineCap='round';
 g.beginPath();g.moveTo(-size*0.1,-size*0.30);g.lineTo(-size*0.1+(over?0:sw)*size*0.28,0);g.stroke();
 g.beginPath();g.moveTo(size*0.1,-size*0.30);g.lineTo(size*0.1-(over?0:sw)*size*0.28,0);g.stroke();
 g.fillStyle='#23407a';g.beginPath();g.moveTo(-size*0.27,-size*0.28);g.lineTo(-size*0.22,-size*0.74);g.lineTo(size*0.22,-size*0.74);g.lineTo(size*0.27,-size*0.28);g.closePath();g.fill();
 g.fillStyle='#e0b020';g.beginPath();g.arc(size*0.1,-size*0.58,size*0.045,0,6.3);g.fill();
 g.strokeStyle='#23407a';g.lineWidth=size*0.09;
 if(over){g.beginPath();g.moveTo(-size*0.25,-size*0.68);g.lineTo(-size*0.40,-size*0.98);g.stroke();g.beginPath();g.moveTo(size*0.25,-size*0.68);g.lineTo(size*0.40,-size*0.98);g.stroke();}
 else{g.beginPath();g.moveTo(-size*0.25,-size*0.68);g.lineTo(-size*0.34,-size*0.45+sw*size*0.15);g.stroke();g.beginPath();g.moveTo(size*0.25,-size*0.68);g.lineTo(size*0.34,-size*0.45-sw*size*0.15);g.stroke();}
 var r=size*0.34,hy=-size*0.74-r*0.78;
 g.save();g.beginPath();g.arc(0,hy,r,0,6.3);g.clip();
 if(COP.complete&&COP.naturalWidth>0)g.drawImage(COP,-r,hy-r,r*2,r*2);else{g.fillStyle='#d9a877';g.fillRect(-r,hy-r,r*2,r*2);}
 g.restore();
 g.strokeStyle='#10141c';g.lineWidth=Math.max(1.5,size*0.03);g.beginPath();g.arc(0,hy,r,0,6.3);g.stroke();
 g.fillStyle='#16264a';g.beginPath();g.ellipse(0,hy-r*0.82,r*0.82,r*0.34,0,3.14,6.28);g.fill();g.fillRect(-r*0.9,hy-r*0.86,r*1.8,r*0.12);
 g.fillStyle='#e0b020';g.beginPath();g.arc(0,hy-r*1.04,r*0.1,0,6.3);g.fill();
 g.restore();
}

/* ---------- on-screen controls: speaker (mute), volume meter, pause ---------- */
function ui(){var u=clamp(W*0.068,18,28);return {u:u,sx:8,sy:H-u-8,mx:8+u+7,mw:Math.min(W*0.30,u*4.4),px:W-u-8};}
function roundRect(x,y,w,h,r){g.beginPath();g.moveTo(x+r,y);g.arcTo(x+w,y,x+w,y+h,r);g.arcTo(x+w,y+h,x,y+h,r);g.arcTo(x,y+h,x,y,r);g.arcTo(x,y,x+w,y,r);g.closePath();}
function drawControls(){
 var r=ui(),u=r.u,i;
 g.fillStyle='rgba(8,10,6,0.62)';roundRect(r.sx-4,r.sy-4,r.mx+r.mw+4-(r.sx-4),u+8,6);g.fill();roundRect(r.px-4,r.sy-4,u+8,u+8,6);g.fill();
 var x=r.sx,y=r.sy,col=muted?'#c9654f':'#d7e2c4';
 g.fillStyle=col;g.fillRect(x+u*0.08,y+u*0.34,u*0.2,u*0.32);g.beginPath();g.moveTo(x+u*0.28,y+u*0.34);g.lineTo(x+u*0.52,y+u*0.14);g.lineTo(x+u*0.52,y+u*0.86);g.lineTo(x+u*0.28,y+u*0.66);g.closePath();g.fill();
 g.strokeStyle=col;g.lineWidth=Math.max(1.4,u*0.08);g.lineCap='round';
 if(muted){g.beginPath();g.moveTo(x+u*0.62,y+u*0.36);g.lineTo(x+u*0.92,y+u*0.66);g.moveTo(x+u*0.92,y+u*0.36);g.lineTo(x+u*0.62,y+u*0.66);g.stroke();}
 else{if(vol>0){g.beginPath();g.arc(x+u*0.52,y+u*0.5,u*0.18,-0.8,0.8);g.stroke();}if(vol>40){g.beginPath();g.arc(x+u*0.52,y+u*0.5,u*0.34,-0.8,0.8);g.stroke();}}
 var n=10,gp=2,bw=(r.mw-(n-1)*gp)/n;
 for(i=0;i<n;i++){var bh=u*(0.28+0.72*i/(n-1)),on=!muted&&vol>i*100/n+0.5;g.fillStyle=on?(i<7?'#9bd45a':(i<9?'#ffc933':'#e0663f')):'rgba(255,255,255,0.18)';g.fillRect(r.mx+i*(bw+gp),y+u-bh,bw,bh);}
 g.fillStyle='#d7e2c4';var px=r.px;
 if(paused&&mode==='play'){g.beginPath();g.moveTo(px+u*0.28,y+u*0.18);g.lineTo(px+u*0.82,y+u*0.5);g.lineTo(px+u*0.28,y+u*0.82);g.closePath();g.fill();}
 else{g.fillRect(px+u*0.24,y+u*0.2,u*0.18,u*0.6);g.fillRect(px+u*0.58,y+u*0.2,u*0.18,u*0.6);}
}
function hit(x,y){var r=ui();if(y<r.sy-6)return '';if(x<=r.sx+r.u+3)return 'mute';if(x<=r.mx+r.mw+6)return 'meter';if(x>=r.px-6)return 'pause';return '';}

function txt(t,y,px,col,al){g.font='bold '+px+'px Consolas,Menlo,monospace';g.textAlign=al||'center';g.fillStyle=col||'#e6efd6';var x=(al==='left')?8:((al==='right')?W-8:W/2);g.lineWidth=Math.max(2,px*0.16);g.strokeStyle='rgba(0,0,0,0.55)';g.lineJoin='round';g.strokeText(t,x,y);g.fillText(t,x,y);}
function shade(a){g.fillStyle='rgba(8,10,6,'+a+')';g.fillRect(0,0,W,H);}
function drawHud(fs){
 var i;
 txt('SCORE '+Math.floor(score),fs+6,fs,'#e6efd6','left');txt('COINS '+coins,fs+6,fs,'#ffc933','right');
 if(mode==='play'){
  txt('BEST '+Math.max(best,Math.floor(score)),fs*2+8,fs*0.72,'#a9b894','right');
  var y=fs*2.0+4;
  var keys=['magnet','sneakers','shield','x2'];
  for(i=0;i<4;i++){var k=keys[i];if(pw[k]<=0)continue;var P=PW[k],h=fs*1.25,w=fs*4.2;
   g.fillStyle='rgba(8,10,6,0.6)';roundRect(8,y,w,h,5);g.fill();g.fillStyle=P.c;g.beginPath();g.arc(8+h/2,y+h/2,h*0.42,0,6.3);g.fill();icon(k,8+h/2,y+h/2,h*0.26);
   g.fillStyle='rgba(255,255,255,0.2)';g.fillRect(8+h+3,y+h*0.38,w-h-8,h*0.24);g.fillStyle=P.c;g.fillRect(8+h+3,y+h*0.38,(w-h-8)*clamp(pw[k]/P.dur,0,1),h*0.24);y+=h+4;}
  if(gap<0.9){var bw=W*0.28;g.fillStyle='rgba(0,0,0,0.45)';g.fillRect(W/2-bw/2,fs*2.1,bw,6);g.fillStyle=gap<0.35?'#e0553f':'#ffb02e';g.fillRect(W/2-bw/2,fs*2.1,bw*clamp(gap/0.9,0,1),6);}
 }
}
function runOp(o){g.globalAlpha=o.a;o.f();g.globalAlpha=1;}
function draw(){
 g.save();if(shake>0){g.translate((Math.random()-0.5)*shake*14,(Math.random()-0.5)*shake*14);}
 drawBg();drawTrack();
 var ops=buildOps(),i;
 for(i=0;i<ops.length;i++)if(ops[i].k>0)runOp(ops[i]);
 drawPlayer();
 for(i=0;i<ops.length;i++)if(ops[i].k<=0)runOp(ops[i]);
 drawCop();
 for(i=0;i<parts.length;i++){var p=parts[i];g.globalAlpha=clamp(p.life*2.5,0,1);g.fillStyle=p.c;g.fillRect(p.x-2,p.y-2,4,4);}g.globalAlpha=1;
 g.restore();
 drawSpeed();
 if(tun>0.02){g.fillStyle='rgba(0,0,0,'+(tun*0.38)+')';g.fillRect(0,0,W,H);}
 var fs=Math.max(11,Math.min(22,W*0.052));
 drawHud(fs);
 /* newest message on top, older ones stacked under it */
 var my=H*0.22;for(i=msgs.length-1;i>=0&&mode==='play';i--){var m=msgs[i],mf=Math.max(13,Math.min(W*0.08,W*1.5/Math.max(8,m.t.length)));g.globalAlpha=clamp(m.life*2,0,1);txt(m.t,my-(1.1-m.life)*8,mf,m.c);g.globalAlpha=1;my+=mf*1.2;}
 if(mode==='title'){
  shade(0.62);var tz=Math.max(16,W*0.115);txt('RUN AWAY',H*0.17,tz,'#9bd45a');txt('FROM NIVE',H*0.17+tz*1.1,tz,'#9bd45a');txt('BEST '+best,H*0.17+tz*1.1+fs*1.9,fs,'#e6efd6');txt('PRESS ANY KEY',H*0.42,fs*1.2,'#ffc933');
  var y0=H*0.50,ls=fs*1.32,sm=fs*0.76;
  txt('LEFT / RIGHT or A / D  = lanes',y0,sm,'#c8d3b4');txt('UP / W / SPACE  = jump',y0+ls,sm,'#c8d3b4');txt('DOWN / S  = slide',y0+ls*2,sm,'#c8d3b4');txt('P pause   M mute   - + volume',y0+ls*3,sm,'#c8d3b4');
  var yp=y0+ls*4.2,keys=['magnet','sneakers','shield','x2'];for(i=0;i<4;i++){var xx=W*(0.2+i*0.2),rr=fs*0.7;g.fillStyle=PW[keys[i]].c;g.beginPath();g.arc(xx,yp,rr,0,6.3);g.fill();icon(keys[i],xx,yp,rr*0.62);}
  txt('grab power-ups, find the open tunnel',yp+ls*1.25,sm*0.85,'#8a9a78');
 }
 else if(mode==='over'){shade(0.28);txt('CAUGHT!',H*0.13,Math.max(16,W*0.10),'#e0553f');txt('SCORE '+Math.floor(score)+'   BEST '+best,H*0.13+fs*1.7,fs,'#e6efd6');if(newBest)txt('NEW BEST!',H*0.13+fs*3.2,fs*1.1,'#ffc933');if(time-overAt>0.6)txt('SPACE / ENTER = RUN AGAIN',H*0.13+fs*(newBest?4.8:3.4),fs*0.9,'#9bd45a');}
 else if(countdown>0){shade(0.25);txt(String(Math.min(3,Math.ceil(countdown/0.4))),H*0.46,Math.max(30,W*0.2),'#ffc933');}
 else if(paused){shade(0.5);txt('PAUSED',H*0.45,Math.max(16,W*0.09),'#9bd45a');txt('press any key',H*0.45+fs*2,fs,'#e6efd6');}
 drawControls();
}

/* ---------- input ---------- */
function act(code,key){
 var c=code||'',k=(key||'').toLowerCase();
 if(c==='ArrowLeft'||c==='KeyA'||k==='arrowleft'||k==='a')return 'L';
 if(c==='ArrowRight'||c==='KeyD'||k==='arrowright'||k==='d')return 'R';
 if(c==='ArrowUp'||c==='KeyW'||k==='arrowup'||k==='w')return 'U';
 if(c==='ArrowDown'||c==='KeyS'||k==='arrowdown'||k==='s')return 'D';
 if(c==='Space'||k===' ')return 'SP';
 if(c==='Enter'||k==='enter')return 'EN';
 if(c==='KeyP'||k==='p')return 'P';
 if(c==='KeyM'||k==='m')return 'M';
 if(k==='-'||k==='_')return 'VD';
 if(k==='='||k==='+')return 'VU';
 return '';
}
function jump(){if(pos<=supH+0.02){vel=pw.sneakers>0?V0*1.45:V0;slide=0;}}
function pauseGame(){if(mode!=='play'||(paused&&countdown<=0))return;paused=true;countdown=0;if(ac)ac.suspend();dirty=true;}
function resumeGame(){if(!paused||countdown>0)return;countdown=1.2;dirty=true;}
addEventListener('keydown',function(e){
 var a=act(e.code,e.key);
 if(a)e.preventDefault();
 if(a==='M'){if(!e.repeat)toggleMute();return;}
 if(a==='VD'){setVol(vol-10,true);return;}
 if(a==='VU'){setVol(vol+10,true);return;}
 unlock();
 if(mode==='title'){if(!e.repeat&&e.key!=='Tab'&&e.key!=='Shift'&&e.key!=='Control'&&e.key!=='Alt')reset();return;}
 if(mode==='over'){if(time-overAt>0.6&&(a==='SP'||a==='EN'))reset();return;}
 if(paused){if(!e.repeat)resumeGame();return;}
 if(e.repeat)return;
 if(a==='L'){if(lane>0){prevLane=lane;lane--;}}
 else if(a==='R'){if(lane<2){prevLane=lane;lane++;}}
 else if(a==='U'||a==='SP')jump();
 else if(a==='D'){if(pos>supH+0.02)vel=-9;else slide=0.6;}
 else if(a==='P')pauseGame();
});
addEventListener('blur',function(){dragging=false;pauseGame();});
var dragging=false;
function setFromX(x){var r=ui();setVol(Math.round((x-r.mx)/r.mw*20)*5,false);}
cv.addEventListener('mousedown',function(e){
 e.preventDefault();window.focus();cv.focus();unlock();
 var h=hit(e.offsetX,e.offsetY);
 if(h==='mute'){toggleMute();return;}
 if(h==='meter'){dragging=true;setFromX(e.offsetX);return;}
 if(h==='pause'){if(mode==='play'){if(paused)resumeGame();else pauseGame();}return;}
 if(mode==='title')reset();else if(paused)resumeGame();
});
addEventListener('mousemove',function(e){if(dragging)setFromX(e.offsetX);cv.style.cursor=hit(e.offsetX,e.offsetY)?'pointer':'default';});
addEventListener('mouseup',function(){if(dragging){dragging=false;post('vol:'+vol);}});
cv.addEventListener('wheel',function(e){e.preventDefault();setVol(vol+(e.deltaY<0?5:-5),true);},{passive:false});

/* ---------- what the app tells the page ---------- */
window.__rrConfig=function(c){if(typeof c.volume==='number')vol=clamp(c.volume,0,100);muted=!!c.muted;if(c.best>best)best=c.best;applyVol();dirty=true;};
window.__rrSound=function(name,b64){raw[name]=b64;decode(name);};

/* ---------- developer test hooks ---------- */
window.__rr=function(){return JSON.stringify({mode:mode,lane:lane,pos:Math.round(pos*100)/100,air:pos>supH+0.02,slide:slide>0,score:Math.floor(score),coins:coins,gap:Math.round(gap*100)/100,stumble:stumble>0,paused:paused,items:items.length,scn:scn.length,tunnel:tun>0.5,w:W,h:H,magnet:pw.magnet>0,sneakers:pw.sneakers>0,shield:pw.shield>0,x2:pw.x2>0,vol:vol,muted:muted,best:best,countdown:countdown>0});};
window.__rrAudio=function(){var p={},k;for(k in snd)p[k]=snd[k].want;return JSON.stringify({state:ac?ac.state:'none',loaded:Object.keys(buf).sort(),playing:p,vol:vol,muted:muted,master:master?Math.round(master.gain.value*100)/100:-1,t:ac?Math.round(ac.currentTime*100)/100:0});};
window.__rrSpawn=function(t,l,z,len,h,kind){items.push({t:t,lane:l,z:z,len:len!==undefined&&len!==null?len:(t==='ramp'?0.28:0.34),h:h||0,kind:kind});};
window.__rrPower=function(k){takePower(k);};
window.__rrGate=function(z,list){addGate(z,list.split(','));};
window.__rrScn=function(t,z){scn.push({t:t,z:z});};
window.__rrGateStats=function(){var cnt={open:0,train:0,closed:0},bad=0;for(var n=0;n<300;n++){scn=[];items=[];safe=Math.floor(Math.random()*3);spawnGate();var st=scn[0].st,o=0;for(var i=0;i<3;i++){cnt[st[i]]++;if(st[i]==='open')o++;}if(o<1)bad++;}scn=[];items=[];return JSON.stringify({cnt:cnt,bad:bad});};
window.__rrStep=function(dt){update(dt);dirty=true;};
window.__rrStart=function(){reset();};
window.__rrClear=function(){items=[];scn=scn.filter(function(e){return e.t!=='gate';});nextRow=99;lastPower=1e9;};
window.__rrFreeze=function(f){frozen=f;dirty=true;};
window.__rrSetGap=function(v){gap=v;};
window.__rrSetDist=function(v){dist=v;dirty=true;};
window.__rrDraws=0;
window.__rrUi=function(){return JSON.stringify(ui());};

var last=0;
function loop(ts){
 var dt=last?Math.min(0.05,(ts-last)/1000):0.016;last=ts;
 if(!frozen)update(dt);
 syncAudio();
 /* nothing moves on the title or pause screens, so they are only redrawn when something changes */
 if(dirty||countdown>0||msgs.length||parts.length||mode==='over'||(mode==='play'&&!paused)){draw();dirty=false;window.__rrDraws++;}
 requestAnimationFrame(loop);
}
actx();
requestAnimationFrame(loop);
cv.focus();
})();
</script></body></html>
";
    }

    partial class OverlayForm
    {
        // ---------- game panel ----------
        GamePanel game;
        CoreWebView2Controller gameCtl;
        bool gameVisible, gameCreating, gameInternalNav, gameLoaded;

        void InitGame()
        {
            game = new GamePanel();
            game.Visible = false;
            webHost.Controls.Add(game);
            game.CloseClicked += delegate { ShowGame(false); };
            game.ReloadClicked += delegate { GameLoad(true); FocusGame(); };
        }

        void ToggleGame() { ShowGame(!gameVisible); }

        void ShowGame(bool show)
        {
            if (game == null) return;
            if (!show)
            {
                bool was = gameVisible;
                CloseGame();
                if (was) { if (webShown) PositionWeb(); lastSig = ""; RenderState(true); FocusPageNow(); }
                return;
            }
            if (notesVisible) { notesVisible = false; if (notes != null) notes.Save(); }
            if (toolsVisible) { toolsVisible = false; if (tools != null) tools.SaveAll(); }
            gameVisible = true;
            EnsureGameView();                       // makes the game's page view (it loads the game when ready)
            if (gameCtl != null) GameLoad(false);
            if (!open && !animating) OpenTablet();   // asked for while minimized: bring the tablet up
            else if (webShown) PositionWeb();
            lastSig = ""; RenderState(true);
            if (!Program.TestMode && open) { Native.ForceForeground(webHost.Handle); FocusGame(); }
        }

        // Stops the game and hides the panel. Safe to call any time.
        void CloseGame()
        {
            if (!gameVisible) return;
            gameVisible = false;
            if (game != null) game.Visible = false;
            DisposeGameView();
        }

        // The game's page view is thrown away when the panel closes: the sound stops at once and the memory comes back.
        void DisposeGameView()
        {
            gameLoaded = false;
            if (gameCtl == null) return;
            CoreWebView2Controller c = gameCtl;
            gameCtl = null;
            try { c.IsVisible = false; c.Close(); }
            catch (Exception ex) { AppLog.Write("closing the game view: " + ex.Message); }
        }

        void GameLoad(bool force)
        {
            if (gameCtl == null) return;
            if (gameLoaded && !force) return;
            try
            {
                gameInternalNav = true;
                gameLoaded = true;
                gameCtl.CoreWebView2.NavigateToString(RunnerGame.Html);
            }
            catch { gameInternalNav = false; gameLoaded = false; }
        }

        void FocusGame()
        {
            if (gameCtl == null || !gameVisible) return;
            try { gameCtl.MoveFocus(CoreWebView2MoveFocusReason.Programmatic); } catch { }
        }

        async void EnsureGameView()
        {
            if (gameCtl != null || gameCreating || webEnv == null || game == null) return;
            gameCreating = true;
            try
            {
                CoreWebView2Controller c = await webEnv.CreateCoreWebView2ControllerAsync(game.Handle);
                if (!gameVisible) { c.Close(); return; }   // closed again while it was being made
                c.DefaultBackgroundColor = Color.FromArgb(11, 14, 9);
                c.IsVisible = false;
                SecureGameView(c.CoreWebView2);
                gameCtl = c;
                if (webShown) PositionWeb();
                GameLoad(false);
                if (open && !Program.TestMode) FocusGame();
            }
            catch (Exception ex)
            {
                AppLog.Write("game view: " + ex.Message);
                gameVisible = false;
                if (game != null) game.Visible = false;
                if (webShown) PositionWeb();
                HotkeyNote("Could not start the game panel");
            }
            finally { gameCreating = false; }
        }

        // The game's page view is locked right down: it can only show the game page itself (no web addresses, no frames, no
        // pop-ups, no downloads, no permissions, no dialogs) and every network request is refused. The page may tell the app
        // exactly three things: its volume, whether it is muted, and a new best score.
        void SecureGameView(CoreWebView2 wv)
        {
            CoreWebView2Settings st = wv.Settings;
            st.AreDevToolsEnabled = false;
            st.IsStatusBarEnabled = false;
            st.IsWebMessageEnabled = true;
            st.AreHostObjectsAllowed = false;
            st.IsZoomControlEnabled = false;
            st.IsPasswordAutosaveEnabled = false;
            st.IsGeneralAutofillEnabled = false;
            st.AreDefaultContextMenusEnabled = false;
            st.AreDefaultScriptDialogsEnabled = false;
            st.AreBrowserAcceleratorKeysEnabled = false;
            st.IsBuiltInErrorPageEnabled = false;

            wv.WebMessageReceived += delegate (object s, CoreWebView2WebMessageReceivedEventArgs e)
            {
                string src = e.Source ?? "";
                if (!src.StartsWith("about:", StringComparison.OrdinalIgnoreCase) && !src.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;
                string m = null;
                try { m = e.TryGetWebMessageAsString(); } catch { }
                GameMessage(m);
            };
            wv.NavigationStarting += delegate (object s, CoreWebView2NavigationStartingEventArgs e)
            {
                if (e.Uri.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase)) return;
                if (e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && gameInternalNav) { gameInternalNav = false; return; }
                e.Cancel = true;
            };
            wv.NavigationCompleted += delegate (object s, CoreWebView2NavigationCompletedEventArgs e) { if (e.IsSuccess && gameLoaded) PrepareGamePage(); };
            wv.FrameNavigationStarting += delegate (object s, CoreWebView2NavigationStartingEventArgs e) { if (!e.Uri.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase)) e.Cancel = true; };
            wv.NewWindowRequested += delegate (object s, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; };
            wv.LaunchingExternalUriScheme += delegate (object s, CoreWebView2LaunchingExternalUriSchemeEventArgs e) { e.Cancel = true; };
            wv.PermissionRequested += delegate (object s, CoreWebView2PermissionRequestedEventArgs e) { e.State = CoreWebView2PermissionState.Deny; e.Handled = true; };
            wv.DownloadStarting += delegate (object s, CoreWebView2DownloadStartingEventArgs e) { e.Cancel = true; e.Handled = true; };
            wv.AddWebResourceRequestedFilter("http*", CoreWebView2WebResourceContext.All);
            wv.WebResourceRequested += delegate (object s, CoreWebView2WebResourceRequestedEventArgs e)
            {
                e.Response = webEnv.CreateWebResourceResponse(null, 403, "Blocked", "Content-Type: text/plain");
            };
            wv.ProcessFailed += delegate (object s, CoreWebView2ProcessFailedEventArgs e)
            {
                AppLog.Write("game view problem: " + e.ProcessFailedKind);
                BeginInvoke((MethodInvoker)delegate
                {
                    if (!gameVisible) return;
                    ShowGame(false);
                    HotkeyNote("The game stopped - open it again");
                });
            };
        }

        // The page has loaded: give it the saved volume and best score, then its sound files.
        async void PrepareGamePage()
        {
            CoreWebView2Controller view = gameCtl;
            if (view == null) return;
            try
            {
                CoreWebView2 wv = view.CoreWebView2;
                await wv.ExecuteScriptAsync("window.__rrConfig&&window.__rrConfig({volume:" + cfg.GameVolume + ",muted:" + (cfg.GameMuted ? "true" : "false") + ",best:" + cfg.GameBest + "})");
                foreach (string[] f in GameSounds.Files)
                {
                    string name = f[0], file = f[1];
                    string data = await System.Threading.Tasks.Task.Run(() => GameSounds.Read(file));
                    if (view != gameCtl) return;                    // the panel was closed meanwhile
                    if (data != null) await wv.ExecuteScriptAsync("window.__rrSound&&window.__rrSound('" + name + "','" + data + "')");   // base64: only letters, digits, + / =
                }
            }
            catch (Exception ex) { AppLog.Write("game page: " + ex.Message); }
        }

        // "vol:0-100", "mute:0|1", "best:<score>" - anything else is ignored
        void GameMessage(string m)
        {
            if (m == null || m.Length > 14) return;
            int colon = m.IndexOf(':');
            if (colon < 1) return;
            int v;
            if (!int.TryParse(m.Substring(colon + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out v)) return;
            string key = m.Substring(0, colon);
            if (key == "vol" && v <= 100) cfg.GameVolume = v;
            else if (key == "mute" && v <= 1) cfg.GameMuted = v == 1;
            else if (key == "best" && v <= 9999999 && v > cfg.GameBest) cfg.GameBest = v;
            else return;
            SaveState();
        }

        // ---------- developer test: --gametest <folder> [atak|pilot] ----------
        async System.Threading.Tasks.Task<string> GameEval(string js)
        {
            if (gameCtl == null) return "NO GAME VIEW";
            try { return await gameCtl.CoreWebView2.ExecuteScriptAsync(js); } catch (Exception ex) { return "ERR " + ex.Message; }
        }

        // a key sent straight to the game's page view (no keyboard focus needed, so it can never land in another program)
        async System.Threading.Tasks.Task GameKey(string key, string code, int vk, string text)
        {
            string common = "\"key\":\"" + key + "\",\"code\":\"" + code + "\",\"windowsVirtualKeyCode\":" + vk + ",\"nativeVirtualKeyCode\":" + vk;
            string down = text != null ? "{\"type\":\"keyDown\"," + common + ",\"text\":\"" + text + "\"}" : "{\"type\":\"rawKeyDown\"," + common + "}";
            await gameCtl.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", down);
            await gameCtl.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", "{\"type\":\"keyUp\"," + common + "}");
        }

        async System.Threading.Tasks.Task GameClick(double x, double y)
        {
            string at = "\"x\":" + x.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + ",\"y\":" + y.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + ",\"button\":\"left\",\"clickCount\":1";
            await gameCtl.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", "{\"type\":\"mousePressed\"," + at + "}");
            await gameCtl.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", "{\"type\":\"mouseReleased\"," + at + "}");
        }

        async System.Threading.Tasks.Task<bool> GameReady(int maxMs)
        {
            int t = 0;
            while (t < maxMs)
            {
                if (gameCtl != null && (await GameEval("typeof window.__rr")).Contains("function")) return true;
                await Delay(150); t += 150;
            }
            return false;
        }

        static double Num(string json, string name)
        {
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(json.Replace("\\\"", "\""), "\"" + name + "\":(-?[\\d\\.]+)");
            return m.Success ? double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : -999;
        }

        public async void RunGameTest(string dir)
        {
            StringBuilder log = new StringBuilder();
            Action<string> L = delegate (string s) { log.AppendLine(s); try { File.WriteAllText(Path.Combine(dir, "gametest.txt"), log.ToString()); } catch { } };
            try
            {
                int waited = 0;
                while ((ctl == null || !webShown || animating) && waited < 20000) { await Delay(200); waited += 200; }
                L("ready after " + waited + " ms; open=" + open + " webShown=" + webShown + " layout=" + cfg.Layout);
                await Delay(2500);
                int mainW0 = ctl.Bounds.Width, hostW = webHost.ClientSize.Width;
                Shot(Path.Combine(dir, "g01_before.png"));
                if (!IsPilot)
                {
                    PointF gb = GameBtnCenter(frame.Width);
                    L("GAME button hit-test: " + HitExtra(new Point((int)gb.X, (int)gb.Y)) + "  others: " + HitExtra(Point.Round(LiveBtnCenter(frame.Width))) + " " + HitExtra(Point.Round(TimerBtnCenter(frame.Width))) + " " + HitExtra(Point.Round(NotesBtnCenter(frame.Width))));
                    hoverKey = "GAME"; RenderState(true); await Delay(300); Shot(Path.Combine(dir, "g02_hover_hint.png")); hoverKey = null; RenderState(true);
                    menu.Show(new Point(frame.Left + 60, frame.Top + 60)); await Delay(400); ShotRect(menu.Bounds, Path.Combine(dir, "g02b_menu.png")); menu.Close(); await Delay(200);
                }

                PressExtra("GAME");
                bool ready = await GameReady(10000);
                L("game opened: ready=" + ready + "  label=" + KeyTextFor("GAME"));
                await Delay(1500);
                L("panel " + game.Width + " of " + hostW + " = " + (game.Width / (double)hostW).ToString("0.000") + " (expect about 0.33); page view " + mainW0 + " -> " + ctl.Bounds.Width + "; game view " + gameCtl.Bounds + " vs " + game.ViewArea);
                L("page errors: " + await GameEval("window.__err.join('; ')"));
                L("sounds loaded: " + await GameEval("window.__rrAudio()"));
                Shot(Path.Combine(dir, "g03_title.png"));

                // ---------- steering and keys ----------
                await GameEval("window.__rrFreeze(true)");
                await GameKey("Enter", "Enter", 13, null); await Delay(250);
                L("a key on the title screen starts: " + Short(await GameEval("window.__rr()")));
                string setup = "window.__rrStart();window.__rrFreeze(true);window.__rrClear();";
                await GameEval(setup);
                string st = "";
                await GameKey("ArrowLeft", "ArrowLeft", 37, null); st += "left->" + Num(await GameEval("window.__rr()"), "lane") + " ";
                await GameKey("ArrowLeft", "ArrowLeft", 37, null); st += "left->" + Num(await GameEval("window.__rr()"), "lane") + " ";
                await GameKey("d", "KeyD", 68, "d"); st += "D->" + Num(await GameEval("window.__rr()"), "lane") + " ";
                await GameKey("ArrowRight", "ArrowRight", 39, null); st += "right->" + Num(await GameEval("window.__rr()"), "lane") + " ";
                await GameKey("a", "KeyA", 65, "a"); st += "A->" + Num(await GameEval("window.__rr()"), "lane");
                L("steering (expect 0 0 1 2 1): " + st);

                // ---------- the rules (stepped by hand, so the result never depends on timing) ----------
                Func<int, string> step = delegate (int n) { return "(function(){for(var i=0;i<" + n + ";i++)window.__rrStep(0.01);return window.__rr();})()"; };
                L("rules:");
                await GameEval(setup + "window.__rrSpawn('bar',1,0.2);");
                L("  hurdle, no jump (TRIP, gap 0.48)          -> " + Short(await GameEval(step(70))));
                await GameEval(setup + "window.__rrSpawn('bar',1,0.2);"); await GameKey("ArrowUp", "ArrowUp", 38, null);
                L("  hurdle, jumped (clean, gap 1)             -> " + Short(await GameEval(step(70))));
                await GameEval(setup + "window.__rrSpawn('duck',1,0.2);"); await GameKey("ArrowDown", "ArrowDown", 40, null);
                L("  low bar, slid under (clean)               -> " + Short(await GameEval(step(70))));
                await GameEval(setup + "window.__rrSpawn('over',1,0.2);");
                L("  gantry, standing (TRIP)                   -> " + Short(await GameEval(step(70))));
                await GameEval(setup + "window.__rrSpawn('train',1,0.2);");
                L("  train head-on (CAUGHT)                    -> " + Short(await GameEval(step(70))));
                await GameEval(setup + "window.__rrSpawn('train',0,-0.1,0.9);"); await GameKey("ArrowLeft", "ArrowLeft", 37, null);
                L("  into a train's side (TRIP, back lane 1)   -> " + Short(await GameEval(step(30))));
                await GameEval(setup + "window.__rrSpawn('ramp',1,0.2,0.28);window.__rrSpawn('train',1,0.48,0.9);");
                L("  ramp then train (on the roof, pos 1.5)    -> " + Short(await GameEval(step(120))));
                await GameEval(setup + "window.__rrGate(0.3,'closed,open,train');");
                L("  open tunnel (clean)                       -> " + Short(await GameEval(step(130))));
                await GameEval(setup + "window.__rrGate(0.3,'closed,open,train');"); await GameKey("ArrowLeft", "ArrowLeft", 37, null);
                L("  shuttered tunnel (CAUGHT)                 -> " + Short(await GameEval(step(80))));
                L("  300 random walls (bad 0): " + await GameEval("window.__rrGateStats()"));

                // ---------- power-ups ----------
                L("power-ups:");
                await GameEval(setup + "window.__rrPower('magnet');window.__rrSpawn('coin',0,0.3);window.__rrSpawn('coin',2,0.35);");
                L("  magnet pulls coins from other lanes (coins 2)       -> " + Short(await GameEval(step(90))));
                await GameEval(setup + "window.__rrPower('sneakers');window.__rrSpawn('train',1,0.5,0.9);"); await GameEval(step(20)); await GameKey("ArrowUp", "ArrowUp", 38, null);
                L("  super sneakers jump onto a train (play, pos 1.5)     -> " + Short(await GameEval(step(110))));
                await GameEval(setup + "window.__rrPower('shield');window.__rrSpawn('bar',1,0.2);");
                L("  shield takes a trip (gap 1, shield gone)             -> " + Short(await GameEval(step(70))));
                await GameEval(setup + "window.__rrPower('shield');window.__rrSpawn('train',1,0.2);");
                L("  shield smashes through a train (play, shield gone)   -> " + Short(await GameEval(step(70))));
                await GameEval(setup + "window.__rrPower('shield');window.__rrGate(0.3,'closed,open,open');"); await GameKey("ArrowLeft", "ArrowLeft", 37, null);
                L("  shield smashes through a shutter (play)              -> " + Short(await GameEval(step(100))));
                await GameEval(setup + "window.__rrSpawn('coin',1,0.2);");
                string plain = await GameEval(step(60));
                await GameEval(setup + "window.__rrPower('x2');window.__rrSpawn('coin',1,0.2);");
                string doubled = await GameEval(step(60));
                L("  2x score: plain " + Num(plain, "score") + " vs doubled " + Num(doubled, "score"));
                await GameEval(setup + "window.__rrSpawn('power',1,0.2,0,0,'magnet');");
                L("  picking one up in the lane (magnet true)             -> " + Short(await GameEval(step(60))));

                // ---------- sound ----------
                L("sound:");
                await GameKey("w", "KeyW", 87, "w");   // (any key press lets the page start its sound)
                await GameEval(setup + "window.__rrStep(0.01);"); await Delay(500);
                L("  running            (music)                 -> " + await GameEval("window.__rrAudio()"));
                await GameEval("window.__rrSpawn('bar',1,0.05);" + step(10).Replace("return window.__rr();", "return 0;")); await Delay(300);
                L("  tripped            (music low, theme, sting) -> " + await GameEval("window.__rrAudio()"));
                await GameEval("window.__rrSetGap(0.97);window.__rrStep(0.01);"); await Delay(300);
                L("  chaser gone        (music only)            -> " + await GameEval("window.__rrAudio()"));
                await GameEval("window.__rrSpawn('train',1,0.01);window.__rrStep(0.02);"); await Delay(300);
                L("  caught             (slowed only)           -> " + await GameEval("window.__rrAudio()"));
                await GameEval(setup); await Delay(300);
                await GameKey("p", "KeyP", 80, "p"); await Delay(300);
                L("  P pauses           (suspended)             -> " + await GameEval("window.__rrAudio()") + "  " + Short(await GameEval("window.__rr()")));
                await GameKey("w", "KeyW", 87, "w"); await Delay(100);
                L("  any key: countdown                         -> " + Short(await GameEval("window.__rr()")));
                await GameEval(step(140)); await Delay(300);
                L("  then it runs again (running)               -> " + await GameEval("window.__rrAudio()") + "  " + Short(await GameEval("window.__rr()")));

                // ---------- volume meter and mute ----------
                int v0 = cfg.GameVolume;
                await GameKey("-", "Minus", 189, "-"); await Delay(300);
                L("volume: '-' key " + v0 + " -> saved " + cfg.GameVolume + " (expect " + (v0 - 10) + ")  page " + Num(await GameEval("window.__rr()"), "vol"));
                await GameKey("m", "KeyM", 77, "m"); await Delay(300);
                L("  M mutes: saved muted=" + cfg.GameMuted + "  " + await GameEval("window.__rrAudio()"));
                await GameKey("m", "KeyM", 77, "m"); await Delay(300);
                L("  M again: saved muted=" + cfg.GameMuted);
                string uij = await GameEval("window.__rrUi()");
                double u = Num(uij, "u"), mx = Num(uij, "mx"), mw = Num(uij, "mw"), sy = Num(uij, "sy"), sx = Num(uij, "sx"), px = Num(uij, "px");
                await GameClick(mx + mw * 0.5, sy + u / 2); await Delay(400);
                L("  click the middle of the meter -> saved " + cfg.GameVolume + " (expect 50)");
                await GameClick(sx + u / 2, sy + u / 2); await Delay(400);
                L("  click the speaker -> muted=" + cfg.GameMuted + " (expect True)");
                await GameClick(mx + mw * 0.8, sy + u / 2); await Delay(400);
                L("  click the meter while muted -> muted=" + cfg.GameMuted + " volume " + cfg.GameVolume + " (expect False 80)");
                await GameEval(setup); await Delay(200);
                await GameClick(px + u / 2, sy + u / 2); await Delay(300);
                L("  pause button -> " + Short(await GameEval("window.__rr()")));

                // ---------- the screen is only redrawn when something moves ----------
                await GameEval("window.__rrFreeze(false)");
                await Delay(1500);
                int d0 = (int)Num("{\"d\":" + await GameEval("window.__rrDraws") + "}", "d");
                await Delay(1000);
                int d1 = (int)Num("{\"d\":" + await GameEval("window.__rrDraws") + "}", "d");
                L("redraws while paused for 1 s: " + (d1 - d0) + " (expect 0)");

                // ---------- pictures ----------
                string pics = "for(var i=0;i<260;i++){window.__rrStep(0.02);window.__rrClear();}";
                await GameEval(setup + pics + "window.__rrSpawn('ramp',0,0.3,0.28);window.__rrSpawn('train',0,0.58,0.9);window.__rrSpawn('train',2,0.75,0.5);window.__rrSpawn('coin',1,0.35);window.__rrSpawn('coin',1,0.45);window.__rrSpawn('power',1,0.7,0,0,'magnet');window.__rrPower('shield');window.__rrPower('x2');window.__rrStep(0.001);");
                await Delay(1300); Shot(Path.Combine(dir, "g04_play.png"));
                await GameEval(setup + pics + "window.__rrSetDist(20);window.__rrSpawn('bar',0,0.3);window.__rrSpawn('duck',2,0.45);window.__rrPower('magnet');window.__rrPower('sneakers');window.__rrStep(0.001);");
                await Delay(1300); Shot(Path.Combine(dir, "g05_night.png"));
                await GameEval(setup + pics + "window.__rrSetDist(47);window.__rrGate(0.55,'closed,open,train');window.__rrStep(0.001);");
                await Delay(1300); Shot(Path.Combine(dir, "g06_day_gate.png"));
                await GameKey("p", "KeyP", 80, "p"); await Delay(400); Shot(Path.Combine(dir, "g07_paused.png"));
                await GameEval(setup + "window.__rrSpawn('bar',1,0.2);" + step(65).Replace("return window.__rr();", "return 0;")); await Delay(500);
                Shot(Path.Combine(dir, "g08_tripped.png"));
                await GameEval(step(110)); await GameEval("window.__rrSpawn('bar',1,0.0);"); await GameEval(step(40));
                await GameEval("(function(){var n=0;while(n<400){window.__rrStep(0.01);n++;if(JSON.parse(window.__rr()).mode==='over')break;}return n;})()");
                await GameEval("window.__rrStep(0.15);"); await Delay(300);
                Shot(Path.Combine(dir, "g09_caught.png"));

                // ---------- saved best score ----------
                await GameEval(setup + "window.__rrFreeze(true);");
                await GameEval(step(300));
                await GameEval("window.__rrSpawn('train',1,0.0);window.__rrStep(0.01);"); await Delay(400);
                int saved = cfg.GameBest;
                L("best score: saved " + saved + " after a run (expect above 0), page says " + Num(await GameEval("window.__rr()"), "best"));

                // ---------- the real keyboard, only if this window really has it (never into another program) ----------
                await GameEval("window.__rrStart();window.__rrFreeze(true);window.__rrClear();");
                Native.ForceForeground(webHost.Handle); await Delay(500);
                FocusGame(); await Delay(400);
                IntPtr fg = Native.GetForegroundWindow(); uint fpid; Native.GetWindowPid(fg, out fpid);
                if (fpid == myPid)
                {
                    string real = "";
                    foreach (string k in new string[] { "{LEFT}", "{RIGHT}", "d", "a" })
                    {
                        Native.GetWindowPid(Native.GetForegroundWindow(), out fpid);
                        if (fpid != myPid) { real += "[keyboard lost to another window, stopped]"; break; }
                        SendKeys.SendWait(k); await Delay(150);
                        real += k + "=" + Num(await GameEval("window.__rr()"), "lane") + " ";
                    }
                    L("real keyboard lanes (expect 0 1 2 1): " + real);
                }
                else L("real-keyboard check skipped (this program was not in front)");

                // ---------- the lock-down ----------
                await GameEval("window.__net='pending';fetch('https://example.com/').then(function(){window.__net='REACHED';}).catch(function(){window.__net='refused';});window.__pop=String(window.open('https://example.com/'));var f=document.createElement('iframe');f.src='https://example.com/';document.body.appendChild(f);");
                await Delay(1500);
                L("page tries the internet -> " + await GameEval("window.__net") + "  window.open -> " + await GameEval("window.__pop"));
                gameCtl.CoreWebView2.Navigate("https://example.com/"); await Delay(1200);
                L("navigating away -> still the game: " + (await GameEval("typeof window.__rr")).Contains("function"));
                await GameEval("window.chrome.webview.postMessage('best:99999999');window.chrome.webview.postMessage('vol:500');window.chrome.webview.postMessage('url:https://evil');"); await Delay(400);
                L("bad messages ignored: best " + cfg.GameBest + " (was " + saved + "), volume " + cfg.GameVolume);

                // ---------- closing, reopening, taking turns ----------
                game.ClickClose(); await Delay(700);
                L("close button -> visible=" + gameVisible + " view thrown away=" + (gameCtl == null) + "  page view back to " + ctl.Bounds.Width + " (was " + mainW0 + ")");
                PressExtra("GAME"); bool again = await GameReady(10000); await Delay(1200);
                L("reopened -> ready=" + again + " best on the title " + Num(await GameEval("window.__rr()"), "best") + " (expect " + cfg.GameBest + "), volume " + Num(await GameEval("window.__rr()"), "vol"));
                ShowNotes(true); await Delay(500);
                L("notes opened -> game visible=" + gameVisible + " view thrown away=" + (gameCtl == null));
                ShowGame(true); await GameReady(10000);
                L("game again -> notes=" + notesVisible + " game=" + gameVisible);
                ShowTools(0); await Delay(400);
                L("tools opened -> game visible=" + gameVisible);
                ShowGame(true); await GameReady(10000);
                CloseTablet(); await Delay(800);
                L("tablet minimized -> game=" + gameVisible + " view thrown away=" + (gameCtl == null));
                PressExtra("GAME"); await GameReady(10000); await Delay(800);
                L("pressed GAME while minimized -> open=" + open + " game=" + gameVisible);
                Shot(Path.Combine(dir, "g10_from_minimized.png"));
                // open and close quickly many times: nothing may break
                for (int i = 0; i < 6; i++) { ShowGame(false); await Delay(30); ShowGame(true); await Delay(30); ShowGame(false); await Delay(10); }
                ShowGame(true); bool stable = await GameReady(10000);
                L("after fast open/close x6 -> ready=" + stable);
                ShowGame(false);

                // ---------- settings file ----------
                string tmpIni = Path.Combine(dir, "game.ini");
                cfg.Save(tmpIni); Config back = Config.Load(tmpIni);
                L("settings round trip: volume " + back.GameVolume + " muted " + back.GameMuted + " best " + back.GameBest);
                File.WriteAllText(tmpIni, "Page3=MAP,WEB,NOTE,TIME,LIVE,PAGE,SET,UPD,CLRD,NONE,NONE,NONE,BACK,FWD,RFSH,ADS,LOCK,XPAR,PR1,PR2,PR3,UTC,SHOT,CPY\r\nGameVolume=900\r\nGameBest=-5\r\n");
                back = Config.Load(tmpIni);
                L("old page 3 -> " + back.Pages[2][9] + " (expect GAME); out-of-range values -> volume " + back.GameVolume + " best " + back.GameBest);

                // ---------- security checks of the other fixes ----------
                L("plain-http allowed only on your network: localhost=" + IsLocalHost("localhost") + " 192.168.1.5=" + IsLocalHost("192.168.1.5") + " 172.20.0.1=" + IsLocalHost("172.20.0.1") + " nas=" + IsLocalHost("nas") +
                    " | 10.example.com=" + IsLocalHost("10.example.com") + " 127.evil.net=" + IsLocalHost("127.evil.net") + " 192.168.evil.org=" + IsLocalHost("192.168.evil.org") + " 8.8.8.8=" + IsLocalHost("8.8.8.8"));
                Config mine = new Config(), theirs = new Config();
                theirs.Url = "https://phish.example/gate"; theirs.UpdateUrl = "https://evil.example/version.txt"; theirs.Bookmarks[0] = "x|https://bad.example"; theirs.HttpsOnly = false;
                L("import warning lists: " + ImportRisks(mine, theirs).Replace("\r\n", " | "));
                KeepOwnSensitive(mine, theirs);
                L("  'No' keeps own: url ok=" + (theirs.Url == mine.Url) + " https-only=" + theirs.HttpsOnly + " bookmarks cleared=" + (theirs.Bookmarks[0].Length == 0) + "; unchanged settings warn nothing: " + (ImportRisks(mine, new Config()).Length == 0));
                L("DONE");
            }
            catch (Exception ex) { L("EXCEPTION " + ex); }
            Quit();
        }

        // the interesting parts of the game's state, for the test log
        static string Short(string json)
        {
            string j = json.Replace("\\\"", "\"").Trim('"');
            Func<string, string> f = delegate (string name)
            {
                System.Text.RegularExpressions.Match mm = System.Text.RegularExpressions.Regex.Match(j, "\"" + name + "\":\"?([\\w\\.\\-]+)");
                return mm.Success ? mm.Groups[1].Value : "?";
            };
            if (!j.StartsWith("{")) return json;
            string extra = "";
            foreach (string k in new string[] { "shield", "magnet", "paused", "countdown", "stumble" }) if (f(k) == "true") extra += " " + k;
            return "mode=" + f("mode") + " lane=" + f("lane") + " pos=" + f("pos") + " gap=" + f("gap") + " coins=" + f("coins") + " score=" + f("score") + extra;
        }

        ToolsPanel tools;
        bool toolsVisible;
        int toolsTab;

        void InitTools()
        {
            tools = new ToolsPanel();
            tools.Visible = false;
            webHost.Controls.Add(tools);
            tools.CloseClicked += delegate { ShowTools(false); };
            tools.EscPressed += delegate { FocusPageNow(); };
            InitGame();
        }

        // a tab number (0 bearing/distance, 1 nine-line, 2 checklist) shows it; pressing the same tab again hides the panel
        void ShowTools(int tabIndex)
        {
            if (tools == null) return;
            if (toolsVisible && toolsTab == tabIndex) { ShowTools(false); return; }
            toolsTab = tabIndex;
            tools.SelectTab(tabIndex);
            ShowTools(true);
        }

        void ShowTools(bool show)
        {
            if (tools == null) return;
            if (show && notesVisible) { notesVisible = false; if (notes != null) notes.Save(); }
            if (show) CloseGame();
            toolsVisible = show;
            if (!show) tools.SaveAll();
            if (!open && !animating && show) { focusNotesOnOpen = false; OpenTablet(); }   // asked for while minimized: bring it up
            else if (webShown) PositionWeb();
            lastSig = ""; RenderState(true);
            if (show && !Program.TestMode && open) Native.ForceForeground(webHost.Handle);
            if (!show) FocusPageNow();
        }

        // ---------- mission log (page 4 of the notes pad) ----------
        void AddLogLine()
        {
            if (notes == null) return;
            DateTime d = cfg.Zulu ? DateTime.UtcNow : DateTime.Now;
            notes.AppendLog("[" + d.ToString("HH:mm:ss") + (cfg.Zulu ? "Z" : "") + "] ");
            if (toolsVisible) { toolsVisible = false; tools.SaveAll(); }
            CloseGame();
            notesVisible = true; focusNotesOnOpen = true;
            notes.GotoPage(3);
            if (!open && !animating) OpenTablet();
            else { if (webShown) PositionWeb(); if (!Program.TestMode) { Native.ForceForeground(webHost.Handle); notes.FocusBox(); } }
            lastSig = ""; RenderState(true);
        }

        void CopyLog()
        {
            try
            {
                if (notes != null && notes.LogText.Length > 0) { Clipboard.SetText(notes.LogText); HotkeyNote("Mission log copied"); }
                else HotkeyNote("The mission log is empty");
            }
            catch { }
        }

        // ---------- bookmarks ----------
        void OpenBookmark(int i)
        {
            string b = cfg.Bookmarks[i];
            int bar = b.IndexOf('|');
            string url = bar >= 0 ? b.Substring(bar + 1).Trim() : "";
            if (url.Length == 0) { HotkeyNote("Bookmark " + (i + 1) + " is empty - set it in Settings"); return; }
            string u = ResolveInput(url);
            if (u == null || ctl == null) return;
            try { ctl.CoreWebView2.Navigate(u); } catch { }
            FocusPageNow();
        }

        // ---------- game-aware: only show while the game is the window in front ----------
        bool gameHidden;

        bool GameInFront()
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero) return !gameHidden;           // Windows is mid-switch: keep what we have
            uint pid; Native.GetWindowPid(fg, out pid);
            if (pid == myPid || (browserPid != 0 && pid == browserPid)) return true;   // our own windows (settings, menus) count
            try
            {
                string name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
                string want = cfg.GameProcess.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? cfg.GameProcess.Substring(0, cfg.GameProcess.Length - 4) : cfg.GameProcess;
                return string.Equals(name, want, StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; }
        }

        // Called from the 500 ms timer while the tablet is minimized.
        void ApplyGameVisibility()
        {
            bool hideIt = cfg.GameOnly && !GameInFront();
            if (hideIt && !gameHidden)
            {
                gameHidden = true;
                if (bezelShown) Native.ShowWindow(Handle, Native.SW_HIDE);
                if (LiveShown && webHost != null) Native.ShowWindow(webHost.Handle, Native.SW_HIDE);
            }
            else if (!hideIt && gameHidden) RestoreFromGameHidden();
        }

        void RestoreFromGameHidden()
        {
            gameHidden = false;
            if (bezelShown) { Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE); RaiseBezel(); }
            if (LiveShown && webHost != null) { Native.ShowWindow(webHost.Handle, Native.SW_SHOWNOACTIVATE); RaiseLive(); }
        }

        public void SetGameOnly(bool on)
        {
            cfg.GameOnly = on; SaveState();
            if (!on && gameHidden) RestoreFromGameHidden();
        }

        // ---------- settings: export / import / log folder / restart ----------
        bool noSave;   // set while importing, so closing the app doesn't overwrite what was just imported

        public void ExportSettings(IWin32Window parent)
        {
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = "Export your Tac Map Overlay settings";
                dlg.Filter = "Settings file (*.ini)|*.ini"; dlg.FileName = "TacOverlay-settings.ini";
                if (dlg.ShowDialog(parent) != DialogResult.OK) return;
                try { SaveState(); File.Copy(cfgPath, dlg.FileName, true); HotkeyNote("Settings exported"); }
                catch (Exception ex) { MessageBox.Show(parent, "Couldn't export: " + ex.Message, "Tac Map Overlay"); }
            }
        }

        // returns true when the app is restarting with the imported settings
        public bool ImportSettings(IWin32Window parent)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Import Tac Map Overlay settings"; dlg.Filter = "Settings file (*.ini)|*.ini|All files|*.*"; dlg.CheckFileExists = true;
                if (dlg.ShowDialog(parent) != DialogResult.OK) return false;
                try
                {
                    string text = File.ReadAllText(dlg.FileName);
                    if (text.IndexOf("ToggleKey", StringComparison.OrdinalIgnoreCase) < 0 && text.IndexOf("Url=", StringComparison.OrdinalIgnoreCase) < 0)
                    { MessageBox.Show(parent, "That doesn't look like a Tac Map Overlay settings file.", "Tac Map Overlay"); return false; }
                    if (MessageBox.Show(parent, "Replace your current settings with this file and restart the app?", "Tac Map Overlay",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
                    Config incoming = Config.Load(dlg.FileName);
                    // A settings file may come from someone else. It never changes where updates come from, and anything that
                    // changes where the tablet goes (or weakens the browser) is shown first.
                    incoming.UpdateUrl = cfg.UpdateUrl;
                    string changes = ImportRisks(cfg, incoming);
                    if (changes.Length > 0)
                    {
                        DialogResult r = MessageBox.Show(parent, "This file changes where the tablet goes or how safe it is:\r\n" + changes +
                            "\r\n\r\nOnly press Yes if you trust whoever gave you the file.\r\n\r\nYes = use these too.   No = keep your own for these, import the rest.",
                            "Tac Map Overlay - check this settings file", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                        if (r == DialogResult.Cancel) return false;
                        if (r == DialogResult.No) KeepOwnSensitive(cfg, incoming);
                    }
                    File.Copy(cfgPath, cfgPath + ".bak", true);
                    incoming.Save(cfgPath);
                    noSave = true;
                    RestartApp();
                    return true;
                }
                catch (Exception ex) { MessageBox.Show(parent, "Couldn't import: " + ex.Message, "Tac Map Overlay"); return false; }
            }
        }

        // what an imported file would change that matters for safety (empty = nothing)
        static string ImportRisks(Config mine, Config theirs)
        {
            StringBuilder s = new StringBuilder();
            if (!theirs.Url.Trim().TrimEnd('/').Equals(mine.Url.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) s.Append("\r\n  Map address:  " + Cut(theirs.Url));
            if (!theirs.SearchUrl.Equals(mine.SearchUrl, StringComparison.OrdinalIgnoreCase)) s.Append("\r\n  Search page:  " + Cut(theirs.SearchUrl));
            for (int i = 0; i < 6; i++)
                if (theirs.Bookmarks[i].Length > 0 && theirs.Bookmarks[i] != mine.Bookmarks[i]) s.Append("\r\n  Bookmark " + (i + 1) + ":  " + Cut(theirs.Bookmarks[i]));
            if (mine.HttpsOnly && !theirs.HttpsOnly) s.Append("\r\n  Turns OFF 'only allow secure (https) sites'");
            if (mine.AdBlock && !theirs.AdBlock) s.Append("\r\n  Turns OFF the ad / tracker blocker");
            return s.ToString();
        }

        static string Cut(string s) { return s.Length > 90 ? s.Substring(0, 90) + "..." : s; }

        static void KeepOwnSensitive(Config mine, Config theirs)
        {
            theirs.Url = mine.Url; theirs.SearchUrl = mine.SearchUrl;
            theirs.Bookmarks = (string[])mine.Bookmarks.Clone();
            theirs.HttpsOnly = mine.HttpsOnly; theirs.AdBlock = mine.AdBlock;
        }

        // Restart with default settings. The old settings are kept as TacOverlay.ini.bak.
        public bool RestartInSafeMode(IWin32Window parent)
        {
            if (MessageBox.Show(parent, "Restart the app with DEFAULT settings?\r\n\r\nYour current settings are kept as TacOverlay.ini.bak.", "Tac Map Overlay",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
            noSave = true;   // don't overwrite the settings file on the way out
            RestartApp("--safe");
            return true;
        }

        void RestartApp() { RestartApp(""); }

        void RestartApp(string extra)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, ("--after-update " + System.Diagnostics.Process.GetCurrentProcess().Id + " " + extra).Trim());
                psi.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
                System.Diagnostics.Process.Start(psi);
            }
            catch { }
            Quit();
            Environment.Exit(0);
        }

        public void OpenLogFolder()
        {
            try { Directory.CreateDirectory(AppLog.Dir); System.Diagnostics.Process.Start("explorer.exe", "\"" + AppLog.Dir + "\""); } catch { }
        }

        // ---------- saved layouts ----------
        public void SavePreset(int i)
        {
            cfg.Presets[i] = string.Join("|", new string[] {
                cfg.OpenSize.ToString(), cfg.Anchor, (cfg.CX == int.MinValue ? "" : cfg.CX.ToString()), (cfg.CY == int.MinValue ? "" : cfg.CY.ToString()),
                cfg.Opacity.ToString(), cfg.PageWidth.ToString(), cfg.ShowPanel ? "1" : "0", cfg.ShowLabels ? "1" : "0", cfg.LabelInset ? "1" : "0" });
            SaveState();
            HotkeyNote("Layout saved as preset " + (i + 1));
        }

        void ApplyPreset(int i)
        {
            string s = cfg.Presets[i];
            if (s.Length == 0) { HotkeyNote("Preset " + (i + 1) + " is empty - save one in Settings"); return; }
            string[] p = s.Split('|');
            if (p.Length < 9) return;
            int n;
            if (int.TryParse(p[0], out n)) cfg.OpenSize = Math.Max(25, Math.Min(90, n));
            if (p[1] == "center" || p[1] == "bottomright" || p[1] == "custom") cfg.Anchor = p[1];
            if (int.TryParse(p[2], out n)) cfg.CX = n; else cfg.CX = int.MinValue;
            if (int.TryParse(p[3], out n)) cfg.CY = n; else cfg.CY = int.MinValue;
            if (cfg.Anchor == "custom" && (cfg.CX == int.MinValue || cfg.CY == int.MinValue)) cfg.Anchor = "center";
            if (int.TryParse(p[4], out n)) cfg.Opacity = Math.Max(25, Math.Min(100, n));
            if (int.TryParse(p[5], out n)) cfg.PageWidth = Math.Max(500, Math.Min(2400, n));
            cfg.ShowPanel = p[6] == "1"; cfg.ShowLabels = p[7] == "1"; cfg.LabelInset = p[8] == "1";
            ApplyMode();
            MoveToOpenRect();                  // re-places the tablet (open or minimized) with the new size / position
            if (webShown) PositionWeb();
            lastSig = ""; RenderState(true);
            HotkeyNote("Preset " + (i + 1) + " applied");
        }
    }
}
