// Offline tools panel (bearing / distance between grids, 9-line template, checklist) and the actions that go with the
// pilot-display buttons: mission log, bookmarks, layout presets.
using System;
using System.Collections.Generic;
using System.Drawing;
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

    // The game panel: a title strip (restart / close) and, under it, a second page view that fills the rest.
    class GamePanel : SidePanel
    {
        public event Action CloseClicked;
        public event Action ReloadClicked;
        readonly Label title = new Label();
        readonly Button reload = new Button(), close = new Button();
        Font uiFont;
        Rectangle viewArea = new Rectangle(0, 0, 10, 10);
        public Rectangle ViewArea { get { return viewArea; } }     // where the game's page view goes (in this panel's own coordinates)
        public void ClickClose() { close.PerformClick(); }          // (the self-test presses the button this way)

        static readonly Color Bg = Color.FromArgb(24, 27, 22), BtnBg = Color.FromArgb(40, 45, 36),
            Fg = Color.FromArgb(205, 215, 190), Green = Color.FromArgb(150, 205, 90);

        public GamePanel()
        {
            BackColor = Bg;
            title.Text = "RUN AWAY FROM NIVE"; title.ForeColor = Green; title.TextAlign = ContentAlignment.MiddleLeft;
            reload.Text = "↻"; close.Text = "✕";
            reload.Click += delegate { if (ReloadClicked != null) ReloadClicked(); };
            close.Click += delegate { if (CloseClicked != null) CloseClicked(); };
            Controls.AddRange(new Control[] { title, reload, close });
            foreach (Button b in new Button[] { reload, close })
            {
                b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.UseVisualStyleBackColor = false;
                b.BackColor = BtnBg; b.ForeColor = Fg; b.TabStop = false; b.Cursor = Cursors.Hand; b.Padding = Padding.Empty;
            }
        }

        public override void Relayout(int w, int h, double sc)
        {
            int row = (int)Math.Max(24, Math.Min(44, Math.Round(10.5 * sc))), p = Math.Max(3, row / 7);
            FontUtil.Apply(ref uiFont, "Consolas", Math.Max(10f, row * 0.44f), FontStyle.Bold, new Control[] { title, reload, close });
            int bs = row - 2 * p;
            close.SetBounds(w - p - bs, p, bs, bs);
            reload.SetBounds(close.Left - p - bs, p, bs, bs);
            title.SetBounds(p, p, Math.Max(10, reload.Left - 2 * p), bs);
            viewArea = new Rectangle(0, row, Math.Max(10, w), Math.Max(20, h - row));
        }
    }

    // Sound effects for the game: plays the mp3 files found in a "sounds" folder next to the program (or in
    // %LocalAppData%\TacOverlay\sounds). The files are NOT part of the program - if one is missing that sound is simply skipped.
    //   enrique.mp3         - when you trip or hit something and the chaser starts closing in
    //   fnaf2-ambience.mp3  - loops while the chaser is right behind you
    //   enrique-slowed.mp3  - when you are caught
    static class Sfx
    {
        [System.Runtime.InteropServices.DllImport("winmm.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int mciSendString(string command, StringBuilder ret, int retLen, IntPtr callback);

        class Clip { public string Alias, Path; public bool Opened, Loop, Wanted; }
        static readonly Dictionary<string, Clip> clips = new Dictionary<string, Clip>();
        static readonly Dictionary<string, string> files = new Dictionary<string, string>
        {
            { "enrique", "enrique.mp3" }, { "ambience", "fnaf2-ambience.mp3" }, { "slowed", "enrique-slowed.mp3" }
        };
        public static int Volume = 700;                       // 0..1000 (tests turn it right down)
        public static readonly StringBuilder Log = new StringBuilder();   // what happened, for the self-test
        static System.Windows.Forms.Timer loopTimer;

        static string Find(string file)
        {
            try
            {
                string a = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sounds", file);
                if (File.Exists(a)) return a;
                string b = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TacOverlay", "sounds", file);
                if (File.Exists(b)) return b;
            }
            catch { }
            return null;
        }

        static string Ask(string cmd)
        {
            StringBuilder r = new StringBuilder(128);
            int rc = mciSendString(cmd, r, r.Capacity, IntPtr.Zero);
            return rc == 0 ? r.ToString() : "ERR" + rc;
        }

        static Clip Get(string key)
        {
            Clip c;
            if (clips.TryGetValue(key, out c)) return c;
            string file;
            if (!files.TryGetValue(key, out file)) return null;
            c = new Clip(); c.Alias = "tacsfx_" + key; c.Path = Find(file);
            if (c.Path != null) c.Opened = !Ask("open \"" + c.Path + "\" type mpegvideo alias " + c.Alias).StartsWith("ERR");
            clips[key] = c;
            if (c.Path == null) Log.AppendLine("missing: " + file);
            else if (!c.Opened) Log.AppendLine("could not open: " + file);
            return c;
        }

        public static bool Playing(string key)
        {
            Clip c; if (!clips.TryGetValue(key, out c) || !c.Opened) return false;
            return Ask("status " + c.Alias + " mode") == "playing";
        }

        public static void Play(string key, bool loop)
        {
            Clip c = Get(key); if (c == null || !c.Opened) return;
            c.Loop = loop; c.Wanted = true;
            Ask("setaudio " + c.Alias + " volume to " + Volume);
            Ask("play " + c.Alias + " from 0");
            Log.AppendLine("play " + key + (loop ? " (loop)" : ""));
            if (loop && loopTimer == null)
            {
                loopTimer = new System.Windows.Forms.Timer(); loopTimer.Interval = 250;
                loopTimer.Tick += delegate { Tick(); };
                loopTimer.Start();
            }
        }

        public static void Stop(string key)
        {
            Clip c; if (!clips.TryGetValue(key, out c) || !c.Opened) return;
            c.Wanted = false;
            Ask("stop " + c.Alias);
            Log.AppendLine("stop " + key);
        }

        public static void StopAll() { foreach (string k in new List<string>(clips.Keys)) { Clip c = clips[k]; if (c.Opened && (c.Wanted || Playing(k))) Stop(k); } }

        static void Tick()
        {
            foreach (Clip c in clips.Values)
                if (c.Opened && c.Loop && c.Wanted && Ask("status " + c.Alias + " mode") != "playing") Ask("play " + c.Alias + " from 0");
        }

        // what the game page asks for (only these exact words are accepted)
        public static void Message(string m)
        {
            switch (m)
            {
                case "enrique": if (!Playing("enrique")) Play("enrique", false); break;
                case "amb1": if (!Playing("ambience")) Play("ambience", true); break;
                case "amb0": Stop("ambience"); break;
                case "chaseend": Stop("enrique"); Stop("ambience"); break;
                case "slowed": StopAll(); Play("slowed", false); break;
                case "stopall": StopAll(); break;
            }
        }
    }

    // The game itself: a small original 3-lane runner, written for this app. It is one page that runs with no internet at all
    // (nothing is downloaded, there are no ads, and the page view it runs in is not allowed to reach the network).
    static class RunnerGame
    {
        public const string Html = @"<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>
<style>html,body{margin:0;height:100%;overflow:hidden;background:#0b0e09}canvas{display:block;width:100%;height:100%;outline:none}</style></head>
<body><canvas id='c' tabindex='0'></canvas><script>
(function(){
window.__err=[];window.onerror=function(m,s,l){window.__err.push(m+'@'+l);};
var cv=document.getElementById('c'),g=cv.getContext('2d');
var W=300,H=400,dpr=1,K=5,LS=90,cx=150,hor=100,py=320,ZMIN=-0.15,Z0=1.5,TH=1.5;
function fit(){dpr=window.devicePixelRatio||1;W=Math.max(120,innerWidth);H=Math.max(160,innerHeight);cv.width=Math.round(W*dpr);cv.height=Math.round(H*dpr);g.setTransform(dpr,0,0,dpr,0,0);cx=W/2;hor=H*0.27;py=H*0.80;LS=Math.min(W*0.30,(py-hor)*0.62);}
addEventListener('resize',fit);fit();
var COP=new Image();COP.src='data:image/jpeg;base64,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';
var best=0;try{best=+localStorage.getItem('rr_best')||0;}catch(e){}
var mode='title',paused=false,score=0,coins=0,speed=0.55,speedMul=1,dist=0,time=0,overAt=0,shake=0,nextRow=0.6,safe=1,frozen=false;
var lane=1,prevLane=1,laneX=1,pos=0,vel=0,slide=0,supH=0,stumble=0,invuln=0,gap=1,copX=1,copT=0,tun=0,nextScn=0,bigCool=0,poleFlip=1;
var items=[],scn=[],msgs=[],sfxNear=false,sfxChase=false;
var SPEED0=0.55,SPEEDMAX=1.45,G=14,V0=5.4;
function S(z){return 1/(1+K*z);}
function Y(z){return hor+(py-hor)*S(z);}
function X(lx,z){return cx+lx*LS*S(z);}
function clamp(v,a,b){return v<a?a:(v>b?b:v);}
function sh(c,d){var n=parseInt(c.slice(1),16),r=clamp((n>>16)+d,0,255),gg=clamp(((n>>8)&255)+d,0,255),b=clamp((n&255)+d,0,255);return 'rgb('+r+','+gg+','+b+')';}
function post(m){try{window.chrome.webview.postMessage(m);}catch(e){}}
var sky=[],sd=7;function rnd(){sd=(sd*16807)%2147483647;return sd/2147483647;}
for(var i=0;i<26;i++){sky.push({x:i/26,w:0.03+rnd()*0.06,h:0.04+rnd()*0.10,c:rnd()});}
function msg(t,c){msgs.push({t:t,c:c,life:1.1});}
function shuf(a){for(var i=a.length-1;i>0;i--){var j=Math.floor(Math.random()*(i+1)),t=a[i];a[i]=a[j];a[j]=t;}return a;}

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
var CTRAIN={F:'#3f5c80',S:'#2c3f5a',T:'#5a7392'};
function trainFront(r){
 var ww=(r.xr-r.xl)*0.34,wh=(r.yb-r.yt)*0.26;g.fillStyle='#cfe3f7';g.fillRect(r.xl+(r.xr-r.xl)*0.10,r.yt+(r.yb-r.yt)*0.14,ww,wh);g.fillRect(r.xr-(r.xr-r.xl)*0.10-ww,r.yt+(r.yb-r.yt)*0.14,ww,wh);
 g.fillStyle='#ffe9a0';var hl=(r.xr-r.xl)*0.12;g.fillRect(r.xl+(r.xr-r.xl)*0.12,r.yb-(r.yb-r.yt)*0.22,hl,hl*0.7);g.fillRect(r.xr-(r.xr-r.xl)*0.12-hl,r.yb-(r.yb-r.yt)*0.22,hl,hl*0.7);
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
function drawSimple(it){
 var lx=it.lane-1,s=S(it.z),x=X(lx,it.z),y=Y(it.z),u=LS*s;
 if(it.t==='coin'){var cw=Math.abs(Math.cos(time*6+it.z*9))*0.13+0.03,cy=y-u*(0.5+(it.h||0));g.fillStyle='#ffc933';g.strokeStyle='#a87a10';g.lineWidth=Math.max(1,u*0.03);g.beginPath();g.ellipse(x,cy,u*cw,u*0.13,0,0,6.3);g.fill();g.stroke();}
 else if(it.t==='bar'){var w=u*0.8,h=u*0.40;g.fillStyle='#e9e9e0';g.fillRect(x-w/2,y-h,w,h*0.5);g.fillStyle='#d23c2d';for(var k2=0;k2<4;k2++)g.fillRect(x-w/2+k2*w/4,y-h,w/8,h*0.5);g.fillStyle='#555';g.fillRect(x-w/2,y-h,u*0.06,h);g.fillRect(x+w/2-u*0.06,y-h,u*0.06,h);}
 else if(it.t==='over'){var hh=u*0.72,th=u*0.22,pw=u*0.08,xl=X(-1.5,it.z),xr=X(1.5,it.z);g.fillStyle='#555';g.fillRect(xl-pw,y-hh-th,pw,hh+th);g.fillRect(xr,y-hh-th,pw,hh+th);g.fillStyle='#f2a31b';g.fillRect(xl-pw,y-hh-th,xr-xl+2*pw,th);g.fillStyle='#222';for(var k3=0;k3<12;k3++){var sx=xl-pw+k3*(xr-xl+2*pw)/12;g.fillRect(sx,y-hh-th,(xr-xl+2*pw)/24,th);}}
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
 var i;
 if(e.t==='blk'){
  var c={F:e.c,S:sh(e.c,-16),T:sh(e.c,16)};
  pushBox(ops,e.lxa,e.lxb,e.z,e.z+e.len,0,e.h,c,{front:function(r){
   if(e.plain)return;var cols=3,rows=Math.max(2,Math.floor(e.h*2.2)),ww=(r.xr-r.xl)/(cols*2+1),hh=(r.yb-r.yt)/(rows*2+1);
   for(var ry=0;ry<rows;ry++)for(var rx=0;rx<cols;rx++){if(((e.seed>>(ry*cols+rx))&1)===0)continue;g.fillStyle='rgba(255,214,120,0.75)';g.fillRect(r.xl+ww*(1+rx*2),r.yt+hh*(1+ry*2),ww,hh);}}});
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
 // top of the wall
 quad([X(-GW,za),yn-GW*0+(-3.4*LS*sn)],[X(GW,za),yn-3.4*LS*sn],[X(GW,zb),Y(zb)-3.4*LS*S(zb)],[X(-GW,zb),Y(zb)-3.4*LS*S(zb)],'#9a9ea6');
 // the face, with an opening for every lane
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
 var ops=[],i,it,e;
 for(i=0;i<items.length;i++){it=items[i];
  if(it.block)continue;
  if(it.t==='train')pushBox(ops,it.lane-1-0.43,it.lane-1+0.43,it.z,it.z+it.len,0,TH,CTRAIN,{win:true,front:trainFront});
  else if(it.t==='ramp')pushRamp(ops,it);
  else if(it.z>=ZMIN)(function(q){addOp(ops,q.z,q.z,function(){drawSimple(q);});})(it);
 }
 for(i=0;i<scn.length;i++)buildScnOps(ops,scn[i]);
 ops.sort(function(a,b){return b.k-a.k;});
 return ops;
}

/* ---------- gameplay ---------- */
function reset(){
 mode='play';paused=false;score=0;coins=0;speed=SPEED0;speedMul=1;dist=0;lane=1;prevLane=1;laneX=1;pos=0;vel=0;slide=0;supH=0;items=[];scn=[];msgs=[];
 nextRow=0.5;safe=1;shake=0;stumble=0;invuln=0;gap=1;copX=1;copT=0;tun=0;bigCool=0.8;nextScn=0.3;sfxNear=false;sfxChase=false;
 post('stopall');
 for(var z=0.05;z<Z0;z+=0.22)spawnScn(z);
}
function ended(){mode='over';overAt=time;shake=0.5;copT=0;sfxNear=false;sfxChase=false;post('slowed');if(score>best){best=score;try{localStorage.setItem('rr_best',String(Math.floor(best)));}catch(e){}}}
function trip(){stumble=0.9;invuln=0.9;gap-=0.55;shake=0.3;msg('TRIPPED!','#ffb02e');sfxChase=true;post('enrique');if(gap<=0.12){gap=0;ended();}}
function closeCall(){score+=25;gap=Math.max(0,gap-0.08);msg('CLOSE CALL!','#ffe45c');if(gap<=0.12){gap=0;ended();}}
function support(){
 var h=0;
 for(var i=0;i<items.length;i++){var it=items[i];if(it.t!=='train'&&it.t!=='ramp')continue;if(it.block)continue;if(Math.abs(laneX-it.lane)>=0.52)continue;
  if(it.z<=0.005&&it.z+it.len>=-0.005){
   var th=it.th||TH,gh=it.t==='train'?th:TH*Math.min(1,(0-it.z)/it.len);
   if(it.t==='train'&&pos<gh-0.3)continue;   /* never lifted up onto a roof from far below */
   if(gh>h)h=gh;}}
 return h;
}
function rampH(it){return TH*Math.min(1,Math.max(0,(0-it.z)/it.len));}
function addGate(z,st){
 var depth=0.30,l;scn.push({t:'gate',z:z,depth:depth,st:st});
 for(l=0;l<3;l++){
  if(st[l]==='closed')items.push({t:'train',lane:l,z:z,len:depth,th:3.4,block:1});
  else if(st[l]==='train')items.push({t:'train',lane:l,z:z+0.06,len:0.62});
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
 if(r<0.18){ls=shuf([0,1,2]).slice(0,Math.random()<0.5?2:1);for(i=0;i<ls.length;i++)items.push({t:'bar',lane:ls[i],z:Z0});}
 else if(r<0.32){
  var c=[];for(i=0;i<3;i++)if(Math.abs(i-safe)<=1)c.push(i);
  var f=c[Math.floor(Math.random()*c.length)],others=[],any=false;for(i=0;i<3;i++)if(i!==f)others.push(i);
  for(i=0;i<others.length;i++){if(Math.random()<0.8){items.push({t:'train',lane:others[i],z:Z0,len:0.34});any=true;}}
  if(!any)items.push({t:'train',lane:others[0],z:Z0,len:0.34});
  safe=f;}
 else if(r<0.40){items.push({t:'over',lane:1,z:Z0});}
 else if(r<0.50){ls=shuf([0,1,2]).slice(0,Math.random()<0.4?2:1);for(i=0;i<ls.length;i++)items.push({t:'duck',lane:ls[i],z:Z0});}
 else if(r<0.64){
  var L=Math.floor(Math.random()*3),rl=0.28,tl=0.8+Math.random()*0.5;
  items.push({t:'ramp',lane:L,z:Z0,len:rl});items.push({t:'train',lane:L,z:Z0+rl,len:tl});
  for(i=0;i<4;i++)items.push({t:'coin',lane:L,z:Z0+rl+0.12+i*0.12,h:TH});
  var N=L===1?(Math.random()<0.5?0:2):1,nz=Z0+rl+tl*0.45+0.12+Math.random()*0.1,nl=0.7+Math.random()*0.4;
  items.push({t:'train',lane:N,z:nz,len:nl});
  for(i=0;i<3;i++)items.push({t:'coin',lane:N,z:nz+0.12+i*0.14,h:TH});
  var free=[0,1,2].filter(function(x){return x!==L&&x!==N;})[0];safe=free;
  nextRow=Math.max(tl+rl,nz-Z0+nl)+0.6;return;}
 else if(r<0.80){spawnGate();return;}
 else{l=Math.floor(Math.random()*3);for(i=0;i<5;i++)items.push({t:'coin',lane:l,z:Z0+i*0.09,h:0});}
 nextRow=0.52+Math.random()*0.35;
}
function update(dt){
 time+=dt;
 if(shake>0)shake=Math.max(0,shake-dt);
 for(var m=msgs.length-1;m>=0;m--){msgs[m].life-=dt;if(msgs[m].life<=0)msgs.splice(m,1);}
 if(mode==='over'){
  gap=Math.max(0,gap-dt*2.2);if(gap<=0.02)copT+=dt;
  copX+=(laneX-copX)*Math.min(1,dt*8);
  if(pos>supH||vel>0){vel-=G*dt;pos+=vel*dt;if(pos<=supH){pos=supH;vel=0;}}
  return;}
 if(mode!=='play'||paused)return;
 var i,it;
 stumble=Math.max(0,stumble-dt);invuln=Math.max(0,invuln-dt);
 speedMul=stumble>0?0.55+0.45*(1-stumble/0.9):1;
 speed=Math.min(SPEEDMAX,SPEED0+dist*0.0035)*speedMul;
 dist+=speed*dt;score+=speed*dt*10;gap=Math.min(1,gap+dt*0.07);
 laneX+=(lane-laneX)*Math.min(1,dt*14);copX+=(laneX-copX)*Math.min(1,dt*3);
 if(slide>0)slide-=dt;
 for(i=0;i<items.length;i++)items[i].z-=speed*dt;
 for(i=0;i<scn.length;i++)scn[i].z-=speed*dt;
 for(i=scn.length-1;i>=0;i--){var e=scn[i];if(e.z+(e.len||e.depth||0.1)<ZMIN-0.1)scn.splice(i,1);}
 bigCool-=speed*dt;
 nextRow-=speed*dt;if(nextRow<=0)spawnRow();
 nextScn-=speed*dt;if(nextScn<=0){spawnScn(Z0+0.05);nextScn=0.2+Math.random()*0.16;}
 /* sound cues for the chase */
 var nearNow=gap<0.9;if(nearNow!==sfxNear){sfxNear=nearNow;post(nearNow?'amb1':'amb0');}
 if(sfxChase&&gap>=0.9){sfxChase=false;post('chaseend');}
 /* collisions (using where the player was this frame) */
 for(i=items.length-1;i>=0;i--){
  it=items[i];var d=Math.abs(laneX-it.lane),near=d<0.6,len=it.len||0,inW=it.z<0.04&&it.z>-0.04,th=it.th||TH;
  if(it.t==='coin'){if(it.z<0.05&&it.z>-0.06&&near&&Math.abs(pos-(it.h||0))<0.7){coins++;score+=10;gap=Math.min(1,gap+0.012);items.splice(i,1);continue;}}
  else if(it.t==='bar'){
   if(inW&&near){it.minPos=Math.min(it.minPos===undefined?9:it.minPos,pos);if(pos<0.45&&invuln<=0&&!it.hit){it.hit=1;trip();if(mode==='over')return;}}
   if(it.z<=-0.04&&!it.hit&&!it.cc&&it.minPos!==undefined&&it.minPos<0.62){it.cc=1;closeCall();if(mode==='over')return;}
  }
  else if(it.t==='over'||it.t==='duck'){
   var okS=slide>0&&pos<0.3;
   if(inW&&(it.t==='over'||near)){
    if(!okS&&invuln<=0&&!it.hit){it.hit=1;trip();if(mode==='over')return;}
    else if(okS&&slide<0.14&&!it.cc&&!it.hit){it.cc=1;closeCall();if(mode==='over')return;}
   }
  }
  else if(it.t==='train'){
   if(it.z<=0.005&&it.z>-0.04&&near&&pos<th-0.12&&!it.hit){ended();return;}
   if(inW&&d>=0.6&&d<0.85&&!it.cc&&pos<th-0.12){it.cc=1;closeCall();if(mode==='over')return;}
   if(it.z<=-0.04&&it.z+len>=0&&near&&pos<th-0.25){lane=prevLane;if(invuln<=0&&!it.hit){it.hit=1;trip();if(mode==='over')return;}}
  }
  else if(it.t==='ramp'){
   if(it.z<=0&&it.z+len>=0&&near&&pos<rampH(it)-0.25){lane=prevLane;if(invuln<=0&&!it.hit){it.hit=1;trip();if(mode==='over')return;}}
  }
  if(it.z+len<ZMIN-0.02)items.splice(i,1);
 }
 supH=support();
 if(pos>supH+0.0005||vel>0){vel-=G*dt;pos+=vel*dt;if(pos<=supH&&vel<=0){pos=supH;vel=0;}}
 else{pos=supH;vel=0;}
 var inT=false;for(i=0;i<scn.length;i++){if(scn[i].t==='gate'&&scn[i].z<=0&&scn[i].z+scn[i].depth>=0){inT=true;break;}}
 tun+=((inT?1:0)-tun)*Math.min(1,dt*8);
}

/* ---------- drawing ---------- */
function drawBg(){
 var gr=g.createLinearGradient(0,0,0,hor);gr.addColorStop(0,'#0a1a2a');gr.addColorStop(1,'#d98a4a');g.fillStyle=gr;g.fillRect(0,0,W,hor+1);
 for(var i=0;i<sky.length;i++){var b=sky[i];g.fillStyle=b.c<0.5?'#1b2433':'#232d3f';g.fillRect(b.x*W,hor-b.h*H,b.w*W,b.h*H);}
 g.fillStyle='#14170f';g.fillRect(0,hor,W,H-hor);
}
function drawTrack(){
 var zn=ZMIN,zf=3,i;
 quad([X(-1.5,zf),Y(zf)],[X(1.5,zf),Y(zf)],[X(1.5,zn),Y(zn)],[X(-1.5,zn),Y(zn)],'#383a31');
 for(i=-1;i<15;i++){var z=i*0.22-(dist%0.22);if(z<zn||z>zf)continue;g.strokeStyle='rgba(80,62,44,0.95)';g.lineWidth=Math.max(1,4*S(z));g.beginPath();g.moveTo(X(-1.7,z),Y(z));g.lineTo(X(1.7,z),Y(z));g.stroke();}
 var ls=[-0.5,0.5,-1.5,1.5];
 for(i=0;i<4;i++){g.strokeStyle=i<2?'rgba(255,255,255,0.20)':'#9aa084';g.lineWidth=i<2?1.2:Math.max(2,LS*0.035);g.beginPath();g.moveTo(X(ls[i],zf),Y(zf));g.lineTo(X(ls[i],zn),Y(zn));g.stroke();}
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
 if(invuln>0&&Math.floor(time*16)%2===0)g.globalAlpha=0.45;
 g.save();
 if(stumble>0){g.translate(x,yb);g.rotate(Math.sin(time*34)*0.10);g.translate(-x,-yb);}
 var air=pos>supH+0.02;
 if(!sl){
  var sw=air?0.3:Math.sin(ph)*0.35;g.strokeStyle='#2c3a4d';g.lineWidth=LS*0.09;g.lineCap='round';
  g.beginPath();g.moveTo(x-w*0.2,yb-h*0.38);g.lineTo(x-w*0.2+sw*LS*0.3,yb);g.stroke();
  g.beginPath();g.moveTo(x+w*0.2,yb-h*0.38);g.lineTo(x+w*0.2-sw*LS*0.3,yb);g.stroke();
  g.fillStyle='#5d8f3a';g.beginPath();g.moveTo(x-w/2,yb-h*0.38);g.lineTo(x-w*0.42,yb-h*0.92);g.lineTo(x+w*0.42,yb-h*0.92);g.lineTo(x+w/2,yb-h*0.38);g.closePath();g.fill();
  g.fillStyle='#e8c39a';g.beginPath();g.arc(x,yb-h*1.04,LS*0.13,0,6.3);g.fill();
  g.fillStyle='#ee7a1c';g.beginPath();g.arc(x,yb-h*1.06,LS*0.14,3.14,6.28);g.fill();g.fillRect(x-LS*0.14,yb-h*1.07,LS*0.30,LS*0.035);
 }else{
  g.fillStyle='#2c3a4d';g.fillRect(x-w*0.6,yb-h*0.30,w*1.3,h*0.30);
  g.fillStyle='#5d8f3a';g.beginPath();g.ellipse(x,yb-h*0.62,w*0.62,h*0.34,0,0,6.3);g.fill();
  g.fillStyle='#e8c39a';g.beginPath();g.arc(x+w*0.55,yb-h*0.66,LS*0.11,0,6.3);g.fill();
  g.fillStyle='#ee7a1c';g.beginPath();g.arc(x+w*0.55,yb-h*0.70,LS*0.12,3.14,6.28);g.fill();
 }
 g.restore();g.globalAlpha=1;
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
function txt(t,y,px,col,al){g.font='bold '+px+'px Consolas,Menlo,monospace';g.textAlign=al||'center';g.fillStyle=col||'#e6efd6';g.fillText(t,(al==='left')?8:((al==='right')?W-8:W/2),y);}
function shade(a){g.fillStyle='rgba(8,10,6,'+a+')';g.fillRect(0,0,W,H);}
function runOp(o){g.globalAlpha=o.a;o.f();g.globalAlpha=1;}
function draw(){
 g.save();if(shake>0){g.translate((Math.random()-0.5)*shake*14,(Math.random()-0.5)*shake*14);}
 drawBg();drawTrack();
 var ops=buildOps(),i;
 for(i=0;i<ops.length;i++)if(ops[i].k>0)runOp(ops[i]);
 drawPlayer();
 for(i=0;i<ops.length;i++)if(ops[i].k<=0)runOp(ops[i]);
 drawCop();
 g.restore();
 if(tun>0.02){g.fillStyle='rgba(0,0,0,'+(tun*0.38)+')';g.fillRect(0,0,W,H);}
 var fs=Math.max(11,Math.min(22,W*0.052));
 txt('SCORE '+Math.floor(score),fs+6,fs,'#e6efd6','left');txt('COINS '+coins,fs+6,fs,'#ffc933','right');
 for(i=0;i<msgs.length&&mode==='play';i++){var m=msgs[i];g.globalAlpha=clamp(m.life*2,0,1);txt(m.t,H*0.20-(1.1-m.life)*18,Math.max(14,W*0.08),m.c);g.globalAlpha=1;}
 if(mode==='play'&&gap<0.9){var bw=W*0.28;g.fillStyle='rgba(0,0,0,0.45)';g.fillRect(W/2-bw/2,fs*2.1,bw,6);g.fillStyle=gap<0.35?'#e0553f':'#ffb02e';g.fillRect(W/2-bw/2,fs*2.1,bw*clamp(gap/0.9,0,1),6);}
 if(mode==='title'){shade(0.62);var tz=Math.max(16,W*0.115);txt('RUN AWAY',H*0.22,tz,'#9bd45a');txt('FROM NIVE',H*0.22+tz*1.1,tz,'#9bd45a');txt('BEST '+Math.floor(best),H*0.22+tz*1.1+fs*1.9,fs,'#e6efd6');txt('PRESS ANY KEY',H*0.45,fs*1.2,'#ffc933');
  var y0=H*0.56,ls=fs*1.45,sm=fs*0.78;
  txt('LEFT / RIGHT or A / D  = change lane',y0,sm,'#c8d3b4');txt('UP or W or SPACE  = jump',y0+ls,sm,'#c8d3b4');txt('DOWN or S  = slide',y0+ls*2,sm,'#c8d3b4');
  txt('jump hurdles, slide under bars,',y0+ls*3.2,sm*0.85,'#8a9a78');txt('find the open tunnel in each wall,',y0+ls*4.1,sm*0.85,'#8a9a78');txt('ride trains up the yellow ramps.',y0+ls*5,sm*0.85,'#8a9a78');}
 else if(mode==='over'){shade(0.28);txt('CAUGHT!',H*0.13,Math.max(16,W*0.10),'#e0553f');txt('SCORE '+Math.floor(score)+'   BEST '+Math.floor(best),H*0.13+fs*1.7,fs,'#e6efd6');if(time-overAt>0.6)txt('SPACE / ENTER = RUN AGAIN',H*0.13+fs*3.4,fs*0.9,'#9bd45a');}
 else if(paused){shade(0.5);txt('PAUSED',H*0.45,Math.max(16,W*0.09),'#9bd45a');txt('press any key',H*0.45+fs*2,fs,'#e6efd6');}
}
function act(code,key){
 var c=code||'',k=(key||'').toLowerCase();
 if(c==='ArrowLeft'||c==='KeyA'||k==='arrowleft'||k==='a')return 'L';
 if(c==='ArrowRight'||c==='KeyD'||k==='arrowright'||k==='d')return 'R';
 if(c==='ArrowUp'||c==='KeyW'||k==='arrowup'||k==='w')return 'U';
 if(c==='ArrowDown'||c==='KeyS'||k==='arrowdown'||k==='s')return 'D';
 if(c==='Space'||k===' ')return 'SP';
 if(c==='Enter'||k==='enter')return 'EN';
 if(c==='KeyP'||k==='p')return 'P';
 return '';
}
function jump(){if(pos<=supH+0.02){vel=V0;slide=0;}}
addEventListener('keydown',function(e){
 var a=act(e.code,e.key);
 if(a)e.preventDefault();
 if(mode==='title'){if(!e.repeat&&e.key!=='Tab'&&e.key!=='Shift'&&e.key!=='Control'&&e.key!=='Alt')reset();return;}
 if(mode==='over'){if(time-overAt>0.6&&(a==='SP'||a==='EN'))reset();return;}
 if(paused){paused=false;return;}
 if(e.repeat)return;
 if(a==='L'){if(lane>0){prevLane=lane;lane--;}}
 else if(a==='R'){if(lane<2){prevLane=lane;lane++;}}
 else if(a==='U'||a==='SP')jump();
 else if(a==='D'){if(pos>supH+0.02)vel=-9;else slide=0.6;}
 else if(a==='P'){paused=true;post('stopall');sfxNear=false;}
});
addEventListener('blur',function(){if(mode==='play'){paused=true;post('stopall');sfxNear=false;}});
cv.addEventListener('mousedown',function(){window.focus();cv.focus();if(mode==='title')reset();else if(paused)paused=false;});
window.__rr=function(){return JSON.stringify({mode:mode,lane:lane,pos:Math.round(pos*100)/100,air:pos>supH+0.02,slide:slide>0,score:Math.floor(score),coins:coins,gap:Math.round(gap*100)/100,stumble:stumble>0,paused:paused,items:items.length,scn:scn.length,tunnel:tun>0.5,w:W,h:H});};
window.__rrSpawn=function(t,l,z,len,h){items.push({t:t,lane:l,z:z,len:len!==undefined?len:(t==='ramp'?0.28:0.34),h:h||0});};
window.__rrGate=function(z,list){addGate(z,list.split(','));};
window.__rrScn=function(t,z){scn.push({t:t,z:z});};
window.__rrGateStats=function(){var cnt={open:0,train:0,closed:0},bad=0,mixed=0;for(var n=0;n<300;n++){scn=[];items=[];safe=Math.floor(Math.random()*3);spawnGate();var st=scn[0].st,o=0,seen={};for(var i=0;i<3;i++){cnt[st[i]]++;seen[st[i]]=1;if(st[i]==='open')o++;}if(o<1)bad++;}scn=[];items=[];return JSON.stringify({cnt:cnt,bad:bad});};
window.__rrStep=function(dt){update(dt);};
window.__rrStart=function(){reset();};
window.__rrClear=function(){items=[];scn=scn.filter(function(e){return e.t!=='gate';});nextRow=99;};
window.__rrFreeze=function(f){frozen=f;};
window.__rrSetGap=function(v){gap=v;};
var last=0;
function loop(ts){var dt=last?Math.min(0.05,(ts-last)/1000):0.016;last=ts;if(!frozen)update(dt);draw();requestAnimationFrame(loop);}
requestAnimationFrame(loop);
cv.focus();
})();
</script></body></html>
";
    }

    partial class OverlayForm
    {
        // ---------- game panel (Rail Runner) ----------
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
            EnsureGameView();                       // the first time this makes the second page view (it loads the game when ready)
            if (gameCtl != null) GameLoad(false);
            if (!open && !animating) OpenTablet();   // asked for while minimized: bring the tablet up
            else if (webShown) PositionWeb();
            lastSig = ""; RenderState(true);
            if (!Program.TestMode && open) { Native.ForceForeground(webHost.Handle); FocusGame(); }
        }

        // Stops the game (the page is blanked, so it stops running) and hides the panel. Safe to call any time.
        void CloseGame()
        {
            if (!gameVisible) return;
            gameVisible = false;
            gameLoaded = false;
            Sfx.StopAll();
            if (game != null) game.Visible = false;
            if (gameCtl != null)
            {
                gameCtl.IsVisible = false;
                try { gameCtl.CoreWebView2.Navigate("about:blank"); } catch { }
            }
        }

        void GameLoad(bool force)
        {
            if (gameCtl == null) return;
            if (gameLoaded && !force) return;
            try
            {
                gameInternalNav = true;
                gameCtl.CoreWebView2.NavigateToString(RunnerGame.Html);
                gameLoaded = true;
            }
            catch { gameInternalNav = false; }
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
                c.DefaultBackgroundColor = Color.FromArgb(11, 14, 9);
                c.IsVisible = false;
                SecureGameView(c.CoreWebView2);
                gameCtl = c;
                if (gameVisible)
                {
                    if (webShown) PositionWeb();
                    GameLoad(false);
                    if (open && !Program.TestMode) FocusGame();
                }
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

        // The game's page view is locked right down: it can only show the game page itself (no web addresses, no pop-ups,
        // no downloads, no permissions) and every network request is refused.
        void SecureGameView(CoreWebView2 wv)
        {
            CoreWebView2Settings st = wv.Settings;
            st.AreDevToolsEnabled = false;
            st.IsStatusBarEnabled = false;
            st.IsWebMessageEnabled = true;           // only the game's own page can run here; it may ask for its sound effects (Sfx.Message accepts a short fixed list of words)
            st.AreHostObjectsAllowed = false;
            st.IsZoomControlEnabled = false;
            st.IsPasswordAutosaveEnabled = false;
            st.IsGeneralAutofillEnabled = false;
            st.AreDefaultContextMenusEnabled = false;
            wv.WebMessageReceived += delegate (object s, CoreWebView2WebMessageReceivedEventArgs e)
            {
                string m = null;
                try { m = e.TryGetWebMessageAsString(); } catch { }
                if (m != null && m.Length <= 12) Sfx.Message(m);
            };

            wv.NavigationStarting += delegate (object s, CoreWebView2NavigationStartingEventArgs e)
            {
                if (e.Uri.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase)) return;
                if (e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && gameInternalNav) { gameInternalNav = false; return; }
                e.Cancel = true;
            };
            wv.NewWindowRequested += delegate (object s, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; };
            wv.LaunchingExternalUriScheme += delegate (object s, CoreWebView2LaunchingExternalUriSchemeEventArgs e) { e.Cancel = true; };
            wv.PermissionRequested += delegate (object s, CoreWebView2PermissionRequestedEventArgs e) { e.State = CoreWebView2PermissionState.Deny; e.Handled = true; };
            wv.DownloadStarting += delegate (object s, CoreWebView2DownloadStartingEventArgs e) { e.Cancel = true; e.Handled = true; };
            wv.AddWebResourceRequestedFilter("http*", CoreWebView2WebResourceContext.All);
            wv.WebResourceRequested += delegate (object s, CoreWebView2WebResourceRequestedEventArgs e)
            {
                e.Response = webEnv.CreateWebResourceResponse(null, 403, "Blocked", "Content-Type: text/plain");
            };
        }

        // ---------- developer test: --gametest <folder> [atak|pilot] ----------
        async System.Threading.Tasks.Task<string> GameEval(string js)
        {
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

        public async void RunGameTest(string dir)
        {
            StringBuilder log = new StringBuilder();
            Action<string> L = delegate (string s) { log.AppendLine(s); try { File.WriteAllText(Path.Combine(dir, "gametest.txt"), log.ToString()); } catch { } };
            try
            {
                Sfx.Volume = 120;
                int waited = 0;
                while ((ctl == null || !webShown || animating) && waited < 20000) { await Delay(200); waited += 200; }
                L("ready after " + waited + " ms; open=" + open + " webShown=" + webShown + " layout=" + cfg.Layout);
                await Delay(2500);
                int mainW0 = ctl.Bounds.Width, hostW = webHost.ClientSize.Width;
                L("before: page view width " + mainW0 + " of window " + hostW);
                Shot(Path.Combine(dir, "g01_before.png"));

                if (!IsPilot)
                {
                    PointF gb = GameBtnCenter(frame.Width);
                    L("GAME button centre " + (int)gb.X + "," + (int)gb.Y + " -> hit-test says: " + HitExtra(new Point((int)gb.X, (int)gb.Y)) + "   (expect GAME)");
                    L("other buttons still hit: live=" + HitExtra(Point.Round(LiveBtnCenter(frame.Width))) + " timer=" + HitExtra(Point.Round(TimerBtnCenter(frame.Width))) + " notes=" + HitExtra(Point.Round(NotesBtnCenter(frame.Width))));
                }
                L("label text: " + KeyTextFor("GAME"));

                PressExtra("GAME");
                waited = 0; while (gameCtl == null && waited < 10000) { await Delay(200); waited += 200; }
                L("game view created after " + waited + " ms: " + (gameCtl != null) + "   visible flag=" + gameVisible + "   label: " + KeyTextFor("GAME"));
                await Delay(1500);
                double frac = game.Width / (double)hostW;
                L("panel " + game.Bounds + " = " + frac.ToString("0.000") + " of the window width   (expect about 0.33)");
                L("page view narrowed: " + mainW0 + " -> " + ctl.Bounds.Width + "   right edge " + ctl.Bounds.Right + " vs panel left " + game.Left);
                L("game view bounds " + gameCtl.Bounds + " vs ViewArea " + game.ViewArea + "   visible=" + gameCtl.IsVisible);
                L("game page: source=" + gameCtl.CoreWebView2.Source.Substring(0, Math.Min(30, gameCtl.CoreWebView2.Source.Length)) + "   errors=" + await GameEval("window.__err.join('; ')") + "   state=" + await GameEval("window.__rr()"));
                Shot(Path.Combine(dir, "g02_title.png"));

                // start (any key) and steer with arrows and WASD
                await GameEval("window.__rrFreeze(true)");
                await GameKey("Enter", "Enter", 13, null); await Delay(200);
                L("after pressing a key on the title screen: " + await GameEval("window.__rr()"));
                await GameEval("window.__rrClear()");
                string st = "";
                await GameKey("ArrowLeft", "ArrowLeft", 37, null); st += "left->lane " + Lane(await GameEval("window.__rr()")) + "  ";
                await GameKey("ArrowLeft", "ArrowLeft", 37, null); st += "left again->lane " + Lane(await GameEval("window.__rr()")) + " (stays at the edge)  ";
                await GameKey("d", "KeyD", 68, "d"); st += "D->lane " + Lane(await GameEval("window.__rr()")) + "  ";
                await GameKey("ArrowRight", "ArrowRight", 39, null); st += "right->lane " + Lane(await GameEval("window.__rr()")) + "  ";
                await GameKey("a", "KeyA", 65, "a"); st += "A->lane " + Lane(await GameEval("window.__rr()")) + "  ";
                L("steering: " + st);
                string setup = "window.__rrStart();window.__rrFreeze(true);window.__rrClear();";
                await GameEval(setup); await GameKey("ArrowUp", "ArrowUp", 38, null); L("up -> " + await GameEval("window.__rr()"));
                await GameEval(setup); await GameKey("s", "KeyS", 83, "s"); L("S -> " + await GameEval("window.__rr()"));
                await GameEval(setup); await GameKey("w", "KeyW", 87, "w"); L("W -> " + await GameEval("window.__rr()"));

                // the rules (stepped by hand, so the result never depends on timing)
                Func<int, string> step = delegate (int n) { return "(function(){for(var i=0;i<" + n + ";i++)window.__rrStep(0.01);return window.__rr();})()"; };
                L("rules:");
                await GameEval(setup + "window.__rrSpawn('bar',1,0.2);");
                L("  hurdle, no jump           (expect TRIP: still playing, gap 0.45) -> " + Short(await GameEval(step(70))));
                await GameEval(setup + "window.__rrSpawn('bar',1,0.2);"); await GameKey("ArrowUp", "ArrowUp", 38, null);
                L("  hurdle, jumped in time    (expect clean: gap 1)                  -> " + Short(await GameEval(step(70))));
                await GameEval(setup); await GameKey("ArrowUp", "ArrowUp", 38, null); await GameEval(step(60));
                await GameEval("window.__rrSpawn('bar',1,0.0);");
                L("  hurdle, jumped too late   (expect CLOSE CALL: gap 0.92, +25)     -> " + Short(await GameEval(step(20))));
                await GameEval(setup + "window.__rrSpawn('over',1,0.2);");
                L("  gantry, standing          (expect TRIP)                           -> " + Short(await GameEval(step(70))));
                await GameEval(setup + "window.__rrSpawn('over',1,0.2);"); await GameKey("ArrowDown", "ArrowDown", 40, null);
                L("  gantry, sliding           (expect clean)                          -> " + Short(await GameEval(step(70))));
                await GameEval(setup + "window.__rrSpawn('duck',1,0.2);");
                L("  low bar, standing         (expect TRIP)                           -> " + Short(await GameEval(step(70))));
                await GameEval(setup + "window.__rrSpawn('duck',1,0.2);"); await GameKey("ArrowDown", "ArrowDown", 40, null);
                L("  low bar, sliding under    (expect clean)                          -> " + Short(await GameEval(step(70))));
                await GameEval(setup + "window.__rrSpawn('train',1,0.2);");
                L("  train head-on             (expect CAUGHT)                         -> " + Short(await GameEval(step(70))));
                await GameEval(setup + "window.__rrSpawn('train',0,0.2);window.__rrSpawn('train',2,0.2);");
                L("  trains in other lanes     (expect clean)                          -> " + Short(await GameEval(step(70))));
                await GameEval(setup + "window.__rrSpawn('train',0,-0.1,0.9);"); await GameKey("ArrowLeft", "ArrowLeft", 37, null);
                L("  steer into a train's side (expect TRIP and pushed back to lane 1) -> " + Short(await GameEval(step(30))));
                await GameEval(setup + "window.__rrSpawn('bar',1,0.2);"); await GameEval(step(70));
                await GameEval(step(100) + ";window.__rrSpawn('bar',1,0.0);");
                L("  second trip within a couple of seconds (expect CAUGHT)            -> " + Short(await GameEval(step(20))));
                await GameEval(setup + "window.__rrSpawn('coin',1,0.2);");
                L("  coin on the ground        (expect coins 1)                        -> " + Short(await GameEval(step(70))));

                L("trains you can ride:");
                await GameEval(setup + "window.__rrSpawn('train',1,0.4,0.9);"); await Delay(50);
                await GameEval(step(40)); await GameKey("ArrowUp", "ArrowUp", 38, null);
                L("  jump at a train without a ramp (expect CAUGHT, roofs are too high to jump onto) -> " + Short(await GameEval(step(80))));
                await GameEval(setup + "window.__rrSpawn('ramp',1,0.2,0.28);window.__rrSpawn('train',1,0.48,0.9);window.__rrSpawn('coin',1,0.9,0,1.5);");
                L("  ramp then train, 1.2 s later (expect pos 1.5 = on the roof, still playing) -> " + Short(await GameEval(step(120))));
                await GameEval("window.__rrSpawn('train',0,0.15,0.8);");
                await GameEval(step(70)); await GameKey("ArrowLeft", "ArrowLeft", 37, null);
                L("  hopped onto the next train's roof (expect lane 0, pos 1.5)                 -> " + Short(await GameEval(step(25))));
                L("  rode on until the roofs end (expect pos 0, playing, coins 1)               -> " + Short(await GameEval(step(300))));

                // walls with three tunnels: one lane open, others a train inside or shuttered
                L("tunnel walls:");
                await GameEval(setup + "window.__rrGate(0.3,'closed,open,train');");
                L("  middle tunnel open        (expect clean, tunnel=true while inside) -> " + Short(await GameEval(step(70))) + "   later: " + Short(await GameEval(step(60))));
                await GameEval(setup + "window.__rrGate(0.3,'closed,open,train');"); await GameKey("ArrowLeft", "ArrowLeft", 37, null);
                L("  into the shuttered tunnel (expect CAUGHT)                           -> " + Short(await GameEval(step(80))));
                await GameEval(setup + "window.__rrGate(0.3,'closed,open,train');"); await GameKey("ArrowRight", "ArrowRight", 39, null);
                L("  into the tunnel with a train (expect CAUGHT)                        -> " + Short(await GameEval(step(80))));
                await GameEval(setup + "window.__rrGate(0.3,'closed,open,closed');"); await GameEval(step(70)); await GameKey("ArrowLeft", "ArrowLeft", 37, null);
                L("  steering into a wall inside the tunnel (expect TRIP, back in lane 1) -> " + Short(await GameEval(step(15))));
                L("  300 random walls: " + await GameEval("window.__rrGateStats()") + "   (expect bad:0 and a mix of open / train / closed)");

                // pictures: a wall ahead, then passing through it
                await GameEval(setup + "for(var i=0;i<260;i++){window.__rrStep(0.02);window.__rrClear();}window.__rrGate(0.62,'closed,open,train');window.__rrStep(0.001);");
                await Delay(400);
                Shot(Path.Combine(dir, "g03_gate_ahead.png"));
                await GameEval(step(45)); await Delay(300);
                Shot(Path.Combine(dir, "g04_gate_closer.png"));
                await GameEval(step(35)); await Delay(300);
                L("passing through the wall: " + Short(await GameEval("window.__rr()")));
                Shot(Path.Combine(dir, "g05_gate_inside.png"));
                // two or three trains side by side
                await GameEval(setup + "window.__rrSpawn('train',0,0.9,0.9);window.__rrSpawn('train',1,0.5,0.9);window.__rrSpawn('train',2,0.7,0.6);window.__rrStep(0.001);");
                await Delay(300); Shot(Path.Combine(dir, "g06_trains_a.png"));
                await GameEval(step(30)); await Delay(300); Shot(Path.Combine(dir, "g07_trains_b.png"));
                // a ramp sliding past and out of the picture
                await GameEval(setup + "window.__rrSpawn('ramp',0,0.1,0.28);window.__rrSpawn('ramp',2,0.35,0.28);window.__rrStep(0.001);");
                await GameEval(step(25)); await Delay(300); Shot(Path.Combine(dir, "g08_ramp_a.png"));
                await GameEval(step(12)); await Delay(300); Shot(Path.Combine(dir, "g09_ramp_b.png"));
                await GameEval(step(12)); await Delay(300); Shot(Path.Combine(dir, "g10_ramp_c.png"));
                // on a roof with a bridge going over
                await GameEval(setup + "window.__rrSpawn('ramp',1,0.15,0.28);window.__rrSpawn('train',1,0.43,1.4);window.__rrStep(0.001);");
                await GameEval(step(100)); await GameEval("window.__rrScn('bridge',0.25);");
                await GameEval(step(35)); await Delay(300); Shot(Path.Combine(dir, "g11_roof_bridge_a.png"));
                await GameEval(step(22)); await Delay(300); Shot(Path.Combine(dir, "g12_roof_bridge_b.png"));
                L("on the roof under the bridge: " + Short(await GameEval("window.__rr()")));

                // sounds
                Sfx.Volume = 120; Sfx.Log.Length = 0; Sfx.StopAll();
                L("sounds (files are looked for in " + Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sounds") + "):");
                await GameEval(setup + "window.__rrSpawn('bar',1,0.2);" + step(70).Replace("return window.__rr();", "return 0;"));
                await Delay(400);
                L("  tripped (chaser appears) -> enrique playing: " + Sfx.Playing("enrique") + "   ambience playing: " + Sfx.Playing("ambience") + "   (expect true, true)");
                await GameEval("window.__rrSetGap(0.3);window.__rrStep(0.01);"); await Delay(400);
                L("  chaser right behind, still going -> ambience playing: " + Sfx.Playing("ambience") + "   (expect true)");
                await GameEval("window.__rrSetGap(0.97);window.__rrStep(0.01);"); await Delay(400);
                L("  chaser gone -> ambience playing: " + Sfx.Playing("ambience") + "  enrique playing: " + Sfx.Playing("enrique") + "   (expect false, false)");
                await GameEval(setup + "window.__rrSpawn('train',1,0.2);" + step(70).Replace("return window.__rr();", "return 0;"));
                await Delay(400);
                L("  caught         -> slowed playing: " + Sfx.Playing("slowed") + "   (expect true)");
                await GameEval("window.__rrStart();"); await Delay(400);
                L("  run again      -> slowed playing: " + Sfx.Playing("slowed") + "   (expect false)");
                L("  sound log: " + Sfx.Log.ToString().Replace("\r\n", " | "));
                Sfx.StopAll();

                // the chase pictures
                await GameEval(setup + "window.__rrSpawn('bar',1,0.2);window.__rrSpawn('bar',0,0.5);window.__rrSpawn('train',2,0.9,0.34);" + step(30).Replace("return window.__rr();", "return 0;"));
                await Delay(300);
                await GameEval(step(35)); await Delay(300);
                L("tripped, chaser closing in: " + Short(await GameEval("window.__rr()")));
                Shot(Path.Combine(dir, "g13_tripped_cop.png"));
                await GameEval(step(110)); await GameEval("window.__rrSpawn('bar',1,0.0);"); await GameEval(step(40));
                await GameEval("(function(){var n=0;while(n<400){window.__rrStep(0.01);n++;if(JSON.parse(window.__rr()).mode==='over')break;}return n;})()");
                await GameEval("window.__rrStep(0.15);"); await Delay(300);
                L("caught: " + Short(await GameEval("window.__rr()")));
                Shot(Path.Combine(dir, "g14_caught_a.png"));
                await GameEval("window.__rrStep(0.16);"); await Delay(300);
                Shot(Path.Combine(dir, "g15_caught_b.png"));
                Sfx.StopAll();

                // the real keyboard, only if this window really has it (never into another program)
                await GameEval("window.__rrStart();window.__rrFreeze(true);window.__rrClear();");
                Native.ForceForeground(webHost.Handle); await Delay(500);
                FocusGame(); await Delay(400);
                IntPtr fg = Native.GetForegroundWindow(); uint fpid; Native.GetWindowPid(fg, out fpid);
                L("foreground window belongs to this program: " + (fpid == myPid) + "   page.hasFocus=" + await GameEval("document.hasFocus()"));
                if (fpid == myPid)
                {
                    string real = "";
                    foreach (string k in new string[] { "{LEFT}", "{RIGHT}", "{RIGHT}", "a", "d", "w", "s", " " })
                    {
                        Native.GetWindowPid(Native.GetForegroundWindow(), out fpid);
                        if (fpid != myPid) { real += "[keyboard lost to another window, stopped]"; break; }
                        SendKeys.SendWait(k); await Delay(150);
                        real += k + "=" + await GameEval("window.__rr()") + "  ";
                    }
                    L("real keyboard: " + real);
                }
                else L("real-keyboard check skipped (this program was not in front)");
                await GameEval("window.__rrFreeze(false)");

                // the panels take turns
                ShowNotes(true); await Delay(500);
                L("notes opened -> game visible=" + gameVisible + " panel visible=" + game.Visible + "   notes visible=" + (notes != null && notes.Visible));
                ShowGame(true); await Delay(1000);
                L("game again -> notes flag=" + notesVisible + " tools flag=" + toolsVisible + " game=" + gameVisible + "   state=" + await GameEval("window.__rr()"));
                ShowTools(0); await Delay(400);
                L("tools opened -> game visible=" + gameVisible + " panel visible=" + game.Visible);

                // close from the button
                ShowGame(true); await Delay(1000);
                game.ClickClose();
                await Delay(700);
                L("close button -> game=" + gameVisible + " panel visible=" + game.Visible + " game view visible=" + gameCtl.IsVisible + " source=" + gameCtl.CoreWebView2.Source + "   page view back to width " + ctl.Bounds.Width + " (was " + mainW0 + ")");
                Shot(Path.Combine(dir, "g05_closed.png"));

                // the network is shut for the game view
                ShowGame(true); await Delay(1000);
                gameInternalNav = false;
                await GameEval("window.__net='pending';fetch('https://example.com/').then(function(){window.__net='REACHED';}).catch(function(){window.__net='refused';});");
                await Delay(1500);
                L("game page tries the internet -> " + await GameEval("window.__net") + "   (expect refused)");
                gameCtl.CoreWebView2.Navigate("https://example.com/"); await Delay(1500);
                L("navigation away -> source still " + gameCtl.CoreWebView2.Source.Substring(0, Math.Min(30, gameCtl.CoreWebView2.Source.Length)) + "   (expect the game page, not example.com)");

                // minimizing stops it; asking for it while minimized brings the tablet up
                CloseTablet(); await Delay(800);
                L("tablet minimized -> game=" + gameVisible + " panel visible=" + game.Visible + " source=" + gameCtl.CoreWebView2.Source);
                PressExtra("GAME"); await Delay(3000);
                L("pressed GAME while minimized -> open=" + open + " game=" + gameVisible + " panel=" + game.Bounds + " state=" + await GameEval("window.__rr()"));
                Shot(Path.Combine(dir, "g06_from_minimized.png"));
                ShowGame(false);

                // settings file: page 3 migration
                string tmpIni = Path.Combine(dir, "game.ini");
                File.WriteAllText(tmpIni, "Page3=MAP,WEB,NOTE,TIME,LIVE,PAGE,SET,UPD,CLRD,NONE,NONE,NONE,BACK,FWD,RFSH,ADS,LOCK,XPAR,PR1,PR2,PR3,UTC,SHOT,CPY\r\n");
                Config back = Config.Load(tmpIni);
                L("old saved page 3 -> slot 10 = " + back.Pages[2][9] + "   (expect GAME)");
                File.WriteAllText(tmpIni, "Page3=MAP,WEB,NOTE,TIME,LIVE,PAGE,SET,UPD,CLRD,SHOT,NONE,NONE,BACK,FWD,RFSH,ADS,LOCK,XPAR,PR1,PR2,PR3,UTC,SHOT,CPY\r\n");
                back = Config.Load(tmpIni);
                L("customised page 3 is left alone -> slot 10 = " + back.Pages[2][9] + "   (expect SHOT)");
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
            return "mode=" + f("mode") + " lane=" + f("lane") + " pos=" + f("pos") + " gap=" + f("gap") + " coins=" + f("coins") + " score=" + f("score") + " stumble=" + f("stumble") + " tunnel=" + f("tunnel");
        }
        static string Lane(string json) { System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(json, "lane\\\\?\":(\\d)"); return m.Success ? m.Groups[1].Value : "?"; }
        static string Mode(string json) { System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(json, "mode\\\\?\":\\\\?\"(\\w+)"); return m.Success ? (m.Groups[1].Value == "over" ? "CRASH" : "pass (" + m.Groups[1].Value + ")") : json; }

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
                    File.Copy(cfgPath, cfgPath + ".bak", true);
                    File.WriteAllText(cfgPath, text);
                    noSave = true;
                    RestartApp();
                    return true;
                }
                catch (Exception ex) { MessageBox.Show(parent, "Couldn't import: " + ex.Message, "Tac Map Overlay"); return false; }
            }
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
