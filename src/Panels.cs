// Soft keys (the pilot display's buttons), timers / clock / mission clock, the on-screen time panel, and the notes pad.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace TacOverlay
{
    // One physical button around the pilot display's screen. Positions are measured on the original photo (1560 x 1137 px).
    class SoftKey
    {
        public string Id; public char Side;       // Side: T(op) B(ottom) L(eft) R(ight)
        public float X, Y;                        // centre of the button, logical units
        public SoftKey(string id, char side, float srcX, float srcY) { Id = id; Side = side; X = srcX * 0.5f; Y = srcY * 569f / 1137f; }
    }

    class Hotspot
    {
        public string Id; public RectangleF R;    // logical units
        public Hotspot(string id, float x0, float y0, float x1, float y1)
        { Id = id; R = new RectangleF(x0 * 0.5f, y0 * 569f / 1137f, (x1 - x0) * 0.5f, (y1 - y0) * 569f / 1137f); }
    }

    // The notes pad that slides in beside the page: four pages of plain text, saved on this PC as you type.
    class NotesPanel : SidePanel
    {
        public event Action CloseClicked;
        public event Action EscPressed;
        readonly TextBox box = new TextBox();
        readonly Button[] pg = new Button[4];
        readonly Button copy = new Button(), clear = new Button(), close = new Button();
        readonly string[] txt = new string[4];
        int page;
        bool loading;
        System.Windows.Forms.Timer saveTimer;
        Font uiFont, boxFont;
        string clearArmed;

        static readonly Color Bg = Color.FromArgb(24, 27, 22), BtnBg = Color.FromArgb(40, 45, 36), Field = Color.FromArgb(14, 17, 12),
            Fg = Color.FromArgb(205, 215, 190), Green = Color.FromArgb(150, 205, 90);

        public static string Dir
        {
            get
            {
                string d = Environment.GetEnvironmentVariable("TACOVERLAY_DATA");
                if (string.IsNullOrEmpty(d)) d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TacOverlay");
                return Path.Combine(d, "notes");
            }
        }
        static string FileFor(int i) { return Path.Combine(Dir, "notes" + (i + 1) + ".txt"); }

        public NotesPanel()
        {
            BackColor = Bg;
            for (int i = 0; i < 4; i++)
            {
                try { txt[i] = File.Exists(FileFor(i)) ? File.ReadAllText(FileFor(i)) : ""; } catch { txt[i] = ""; }
                Button b = pg[i] = new Button(); b.Text = i == 3 ? "LOG" : (i + 1).ToString(); b.Tag = i;
                b.Click += delegate (object s, EventArgs e) { SwitchPage((int)((Button)s).Tag); };
                Controls.Add(b);
            }
            copy.Text = "COPY"; clear.Text = "CLEAR"; close.Text = "✕";
            copy.Click += delegate { try { if (box.Text.Length > 0) Clipboard.SetText(box.Text); } catch { } };
            clear.Click += delegate
            {
                if (clearArmed == null) { clearArmed = "x"; clear.Text = "SURE?"; clear.ForeColor = Color.FromArgb(255, 150, 40); return; }
                clearArmed = null; clear.Text = "CLEAR"; clear.ForeColor = Fg; box.Text = "";
            };
            close.Click += delegate { Save(); if (CloseClicked != null) CloseClicked(); };
            foreach (Button b in new Button[] { pg[0], pg[1], pg[2], pg[3], copy, clear, close })
            {
                b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.UseVisualStyleBackColor = false;
                b.BackColor = BtnBg; b.ForeColor = Fg; b.TabStop = false; b.Cursor = Cursors.Hand; b.Padding = Padding.Empty;
                if (b.Parent == null) Controls.Add(b);
            }
            box.Multiline = true; box.AcceptsReturn = true; box.WordWrap = true; box.ScrollBars = ScrollBars.Vertical;
            box.BorderStyle = BorderStyle.None; box.BackColor = Field; box.ForeColor = Fg;
            box.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.A) { box.SelectAll(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; if (EscPressed != null) EscPressed(); }
            };
            box.TextChanged += delegate
            {
                if (loading) return;
                txt[page] = box.Text;
                saveTimer.Stop(); saveTimer.Start();
            };
            box.LostFocus += delegate { clearArmed = null; clear.Text = "CLEAR"; clear.ForeColor = Fg; };
            Controls.Add(box);
            saveTimer = new System.Windows.Forms.Timer(); saveTimer.Interval = 800;
            saveTimer.Tick += delegate { saveTimer.Stop(); Save(); };
            ShowPage();
        }

        void ShowPage()
        {
            loading = true; box.Text = txt[page]; loading = false;
            for (int i = 0; i < 4; i++) { pg[i].ForeColor = i == page ? Green : Fg; pg[i].BackColor = i == page ? Color.FromArgb(52, 60, 42) : BtnBg; }
        }

        void SwitchPage(int i) { Save(); page = i; ShowPage(); box.Focus(); }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                for (int i = 0; i < 4; i++)
                    if (txt[i].Length > 0 || File.Exists(FileFor(i))) File.WriteAllText(FileFor(i), txt[i]);
            }
            catch { }
        }

        public void FocusBox() { box.Focus(); box.SelectionStart = box.TextLength; }

        // page 4 is the mission log: add a line (usually a time stamp) at the end
        public void AppendLog(string line)
        {
            string t = txt[3];
            if (t.Length > 0 && !t.EndsWith("\n")) t += "\r\n";
            txt[3] = t + line;
            if (page == 3) { loading = true; box.Text = txt[3]; loading = false; box.SelectionStart = box.TextLength; box.ScrollToCaret(); }
            saveTimer.Stop(); saveTimer.Start();
        }

        public void GotoPage(int i) { if (i != page) SwitchPage(i); }
        public string LogText { get { return txt[3]; } }

        public override void Relayout(int w, int h, double sc)
        {
            int row = (int)Math.Max(24, Math.Min(44, Math.Round(10.5 * sc))), p = Math.Max(3, row / 7);
            List<Control> uiCtl = new List<Control>();
            foreach (Control c in Controls) if (c is Button) uiCtl.Add(c);
            FontUtil.Apply(ref uiFont, "Consolas", Math.Max(10f, row * 0.46f), FontStyle.Bold, uiCtl);
            FontUtil.Apply(ref boxFont, "Consolas", (float)Math.Max(12.0, Math.Round(6.6 * sc)), FontStyle.Regular, new Control[] { box });

            int bs = row - 2 * p, x = p;
            for (int i = 0; i < 4; i++)
            {
                int pw0 = i == 3 ? TextRenderer.MeasureText("LOG", uiFont).Width + 14 : bs;
                pg[i].SetBounds(x, p, pw0, bs); x += pw0 + p;
            }
            close.SetBounds(w - p - bs, p, bs, bs);
            int cw = TextRenderer.MeasureText("CLEAR", uiFont).Width + 14, pw = TextRenderer.MeasureText("COPY", uiFont).Width + 14;
            clear.SetBounds(close.Left - p - cw, p, cw, bs);
            copy.SetBounds(clear.Left - p - pw, p, pw, bs);
            box.SetBounds(p, row + p, Math.Max(20, w - 2 * p), Math.Max(20, h - row - 2 * p));
        }
    }

    partial class OverlayForm
    {
        // ---------- the pilot display's buttons (positions on the original 1560 x 1137 photo) ----------
        static readonly SoftKey[] SoftKeys = new SoftKey[]
        {
            // slot ids: T1-T6 along the top (left to right), L1-L6 down the left, R1-R6 down the right, B1-B6 along the bottom.
            // What each slot DOES comes from cfg.Pages[page][slot index].
            new SoftKey("T1", 'T', 337, 50), new SoftKey("T2", 'T', 466, 50), new SoftKey("T3", 'T', 594, 50),
            new SoftKey("T4", 'T', 962, 50), new SoftKey("T5", 'T', 1093, 50), new SoftKey("T6", 'T', 1222, 50),
            new SoftKey("L1", 'L', 48, 377), new SoftKey("L2", 'L', 48, 476), new SoftKey("L3", 'L', 48, 574),
            new SoftKey("L4", 'L', 48, 672), new SoftKey("L5", 'L', 48, 770), new SoftKey("L6", 'L', 48, 870),
            new SoftKey("R1", 'R', 1506, 378), new SoftKey("R2", 'R', 1506, 477), new SoftKey("R3", 'R', 1506, 577),
            new SoftKey("R4", 'R', 1506, 675), new SoftKey("R5", 'R', 1506, 774), new SoftKey("R6", 'R', 1506, 872),
            new SoftKey("B1", 'B', 207, 1082), new SoftKey("B2", 'B', 347, 1082), new SoftKey("B3", 'B', 493, 1082),
            new SoftKey("B4", 'B', 1055, 1082), new SoftKey("B5", 'B', 1197, 1082), new SoftKey("B6", 'B', 1341, 1082)
        };

        // the rocker switches and the ON/OFF switch (areas on the photo)
        static readonly Hotspot[] Spots = new Hotspot[]
        {
            new Hotspot("VB_UP", 1478, 168, 1548, 215), new Hotspot("VB_DN", 1478, 215, 1548, 262),     // VID / BRT  : transparency
            new Hotspot("BK_UP", 8, 942, 82, 991), new Hotspot("BK_DN", 8, 991, 82, 1040),              // BK LT / BRT: size
            new Hotspot("VC_UP", 1474, 942, 1545, 991), new Hotspot("VC_DN", 1474, 991, 1545, 1040),    // VID / CON  : page zoom
            new Hotspot("OFF", 8, 262, 82, 304)                                                         // OFF        : minimize
        };

        // everything a pilot-display button can be set to (id, name shown in the assign window); the first one must be NONE
        public static readonly string[][] ActionList = new string[][]
        {
            new string[] { "NONE", "(nothing)" },
            new string[] { "MAP", "Map / home page" }, new string[] { "WEB", "Address bar" }, new string[] { "NOTE", "Notes pad" },
            new string[] { "TIME", "Clock and timer panel" }, new string[] { "LIVE", "Live corner view on / off" }, new string[] { "DRAG", "Move the live view" },
            new string[] { "PAGE", "Next button page" }, new string[] { "PG1", "Go to page 1" }, new string[] { "PG2", "Go to page 2" }, new string[] { "PG3", "Go to page 3" },
            new string[] { "TM1", "Timer 1" }, new string[] { "TM2", "Timer 2" }, new string[] { "TM3", "Timer 3" }, new string[] { "TM4", "Timer 4" },
            new string[] { "STW", "Stopwatch" }, new string[] { "CLR", "Clear timers" }, new string[] { "MSN", "Mission clock start / pause" }, new string[] { "MRST", "Mission clock reset" },
            new string[] { "UTC", "Local / Zulu clock" },
            new string[] { "BACK", "Back" }, new string[] { "FWD", "Forward" }, new string[] { "RFSH", "Refresh the page" },
            new string[] { "ADS", "Ad blocker on / off" }, new string[] { "XPAR", "Transparency" }, new string[] { "LOCK", "Mouse lock on / off" },
            new string[] { "CPY", "Copy the page address" }, new string[] { "SHOT", "Screenshot to clipboard" },
            new string[] { "ZRST", "Zoom reset" }, new string[] { "ZM+", "Zoom in" }, new string[] { "ZM-", "Zoom out" }, new string[] { "SZ+", "Tablet bigger" }, new string[] { "SZ-", "Tablet smaller" },
            new string[] { "BM1", "Bookmark 1" }, new string[] { "BM2", "Bookmark 2" }, new string[] { "BM3", "Bookmark 3" },
            new string[] { "BM4", "Bookmark 4" }, new string[] { "BM5", "Bookmark 5" }, new string[] { "BM6", "Bookmark 6" },
            new string[] { "LOG", "Add a time-stamped log line" }, new string[] { "LOGV", "Copy the mission log" },
            new string[] { "CALC", "Bearing / distance tool" }, new string[] { "N9L", "9-line template" }, new string[] { "CHK", "Checklist" },
            new string[] { "PR1", "Layout preset 1" }, new string[] { "PR2", "Layout preset 2" }, new string[] { "PR3", "Layout preset 3" },
            new string[] { "SET", "Settings" }, new string[] { "UPD", "Check for updates" }, new string[] { "CLRD", "Clear browsing data" }
        };

        // the buttons were reassigned / bookmarks edited: redraw the labels and keep the settings
        public void RefreshButtons() { SaveState(); lastSig = ""; RenderState(true); }

        // Settings > "Move it by hand": switch the live view on, minimize, and let the live view be dragged.
        bool pendingLiveMove;
        public void MoveLiveByHand()
        {
            if (!cfg.LiveMini) SetLiveMini(true);
            pendingLiveMove = true;
            if (open && !animating) CloseTablet();
            else if (!open && !animating && LiveShown) { pendingLiveMove = false; SetLiveDrag(true); }
        }

        bool IsPilot { get { return DeviceArt.Cur.Id == "pilot"; } }

        static RectangleF KeyButtonRect(SoftKey k) { return new RectangleF(k.X - 18f, k.Y - 15f, 36f, 30f); }

        RectangleF KeyLabelRect(SoftKey k)
        {
            Rectangle s = HoleRect;
            switch (k.Side)
            {
                case 'L': return new RectangleF(s.X, k.Y - 8f, 74f, 16f);
                case 'R': return new RectangleF(s.Right - 74f, k.Y - 8f, 74f, 16f);
                case 'T': return new RectangleF(k.X - 34f, s.Y, 68f, 17f);
                default: return new RectangleF(k.X - 34f, s.Bottom - 17f, 68f, 17f);
            }
        }

        // ---------- timers, stopwatch, mission clock ----------
        class Tmr { public int Preset; public bool Running, Done; public long EndMs, DoneMs; }
        readonly Stopwatch mono = Stopwatch.StartNew();
        readonly Tmr[] tmrs = new Tmr[4];
        bool stwRunning, stwUsed; long stwStartMs, stwBaseMs;
        bool msnRunning, msnUsed; long msnStartMs, msnBaseMs;
        NotesPanel notes;
        bool notesVisible;
        System.Windows.Forms.Timer infoTimer;
        string lastSig = "";
        string hoverKey;
        RectangleF infoRect = RectangleF.Empty;   // where the time panel was drawn (frame pixels), so clicks on it don't drag the tablet
        ContextMenuStrip timerMenu;
        bool menuWas;
        float infoMinTop, notesLeftInFrame = -1f;   // set when the address bar / notes pad are showing, so the time panel keeps clear

        [DllImport("kernel32.dll")] static extern bool Beep(uint freq, uint ms);

        void InitTimers() { for (int i = 0; i < 4; i++) { tmrs[i] = new Tmr(); tmrs[i].Preset = cfg.TimerSecs[i]; } }

        void InitPanels()
        {
            notes = new NotesPanel();
            notes.Visible = false;
            webHost.Controls.Add(notes);
            notes.CloseClicked += delegate { ShowNotes(false); };
            notes.EscPressed += delegate { FocusPageNow(); };
            InitTools();
            infoTimer = new System.Windows.Forms.Timer();
            infoTimer.Interval = 250;
            infoTimer.Tick += delegate { InfoTick(); };
            infoTimer.Start();
        }

        long Now { get { return mono.ElapsedMilliseconds; } }

        // developer helper: how long does one animation frame take at various widths?
        public string BenchFrames()
        {
            StringBuilder sb = new StringBuilder();
            IntPtr h0 = Handle;
            foreach (int w in new int[] { 500, 900, 1300, 1700, 2200 })
            {
                double vis = 1.0;
                RenderTablet(w, vis, false, false, false).Dispose();   // warm up
                Stopwatch s = Stopwatch.StartNew();
                int n = 8;
                for (int i = 0; i < n; i++) RenderTablet(w, vis, false, false, false).Dispose();
                double render = s.Elapsed.TotalMilliseconds / n;
                Bitmap b = RenderTablet(w, vis, false, false, false);
                s.Restart();
                for (int i = 0; i < n; i++) Push(b, new Point(100, 100), 100);
                double push = s.Elapsed.TotalMilliseconds / n;
                b.Dispose();
                int hh = HeightAt(w, 1.0);
                s.Restart();
                for (int i = 0; i < n; i++) DrawFromMips(w, hh).Dispose();
                double scaleOnly = s.Elapsed.TotalMilliseconds / n;
                sb.AppendLine(string.Format("w={0,5}  render {1,6:0.0} ms (of which scaling the art {4,5:0.0})   push {2,6:0.0} ms   => {3,5:0} fps max", w, render, push, 1000.0 / (render + push), scaleOnly));
            }
            return sb.ToString();
        }

        static string Fmt(long ms)
        {
            long s = Math.Max(0, ms) / 1000;
            return s >= 3600 ? string.Format("{0}:{1:00}:{2:00}", s / 3600, (s / 60) % 60, s % 60) : string.Format("{0:00}:{1:00}", s / 60, s % 60);
        }

        static string FmtPreset(int secs) { return secs >= 3600 ? string.Format("{0}:{1:00}:{2:00}", secs / 3600, (secs / 60) % 60, secs % 60) : string.Format("{0}:{1:00}", secs / 60, secs % 60); }

        // "5", "5:30" or "1:05:00"  ->  seconds (0 = not understood)
        public static int ParseDuration(string s)
        {
            if (s == null) return 0;
            string[] p = s.Trim().Split(':');
            int a, b, c;
            try
            {
                if (p.Length == 1 && int.TryParse(p[0], out a)) return Math.Max(0, Math.Min(86399, a * 60));
                if (p.Length == 2 && int.TryParse(p[0], out a) && int.TryParse(p[1], out b)) return Math.Max(0, Math.Min(86399, a * 60 + b));
                if (p.Length == 3 && int.TryParse(p[0], out a) && int.TryParse(p[1], out b) && int.TryParse(p[2], out c)) return Math.Max(0, Math.Min(86399, a * 3600 + b * 60 + c));
            }
            catch { }
            return 0;
        }

        public static string DurationText(int secs) { return FmtPreset(secs); }

        public void SetShowPanel(bool on) { cfg.ShowPanel = on; SaveState(); lastSig = ""; RenderState(true); }
        public void SetAlertBeep(bool on) { cfg.AlertBeep = on; SaveState(); }
        public void SetZulu(bool on) { cfg.Zulu = on; SaveState(); lastSig = ""; RenderState(true); }
        public void SetLabelInset(bool on) { cfg.LabelInset = on; SaveState(); if (webShown) PositionWeb(); lastSig = ""; RenderState(true); }
        public void SetShowLabels(bool on) { cfg.ShowLabels = on; SaveState(); if (webShown) PositionWeb(); lastSig = ""; RenderState(true); }

        public void SetTimerPreset(int i, int secs)
        {
            if (i < 0 || i > 3 || secs < 1) return;
            cfg.TimerSecs[i] = secs; tmrs[i].Preset = secs;
            SaveState(); RenderState(true);
        }

        bool FlashOn { get { return (Now / 500) % 2 == 0; } }
        bool AnyAlert { get { foreach (Tmr t in tmrs) if (t.Done) return true; return false; } }

        void PressTimer(int i)
        {
            Tmr t = tmrs[i];
            if (t.Done) t.Done = false;                        // acknowledge the alarm
            else if (t.Running) t.Running = false;             // cancel
            else { t.Running = true; t.EndMs = Now + t.Preset * 1000L; }
        }

        void PressStopwatch()
        {
            if (stwRunning) { stwRunning = false; stwBaseMs += Now - stwStartMs; }
            else if (stwUsed) { stwUsed = false; stwBaseMs = 0; }
            else { stwUsed = true; stwRunning = true; stwStartMs = Now; }
        }

        long StwMs { get { return stwBaseMs + (stwRunning ? Now - stwStartMs : 0); } }

        void PressMission()
        {
            if (msnRunning) { msnRunning = false; msnBaseMs += Now - msnStartMs; }
            else { msnUsed = true; msnRunning = true; msnStartMs = Now; }
        }

        void ResetMission() { msnRunning = false; msnUsed = false; msnBaseMs = 0; }
        long MsnMs { get { return msnBaseMs + (msnRunning ? Now - msnStartMs : 0); } }

        void ClearTimers()
        {
            foreach (Tmr t in tmrs) { t.Running = false; t.Done = false; }
            stwRunning = false; stwUsed = false; stwBaseMs = 0;
        }

        string ClockText(bool seconds)
        {
            DateTime d = cfg.Zulu ? DateTime.UtcNow : DateTime.Now;
            return (cfg.Zulu ? "ZULU " : "LCL ") + d.ToString(seconds ? "HH:mm:ss" : "HH:mm");
        }

        // Alarm, timer ends: beep, flash (the frame does that), and a tray note.
        void RaiseAlert(int i)
        {
            if (cfg.AlertBeep)
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { for (int n = 0; n < 3; n++) { Beep(1100, 180); Thread.Sleep(120); } } catch { }
                });
            try { tray.ShowBalloonTip(3000, "Tac Map Overlay", "TIMER " + (i + 1) + " (" + FmtPreset(tmrs[i].Preset) + ") is up", ToolTipIcon.Warning); } catch { }
        }

        // The line shown on the little minimized tablet (null = nothing to show)
        string DockInfo()
        {
            foreach (Tmr t in tmrs) if (t.Done) return FlashOn ? "TMR " + (Array.IndexOf(tmrs, t) + 1) + " DONE" : "";
            long best = long.MaxValue; int bi = -1;
            for (int i = 0; i < 4; i++) if (tmrs[i].Running && tmrs[i].EndMs - Now < best) { best = tmrs[i].EndMs - Now; bi = i; }
            if (bi >= 0) return "TMR " + (bi + 1) + " " + Fmt(best + 999);
            if (msnRunning) return "MSN " + Fmt(MsnMs);
            return null;
        }

        void InfoTick()
        {
            long now = Now;
            for (int i = 0; i < 4; i++)
            {
                Tmr t = tmrs[i];
                if (t.Running && now >= t.EndMs) { t.Running = false; t.Done = true; t.DoneMs = now; RaiseAlert(i); }
                else if (t.Done && now - t.DoneMs > 300000) t.Done = false;   // stops flashing after 5 minutes
            }
            if (!bezelShown || animating || inSizeMove || dockDown || inPush) return;
            // only redraw when something on screen would actually look different
            StringBuilder sb = new StringBuilder();
            sb.Append(open ? "o" : "d").Append(FlashOn && AnyAlert ? "F" : "f");
            if (open)
            {
                if (cfg.ShowPanel) foreach (KeyValuePair<string, int> l in PanelLines()) sb.Append(l.Key).Append('|');
                if (IsPilot && cfg.ShowLabels) foreach (SoftKey k in SoftKeys) { int st; sb.Append(KeyText(k, out st)).Append(st).Append('|'); }
            }
            else sb.Append(DockInfo()).Append(ClockText(false));
            string sig = sb.ToString();
            if (sig == lastSig) return;
            lastSig = sig;
            RenderState(true);
        }

        // ---------- what each soft key says ----------
        // state: 0 normal, 1 on (green), 2 off / unavailable (dim), 3 alarm (flashes)
        int softPage;   // which of the three button pages is showing

        // what a button does right now (page + slot)
        string ActionAt(SoftKey k)
        {
            int idx = Array.IndexOf(SoftKeys, k);
            string[] pg = cfg.Pages[softPage];
            return idx >= 0 && idx < pg.Length && pg[idx].Length > 0 ? pg[idx] : "NONE";
        }

        string KeyText(SoftKey k, out int state) { return KeyTextAct(ActionAt(k), out state); }

        string BookmarkName(int i)
        {
            string b = cfg.Bookmarks[i];
            if (b.Length == 0) return "";
            int bar = b.IndexOf('|');
            string n = bar > 0 ? b.Substring(0, bar).Trim() : b.Trim();
            return n.Length > 11 ? n.Substring(0, 11) : n;
        }

        string KeyTextAct(string id, out int state)
        {
            state = 0;
            CoreWebView2 wv = ctl != null ? ctl.CoreWebView2 : null;
            switch (id)
            {
                case "NONE": return "";
                case "MAP": return "MAP";
                case "PAGE": return "PAGE " + (softPage + 1) + "/3";
                case "PG1": case "PG2": case "PG3": state = (softPage == id[2] - '1') ? 1 : 0; return "PAGE " + id[2];
                case "BM1": case "BM2": case "BM3": case "BM4": case "BM5": case "BM6":
                    {
                        int b = id[2] - '1'; string nm = BookmarkName(b);
                        if (nm.Length == 0) { state = 2; return "BM" + (b + 1) + " -"; }
                        return nm;
                    }
                case "LOG": return "LOG +";
                case "LOGV": return "COPY LOG";
                case "CALC": state = (toolsVisible && toolsTab == 0) ? 1 : 0; return "BRG/DIST";
                case "N9L": state = (toolsVisible && toolsTab == 1) ? 1 : 0; return "9-LINE";
                case "CHK": state = (toolsVisible && toolsTab == 2) ? 1 : 0; return "CHECKLIST";
                case "PR1": case "PR2": case "PR3": state = cfg.Presets[id[2] - '1'].Length > 0 ? 0 : 2; return "PRESET " + id[2];
                case "SET": return "SETTINGS";
                case "UPD": return "UPDATE";
                case "CLRD": return "CLEAR DATA";
                case "ZM+": return "ZOOM +";
                case "ZM-": return "ZOOM -";
                case "SZ+": return "SIZE +";
                case "SZ-": return "SIZE -";
                case "DRAG": state = liveDrag ? 1 : 0; return "MOVE LIVE";
                case "WEB": state = barVisible ? 1 : 0; return "WEB";
                case "NOTE": state = notesVisible ? 1 : 0; return "NOTE";
                case "TIME": state = cfg.ShowPanel ? 1 : 0; return "TIME";
                case "LIVE": state = cfg.LiveMini ? 1 : 0; return cfg.LiveMini ? "LIVE ON" : "LIVE OFF";
                case "TM1": case "TM2": case "TM3": case "TM4":
                    {
                        int i = id[2] - '1';
                        Tmr t = tmrs[i];
                        if (t.Done) { state = 3; return "TMR" + (i + 1) + " DONE"; }
                        if (t.Running) { state = 1; return "TMR" + (i + 1) + " " + Fmt(t.EndMs - Now + 999); }
                        return "TMR" + (i + 1) + " " + FmtPreset(t.Preset);
                    }
                case "STW":
                    if (!stwUsed) return "STOPWATCH";
                    state = stwRunning ? 1 : 2; return "STW " + Fmt(StwMs);
                case "BACK": state = (wv != null && wv.CanGoBack) ? 0 : 2; return "BACK";
                case "FWD": state = (wv != null && wv.CanGoForward) ? 0 : 2; return "FWD";
                case "RFSH": return "REFRESH";
                case "ADS": state = cfg.AdBlock ? 1 : 2; return cfg.AdBlock ? "ADS ON" : "ADS OFF";
                case "XPAR": return "XPAR " + cfg.Opacity;
                case "LOCK": state = cfg.LockMouse ? 1 : 2; return cfg.LockMouse ? "LOCK" : "FREE";
                case "MSN":
                    if (!msnUsed) return "MSN CLK";
                    state = msnRunning ? 1 : 2; return "MSN " + Fmt(MsnMs);
                case "MRST": return "MSN RST";
                case "UTC": return cfg.Zulu ? "ZULU" : "LOCAL";
                case "CPY": return "COPY URL";
                case "SHOT": return "SCREENSHOT";
                case "ZRST": return "ZOOM " + (int)Math.Round(1050.0 * 100 / Math.Max(1, cfg.PageWidth));
                default: return id;
            }
        }

        string KeyTextFor(string id) { int st; string t = KeyTextAct(id, out st); return t + " [state " + st + "]"; }

        List<KeyValuePair<string, int>> PanelLines()
        {
            List<KeyValuePair<string, int>> l = new List<KeyValuePair<string, int>>();
            l.Add(new KeyValuePair<string, int>(ClockText(true), 0));
            if (msnUsed) l.Add(new KeyValuePair<string, int>("MSN " + Fmt(MsnMs), msnRunning ? 1 : 2));
            for (int i = 0; i < 4; i++)
            {
                Tmr t = tmrs[i];
                if (t.Done) l.Add(new KeyValuePair<string, int>("TMR" + (i + 1) + " DONE", 3));
                else if (t.Running) l.Add(new KeyValuePair<string, int>("TMR" + (i + 1) + " " + Fmt(t.EndMs - Now + 999), 1));
            }
            if (stwUsed) l.Add(new KeyValuePair<string, int>("STW " + Fmt(StwMs), stwRunning ? 1 : 2));
            return l;
        }

        Color StateColor(int st)
        {
            switch (st)
            {
                case 1: return Color.FromArgb(125, 235, 95);
                case 2: return Color.FromArgb(135, 140, 125);
                case 3: return FlashOn ? Color.FromArgb(255, 175, 35) : Color.FromArgb(255, 70, 50);
                default: return Color.FromArgb(232, 236, 224);
            }
        }

        // ---------- drawing on top of the page (labels, time panel) ----------
        // g draws on the frame bitmap, which sits over the page: pixels here are painted over the page's edge.
        void DrawScreenOverlays(Graphics g, int w, double sc)
        {
            infoRect = RectangleF.Empty;
            Rectangle s = HoleRect;
            float fpx = (float)Math.Max(9.0, 8.6 * sc);
            using (Font f = new Font("Consolas", fpx, FontStyle.Bold, GraphicsUnit.Pixel))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Near; sf.LineAlignment = StringAlignment.Center;
                if (IsPilot && cfg.ShowLabels)
                    foreach (SoftKey k in SoftKeys)
                    {
                        int st; string text = KeyText(k, out st);
                        if (text.Length == 0) continue;
                        SizeF sz = g.MeasureString(text, f);
                        float tw = Math.Max(4f, sz.Width - fpx * 0.4f), th = fpx * 1.25f, pad = (float)(2.5 * sc);
                        float cx, cy;
                        switch (k.Side)
                        {
                            case 'L': cx = (float)((s.X + 3) * sc) + tw / 2f + pad; cy = (float)(k.Y * sc); break;
                            case 'R': cx = (float)((s.Right - 3) * sc) - tw / 2f - pad; cy = (float)(k.Y * sc); break;
                            case 'T': cx = (float)(k.X * sc); cy = (float)((s.Y + 10) * sc); break;
                            default: cx = (float)(k.X * sc); cy = (float)((s.Bottom - 10) * sc); break;
                        }
                        RectangleF box = new RectangleF(cx - tw / 2f - pad, cy - th / 2f, tw + 2 * pad, th);
                        bool hot = hoverKey == "SK:" + k.Id;
                        using (GraphicsPath gp = RoundRectF(box, th * 0.28f))
                        {
                            using (Brush bb = new SolidBrush(Color.FromArgb(hot ? 215 : 165, 8, 10, 8))) g.FillPath(bb, gp);
                            using (Pen pn = new Pen(hot ? Color.FromArgb(150, 205, 90) : Color.FromArgb(60, 70, 55), 1f)) g.DrawPath(pn, gp);
                        }
                        using (Brush tb = new SolidBrush(StateColor(st))) g.DrawString(text, f, tb, box.X + pad, box.Y + (th - sz.Height) / 2f);
                    }

                if (cfg.ShowPanel)
                {
                    List<KeyValuePair<string, int>> lines = PanelLines();
                    float lh = fpx * 1.35f, wmax = 0f;
                    foreach (KeyValuePair<string, int> l in lines) wmax = Math.Max(wmax, g.MeasureString(l.Key, f).Width);
                    float pad = (float)(4 * sc);
                    float bw = wmax + 2 * pad, bh = lines.Count * lh + pad;
                    float right = (float)((s.Right - 6) * sc), top = (float)((s.Y + (IsPilot && cfg.ShowLabels ? 24 : 6)) * sc);
                    if (notesLeftInFrame > 0f) right = Math.Min(right, notesLeftInFrame - (float)(6 * sc));   // sit left of the notes pad
                    if (infoMinTop > 0f) top = Math.Max(top, infoMinTop);                                       // and below the address bar
                    RectangleF box = new RectangleF(right - bw, top, bw, bh);
                    infoRect = box;
                    using (GraphicsPath gp = RoundRectF(box, (float)(4 * sc)))
                    {
                        using (Brush bb = new SolidBrush(Color.FromArgb(150, 8, 10, 8))) g.FillPath(bb, gp);
                        using (Pen pn = new Pen(Color.FromArgb(70, 80, 62), 1f)) g.DrawPath(pn, gp);
                    }
                    for (int i = 0; i < lines.Count; i++)
                        using (Brush tb = new SolidBrush(StateColor(lines[i].Value)))
                            g.DrawString(lines[i].Key, f, tb, box.X + pad, box.Y + pad / 2f + i * lh);
                }
            }
        }

        // ---------- ATAK: two extra round buttons beside the globe (timers, notes) ----------
        PointF LiveBtnCenter(int w) { double sc = w / (double)ImgW; return new PointF((float)((DeviceArt.Cur.Gear.X - 62) * sc), (float)(DeviceArt.Cur.Gear.Y * sc)); }
        PointF TimerBtnCenter(int w) { double sc = w / (double)ImgW; return new PointF((float)((DeviceArt.Cur.Gear.X - 93) * sc), (float)(DeviceArt.Cur.Gear.Y * sc)); }
        PointF NotesBtnCenter(int w) { double sc = w / (double)ImgW; return new PointF((float)((DeviceArt.Cur.Gear.X - 124) * sc), (float)(DeviceArt.Cur.Gear.Y * sc)); }

        void DrawAtakButtons(Graphics g, int w, float gr)
        {
            if (IsPilot) return;
            PointF tc = TimerBtnCenter(w), nc = NotesBtnCenter(w), lc = LiveBtnCenter(w);
            bool tHot = hoverKey == "TIMERMENU", nHot = hoverKey == "NOTES" || notesVisible, lHot = hoverKey == "LIVE" || cfg.LiveMini;
            bool anyRun = AnyAlert; foreach (Tmr t in tmrs) if (t.Running) anyRun = true; if (stwRunning || msnRunning) anyRun = true;
            foreach (int which in new int[] { 0, 1, 2 })
            {
                PointF c = which == 0 ? tc : (which == 1 ? nc : lc); bool lit = which == 0 ? (tHot || anyRun) : (which == 1 ? nHot : lHot);
                Color col = lit ? Color.FromArgb(150, 205, 90) : Color.FromArgb(205, 212, 192);
                if (which == 0 && AnyAlert) col = StateColor(3);
                using (Brush back = new SolidBrush(Color.FromArgb(30, 32, 26)))
                using (Pen ring = new Pen(lit ? col : Color.FromArgb(110, 120, 95), Math.Max(1f, gr / 10f)))
                {
                    g.FillEllipse(back, c.X - gr, c.Y - gr, gr * 2, gr * 2);
                    g.DrawEllipse(ring, c.X - gr, c.Y - gr, gr * 2, gr * 2);
                }
                using (Pen p = new Pen(col, Math.Max(1f, gr / 9f)))
                {
                    float r = gr * 0.55f;
                    if (which == 0)
                    {   // stopwatch: ring, hand, little crown
                        g.DrawEllipse(p, c.X - r, c.Y - r + gr * 0.08f, r * 2, r * 2);
                        g.DrawLine(p, c.X, c.Y + gr * 0.08f, c.X + r * 0.55f, c.Y - r * 0.35f);
                        g.DrawLine(p, c.X - r * 0.28f, c.Y - r - gr * 0.12f, c.X + r * 0.28f, c.Y - r - gr * 0.12f);
                    }
                    else if (which == 1)
                    {   // notes: a page with lines
                        g.DrawRectangle(p, c.X - r * 0.75f, c.Y - r, r * 1.5f, r * 2f);
                        for (int i = 0; i < 3; i++) g.DrawLine(p, c.X - r * 0.45f, c.Y - r * 0.45f + i * r * 0.5f, c.X + r * 0.45f, c.Y - r * 0.45f + i * r * 0.5f);
                    }
                    else
                    {   // live corner view: a screen with a small picture-in-picture window in its corner
                        g.DrawRectangle(p, c.X - r * 1.0f, c.Y - r * 0.72f, r * 2f, r * 1.44f);
                        using (Brush fb = new SolidBrush(col)) g.FillRectangle(fb, c.X + r * 0.1f, c.Y + r * 0.02f, r * 0.7f, r * 0.5f);
                    }
                }
            }
        }

        // ---------- clicks ----------
        // Which button / label / switch is under this point of the frame window? (null = none)
        string HitExtra(Point p)
        {
            if (!open || animating) return null;
            double sc = frame.Width / (double)ImgW;
            float lx = (float)(p.X / sc), ly = (float)(p.Y / sc);
            if (IsPilot)
            {
                foreach (SoftKey k in SoftKeys)
                    if (KeyButtonRect(k).Contains(lx, ly) || (cfg.ShowLabels && ActionAt(k) != "NONE" && KeyLabelRect(k).Contains(lx, ly))) return "SK:" + k.Id;
                foreach (Hotspot h in Spots) if (h.R.Contains(lx, ly)) return h.Id;
            }
            else
            {
                float gr = GearRadius(frame.Width); float r = Math.Max(13f, gr);
                PointF lv = LiveBtnCenter(frame.Width);
                if ((p.X - lv.X) * (p.X - lv.X) + (p.Y - lv.Y) * (p.Y - lv.Y) <= r * r) return "LIVE";
                PointF t = TimerBtnCenter(frame.Width), n = NotesBtnCenter(frame.Width);
                if ((p.X - t.X) * (p.X - t.X) + (p.Y - t.Y) * (p.Y - t.Y) <= r * r) return "TIMERMENU";
                if ((p.X - n.X) * (p.X - n.X) + (p.Y - n.Y) * (p.Y - n.Y) <= r * r) return "NOTES";
            }
            return null;
        }

        bool OverInfoPanel(Point p) { return open && !infoRect.IsEmpty && infoRect.Contains(p); }

        void PressExtra(string id)
        {
            CoreWebView2 wv = ctl != null ? ctl.CoreWebView2 : null;
            bool redraw = true;
            if (id.StartsWith("SK:"))
            {   // a pilot-display button: look up what it does on the page that is showing
                string slot = id.Substring(3); id = "NONE";
                foreach (SoftKey sk in SoftKeys) if (sk.Id == slot) { id = ActionAt(sk); break; }
            }
            switch (id)
            {
                case "NONE": redraw = false; break;
                case "PAGE": softPage = (softPage + 1) % 3; break;
                case "PG1": softPage = 0; break;
                case "PG2": softPage = 1; break;
                case "PG3": softPage = 2; break;
                case "BM1": case "BM2": case "BM3": case "BM4": case "BM5": case "BM6": OpenBookmark(id[2] - '1'); break;
                case "ZM+": SetPageWidth(cfg.PageWidth - 100); break;
                case "ZM-": SetPageWidth(cfg.PageWidth + 100); break;
                case "SZ+": SetOpenSize(cfg.OpenSize + 5); break;
                case "SZ-": SetOpenSize(cfg.OpenSize - 5); break;
                case "DRAG": redraw = false; ToggleLiveDrag(); break;
                case "LOG": AddLogLine(); break;
                case "LOGV": CopyLog(); break;
                case "CALC": ShowTools(0); break;
                case "N9L": ShowTools(1); break;
                case "CHK": ShowTools(2); break;
                case "PR1": ApplyPreset(0); break;
                case "PR2": ApplyPreset(1); break;
                case "PR3": ApplyPreset(2); break;
                case "SET": redraw = false; BeginInvoke((MethodInvoker)delegate { OpenSettings(); }); break;
                case "UPD": redraw = false; BeginInvoke((MethodInvoker)delegate { bool w0; FreeMouseBegin(out w0); try { CheckForUpdatesNow(); } finally { FreeMouseEnd(w0); } }); break;
                case "CLRD":
                    redraw = false;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        bool w1; FreeMouseBegin(out w1);
                        try
                        {
                            if (MessageBox.Show("Clear history, cookies, cache and saved site logins for the tablet's browser?", "Tac Map Overlay",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes) ClearBrowsingData();
                        }
                        finally { FreeMouseEnd(w1); }
                    });
                    break;
                case "MAP": if (ctl != null) { NavigateConfigured(); FocusPageNow(); } break;
                case "WEB": ToggleBar(); break;
                case "NOTE": case "NOTES": ToggleNotes(); break;
                case "TIME": case "TMR": cfg.ShowPanel = !cfg.ShowPanel; SaveState(); break;
                case "LIVE": SetLiveMini(!cfg.LiveMini); break;
                case "MENU": redraw = false; OpenSettings(); break;
                case "TM1": PressTimer(0); break;
                case "TM2": PressTimer(1); break;
                case "TM3": PressTimer(2); break;
                case "TM4": PressTimer(3); break;
                case "STW": PressStopwatch(); break;
                case "CLR": ClearTimers(); break;
                case "BACK": if (wv != null && wv.CanGoBack) wv.GoBack(); FocusPageNow(); break;
                case "FWD": if (wv != null && wv.CanGoForward) wv.GoForward(); FocusPageNow(); break;
                case "RFSH": if (wv != null) wv.Reload(); FocusPageNow(); break;
                case "ADS": SetAdBlock(!cfg.AdBlock); break;
                case "XPAR": CycleOpacity(); break;
                case "LOCK": SetLockMouse(!cfg.LockMouse); break;
                case "MSN": PressMission(); break;
                case "MRST": ResetMission(); break;
                case "UTC": cfg.Zulu = !cfg.Zulu; SaveState(); break;
                case "CPY":
                    try { string u = wv != null ? wv.Source : ""; if (u.StartsWith("http")) { Clipboard.SetText(u); tray.ShowBalloonTip(1500, "Tac Map Overlay", "Address copied", ToolTipIcon.Info); } } catch { }
                    break;
                case "SHOT": TakeScreenshot(); break;
                case "ZRST": SetPageWidth(1050); break;
                case "VB_UP": SetOpacity(Math.Min(100, cfg.Opacity + 10)); break;
                case "VB_DN": SetOpacity(Math.Max(25, cfg.Opacity - 10)); break;
                case "BK_UP": SetOpenSize(cfg.OpenSize + 5); break;
                case "BK_DN": SetOpenSize(cfg.OpenSize - 5); break;
                case "VC_UP": SetPageWidth(cfg.PageWidth - 100); break;     // zoom in = a narrower page
                case "VC_DN": SetPageWidth(cfg.PageWidth + 100); break;
                case "OFF": redraw = false; CloseTablet(); break;
                case "TIMERMENU": redraw = false; ShowTimerMenu(); break;
            }
            if (redraw) { lastSig = ""; RenderState(true); }
        }

        public void SetPageWidth(int pw)
        {
            cfg.PageWidth = Math.Max(500, Math.Min(2400, pw));
            if (webShown) PositionWeb();
            SaveState();
        }

        async void TakeScreenshot()
        {
            if (ctl == null) return;
            try
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    await ctl.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
                    ms.Position = 0;
                    using (Image img = Image.FromStream(ms)) Clipboard.SetImage(new Bitmap(img));
                }
                tray.ShowBalloonTip(1500, "Tac Map Overlay", "Screenshot copied - paste it anywhere", ToolTipIcon.Info);
            }
            catch { }
        }

        // ATAK's timer button: a small menu (the pilot display has real buttons for this)
        void ShowTimerMenu()
        {
            if (timerMenu != null) timerMenu.Dispose();
            timerMenu = new ContextMenuStrip();
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                Tmr t = tmrs[i];
                string label = t.Done ? "Timer " + (i + 1) + " is up - dismiss" : t.Running ? "Timer " + (i + 1) + "  " + Fmt(t.EndMs - Now + 999) + "  -  cancel" : "Start timer " + (i + 1) + "  (" + FmtPreset(t.Preset) + ")";
                timerMenu.Items.Add(label, null, delegate { PressTimer(idx); lastSig = ""; RenderState(true); });
            }
            timerMenu.Items.Add(new ToolStripSeparator());
            timerMenu.Items.Add(stwUsed ? (stwRunning ? "Stopwatch  " + Fmt(StwMs) + "  -  stop" : "Stopwatch  " + Fmt(StwMs) + "  -  reset") : "Start stopwatch", null, delegate { PressStopwatch(); lastSig = ""; RenderState(true); });
            timerMenu.Items.Add(msnRunning ? "Mission clock  " + Fmt(MsnMs) + "  -  pause" : (msnUsed ? "Mission clock  " + Fmt(MsnMs) + "  -  resume" : "Start mission clock"), null, delegate { PressMission(); lastSig = ""; RenderState(true); });
            if (msnUsed) timerMenu.Items.Add("Reset mission clock", null, delegate { ResetMission(); lastSig = ""; RenderState(true); });
            timerMenu.Items.Add(new ToolStripSeparator());
            timerMenu.Items.Add("Clear all timers", null, delegate { ClearTimers(); lastSig = ""; RenderState(true); });
            timerMenu.Items.Add(cfg.ShowPanel ? "Hide the clock panel" : "Show the clock panel", null, delegate { cfg.ShowPanel = !cfg.ShowPanel; SaveState(); lastSig = ""; RenderState(true); });
            timerMenu.Items.Add(cfg.Zulu ? "Show local time" : "Show Zulu (UTC) time", null, delegate { cfg.Zulu = !cfg.Zulu; SaveState(); lastSig = ""; RenderState(true); });
            FreeMouseBegin(out menuWas);
            timerMenu.Closed += delegate { FreeMouseEnd(menuWas); };
            timerMenu.Show(Cursor.Position);
        }

        // ---------- live corner view (minimized, no frame) ----------
        // The page window is shown on its own where the little tablet would sit, as big as the whole tablet was, with no frame
        // around it: the screen takes over the space the frame used. It is click-through, so the game keeps the mouse.
        void ShowLive()
        {
            if (webHost == null || ctl == null || open || animating) return;
            dockRect = ComputeDockRect();
            LiveShown = true;
            ApplyMode();                       // click-through + the minimized transparency
            PositionLive(false);
            Native.ShowWindow(webHost.Handle, Native.SW_SHOWNOACTIVATE);
            ctl.IsVisible = true;
            // the frame window stays, but only as a transparent, click-through layer for the alarm banner / move outline
            if (!bezelShown) ShowBezel();
            frame = dockRect;
            RenderLiveOverlay(dockRect);
            RaiseLive();
            NotifyPageMode();
            if (pendingLiveMove) { pendingLiveMove = false; SetLiveDrag(true); }
        }

        // Frame window while the live view is on screen: transparent, with an alarm banner and (in move mode) an outline.
        void RenderLiveOverlay(Rectangle r)
        {
            using (Bitmap b = new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height), PixelFormat.Format32bppPArgb))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    float fs = Math.Max(11f, r.Width / 34f);
                    if (liveDrag)
                    {
                        // nearly invisible fill so this layer can be grabbed, a clear outline and a hint
                        using (Brush fill = new SolidBrush(Color.FromArgb(10, 20, 20, 20))) g.FillRectangle(fill, 0, 0, r.Width, r.Height);
                        using (Pen pn = new Pen(Color.FromArgb(235, 150, 205, 90), 4f)) g.DrawRectangle(pn, 2, 2, r.Width - 5, r.Height - 5);
                        using (Font f = new Font("Consolas", fs, FontStyle.Bold, GraphicsUnit.Pixel))
                        {
                            string hint = "DRAG TO MOVE  -  click to finish";
                            SizeF sz = g.MeasureString(hint, f);
                            RectangleF pill = new RectangleF((r.Width - sz.Width) / 2f - 10, r.Height / 2f - sz.Height / 2f - 6, sz.Width + 20, sz.Height + 12);
                            using (GraphicsPath gp = RoundRectF(pill, 8f)) using (Brush bb = new SolidBrush(Color.FromArgb(200, 10, 12, 8))) g.FillPath(bb, gp);
                            using (Brush tb = new SolidBrush(Color.FromArgb(150, 205, 90))) g.DrawString(hint, f, tb, pill.X + 10, pill.Y + 6);
                        }
                    }
                    int doneIdx = -1;
                    for (int i = 0; i < 4; i++) if (tmrs[i].Done) { doneIdx = i; break; }
                    if (doneIdx >= 0)
                    {
                        string txt = "TIMER " + (doneIdx + 1) + " DONE  " + FmtPreset(tmrs[doneIdx].Preset);
                        using (Font f = new Font("Consolas", fs * 1.25f, FontStyle.Bold, GraphicsUnit.Pixel))
                        {
                            SizeF sz = g.MeasureString(txt, f);
                            RectangleF pill = new RectangleF((r.Width - sz.Width) / 2f - 12, 10, sz.Width + 24, sz.Height + 10);
                            using (GraphicsPath gp = RoundRectF(pill, 8f))
                            {
                                using (Brush bb = new SolidBrush(Color.FromArgb(215, FlashOn ? 120 : 60, FlashOn ? 70 : 8, 6))) g.FillPath(bb, gp);
                                using (Pen pn = new Pen(StateColor(3), 2f)) g.DrawPath(pn, gp);
                            }
                            using (Brush tb = new SolidBrush(Color.FromArgb(255, 245, 225))) g.DrawString(txt, f, tb, pill.X + 12, pill.Y + 5);
                        }
                    }
                }
                frame = r;
                Push(b, r.Location, 100);
            }
        }

        bool liveDrag;
        System.Windows.Forms.Timer liveDragTimer;

        // "Move the live view": it can be grabbed and dragged for a few seconds, then goes click-through again.
        public void SetLiveDrag(bool on)
        {
            if (on && !(LiveShown && !open && !animating)) return;
            liveDrag = on;
            if (liveDragTimer == null)
            {
                liveDragTimer = new System.Windows.Forms.Timer(); liveDragTimer.Interval = 12000;
                liveDragTimer.Tick += delegate { liveDragTimer.Stop(); SetLiveDrag(false); };
            }
            liveDragTimer.Stop(); if (on) liveDragTimer.Start();
            ApplyMode();
            if (LiveShown) RenderLiveOverlay(dockRect);
        }

        public void ToggleLiveDrag()
        {
            if (!cfg.LiveMini) { try { tray.ShowBalloonTip(2500, "Tac Map Overlay", "Switch the LIVE view on first.", ToolTipIcon.Info); } catch { } return; }
            if (open) { try { tray.ShowBalloonTip(2500, "Tac Map Overlay", "Minimize the tablet first, then move the live view.", ToolTipIcon.Info); } catch { } return; }
            SetLiveDrag(!liveDrag);
        }

        void EndLiveDrag()
        {
            Screen sc = Screen.FromRectangle(frame);
            Rectangle b = sc.Bounds;
            if (sc.DeviceName != cfg.Monitor && !(string.IsNullOrEmpty(cfg.Monitor) && sc.Primary)) cfg.Monitor = sc.DeviceName;
            cfg.LiveX = frame.Right >= b.Right - 30 ? int.MinValue : frame.X - b.Left;               // snaps to the corner / bottom when dropped near it
            cfg.LiveY = frame.Bottom >= FloorBottom() - 30 ? int.MinValue : frame.Y - b.Top;
            dockRect = ComputeDockRect();
            SaveState();
            SetLiveDrag(false);
            PositionLive();
        }

        public void SetLivePct(int pct)
        {
            cfg.LivePct = Math.Max(15, Math.Min(60, pct));
            if (!open && !animating && cfg.LiveMini) { dockRect = ComputeDockRect(); if (LiveShown) PositionLive(); }
            SaveState();
        }

        // "br" bottom-right, "bl" bottom-left, "tr" top-right, "tl" top-left
        public void SetLiveCorner(string c)
        {
            cfg.LiveX = (c == "bl" || c == "tl") ? 14 : int.MinValue;
            cfg.LiveY = (c == "tr" || c == "tl") ? 14 : int.MinValue;
            if (!open && !animating && cfg.LiveMini) { dockRect = ComputeDockRect(); if (LiveShown) PositionLive(); }
            SaveState();
        }

        void PositionLive() { PositionLive(true); }

        void PositionLive(bool overlay)
        {
            if (webHost == null || ctl == null) return;
            Rectangle r = dockRect;
            int w = r.Width, h = r.Height;
            Native.SetWindowPos(webHost.Handle, IntPtr.Zero, r.X, r.Y, w, h, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            Native.SetWindowRgn(webHost.Handle, Native.CreateRoundRectRgn(0, 0, w + 1, h + 1, 14, 14), true);
            if (bar != null) bar.Visible = false;
            if (notes != null) notes.Visible = false;
            ctl.Bounds = new Rectangle(0, 0, w, h);
            // same page layout as on the big screen, just smaller
            ctl.ZoomFactor = Math.Max(0.15, Math.Min(3.0, w / (double)Math.Max(300, cfg.PageWidth)));
            if (overlay && bezelShown) { frame = r; RenderLiveOverlay(r); }
        }

        void RaiseLive()
        {
            if (webHost != null && LiveShown)
            {
                Native.SetWindowPos(webHost.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                RaiseBezel();   // the transparent alarm layer goes above the page
            }
        }

        // The top button / LIVE key / tray item / settings tick. Takes effect when the tablet is minimized.
        public void SetLiveMini(bool on)
        {
            if (cfg.LiveMini == on) return;
            cfg.LiveMini = on;
            SaveState();
            BuildMenu();
            if (!open && !animating)
            {
                // minimized right now: switch what is showing in the corner
                dockRect = ComputeDockRect();
                if (on) { if (ctl != null) ShowLive(); }
                else
                {
                    liveDrag = false;
                    HideWeb();
                    ApplyMode();
                    if (cfg.ShowDock) { ShowBezel(); frame = dockRect; SetFrame(dockRect, PeekFrac, false, true); }
                    else { Native.ShowWindow(Handle, Native.SW_HIDE); bezelShown = false; }
                    NotifyPageMode();
                }
            }
            lastSig = "";
            RenderState(true);
            if (on && open) { try { tray.ShowBalloonTip(2500, "Tac Map Overlay", "Live screen is ON: minimize the tablet and the screen stays in the corner, without the frame.", ToolTipIcon.Info); } catch { } }
        }

        public bool LiveMiniOn { get { return cfg.LiveMini; } }

        // ---------- extra hotkeys ----------
        bool peekActive, peekSawRepeat, focusNotesOnOpen;
        long peekLastMs;
        System.Windows.Forms.Timer peekTimer;

        void HotkeyNote(string text)
        {
            try { tray.ShowBalloonTip(1500, "Tac Map Overlay", text, ToolTipIcon.Info); } catch { }
        }

        // HOLD-to-peek: the key opens the tablet; letting go of it minimizes again.
        void PeekPressed()
        {
            // No key-state reading at all: Windows repeats the hotkey message while the key is held (the typematic repeat), so
            // "messages are still arriving" = still held. When they stop for longer than the repeat gap, it was let go.
            if (Program.TestMode) AppLog.Write("peek hotkey message: open=" + open + " animating=" + animating + " active=" + peekActive);
            if (peekActive) { peekLastMs = Now; peekSawRepeat = true; return; }   // the held key repeating
            if (open || animating) return;           // already out (you pressed the normal key): leave it alone
            peekActive = true; peekSawRepeat = false; peekLastMs = Now;
            OpenTablet();
            if (peekTimer == null)
            {
                peekTimer = new System.Windows.Forms.Timer(); peekTimer.Interval = 40;
                peekTimer.Tick += delegate
                {
                    if (!peekActive) { peekTimer.Stop(); return; }
                    // time Windows waits before the first repeat (your keyboard setting: 250-1000 ms), then the gap between repeats
                    int firstWait = (SystemInformation.KeyboardDelay + 1) * 250;
                    double perSec = 2.5 + SystemInformation.KeyboardSpeed * (27.5 / 31.0);
                    int gap = (int)(1000.0 / perSec);
                    long limit = (peekSawRepeat ? gap : firstWait) + 160;
                    if (Now - peekLastMs > limit)
                    {
                        peekActive = false; peekTimer.Stop();
                        if (open || animating) CloseTablet();
                    }
                };
            }
            peekTimer.Start();
        }

        void NotesHotkey()
        {
            if (!open && !animating) { notesVisible = true; focusNotesOnOpen = true; OpenTablet(); }
            else ToggleNotes();
        }

        void TimerHotkey(int i)
        {
            PressTimer(i);
            lastSig = ""; RenderState(true);
            Tmr t = tmrs[i];
            if (!bezelShown) HotkeyNote("Timer " + (i + 1) + (t.Running ? " started  " + FmtPreset(t.Preset) : " stopped"));
        }

        // ---------- notes ----------
        void ToggleNotes() { ShowNotes(!notesVisible); }

        void ShowNotes(bool show)
        {
            if (notes == null) return;
            if (show && toolsVisible) { toolsVisible = false; if (tools != null) tools.SaveAll(); }
            notesVisible = show;
            if (webShown) PositionWeb();
            RenderState(true);
            if (show)
            {
                if (!Program.TestMode) Native.ForceForeground(webHost.Handle);
                notes.FocusBox();
            }
            else { notes.Save(); FocusPageNow(); }
        }
    }
}
