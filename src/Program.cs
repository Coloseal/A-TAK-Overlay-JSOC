// Tac Map Overlay - a rugged ATAK-style tablet that pops open over your game and shows a web page.
// Written in C# 5 so it compiles with the csc.exe that ships with Windows (no SDK needed).
//
// How it is built: the tablet frame is a per-pixel-alpha layered window (smooth edges, atomic
// move/resize = no rubber-banding). The web page lives in a second window that sits behind a
// transparent "hole" cut into the frame's screen.
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
    static class Native
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x20;
        public const int WS_EX_TOOLWINDOW = 0x80;
        public const int WS_EX_LAYERED = 0x80000;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const uint LWA_ALPHA = 2;
        public const int ULW_ALPHA = 2;
        public const int SW_HIDE = 0;
        public const int SW_SHOWNOACTIVATE = 4;
        public const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; public SIZE(int x, int y) { cx = x; cy = y; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int idx, int val);
        [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr h, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION blend, int flags);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
        [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")] public static extern uint GetWindowPid(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("user32.dll")] public static extern bool ClipCursor(IntPtr rect);
        [DllImport("user32.dll", EntryPoint = "ClipCursor")] public static extern bool ClipCursorRect(ref RECT rect);
        [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")] public static extern void CopyMemory(IntPtr dst, IntPtr src, UIntPtr len);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint ms);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        public const uint GW_HWNDPREV = 3;
        [DllImport("psapi.dll")] public static extern bool EmptyWorkingSet(IntPtr process);

        public static void ForceForeground(IntPtr h)
        {
            if (h == IntPtr.Zero || !IsWindow(h)) return;
            if (GetForegroundWindow() == h) return;
            // Normal case (we just got a hotkey / click): Windows lets us take the foreground directly.
            SetForegroundWindow(h);
            if (GetForegroundWindow() == h) return;
            // A game is holding the foreground: briefly share its input queue, as launchers and tools commonly do.
            uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
            uint me = GetCurrentThreadId();
            bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
            SetForegroundWindow(h);
            if (attached) AttachThreadInput(me, fgThread, false);
        }
    }

    class Config
    {
        public string Url = "https://jsoctacmap-544084018891.us-east1.run.app/";
        public string ToggleKey = "Ctrl+F1";  // open / close the tablet
        public string SizeKey = "Ctrl+F8";    // cycle tablet size
        public string OpacityKey = "Ctrl+F9"; // cycle transparency
        public string PeekKey = "Ctrl+Alt+P";      // HOLD: the tablet is expanded only while you hold the key
        public string NotesKey = "Ctrl+Alt+N";     // open / close the notes pad
        public string LiveKey = "Ctrl+Alt+L";      // live corner view on / off
        public string DragKey = "Ctrl+Alt+M";      // move the live view
        public string Timer1Key = "Ctrl+Alt+1";    // start / cancel timer 1
        public string Timer2Key = "Ctrl+Alt+2";    // start / cancel timer 2
        public string StopwatchKey = "Ctrl+Alt+S"; // stopwatch start / stop / reset
        public string Theme = Themes.Default;
        public string Monitor = "";           // Screen.DeviceName, empty = primary
        public int OpenSize = 78;             // tablet width as % of the monitor's width
        public string Anchor = "center";      // center | bottomright | custom
        public int CX = int.MinValue, CY = int.MinValue; // centre of the tablet when Anchor=custom
        public int Opacity = 100;             // percent
        public int PageWidth = 1050;          // page is laid out this many CSS px wide, then scaled to the screen (0 = off)
        public bool Animate = true;
        public int AnimMs = 300;
        public bool ShowDock = true;          // keep the top of the tablet peeking at the bottom-right when closed
        public int DockWidth = 340;           // width of that peeking tablet in pixels
        public int DockPeek = 30;             // how much of the tablet shows when minimized (% of its height)
        public int DockOpacity = 100;         // transparency of the minimized tablet (%)
        public int DockX = int.MinValue;      // left edge of the minimized tablet, from the monitor's left (MinValue = bottom-right)
        public int DockY = int.MinValue;      // top edge from the monitor's top (MinValue = flush with the bottom edge)
        public string UpdateUrl = "";         // address of the update info file (blank = the one built into the exe)
        public bool ShowOnStart = false;
        public bool LockMouse = true;         // while the tablet is open, keep the mouse inside it (released when you minimize)
        public string Layout = "atak";        // atak | pilot  (which device picture is shown)
        public string PilotTheme = "Black";   // theme used by the pilot display (the ATAK one uses Theme)
        public bool AdBlock = true;           // block ads and trackers in the built-in browser
        public bool HttpsOnly = true;         // upgrade http:// pages to https:// (plain http is only allowed on your own network)
        public string SearchUrl = "https://duckduckgo.com/?q=";   // used when you type words instead of a web address
        public bool ShowLabels = true;        // pilot display: draw the soft-key labels next to the buttons
        public bool LiveMini = false;         // when minimized, show the live screen in the corner with no frame (instead of the little tablet)
        public double MapPixelRatio = 0;      // sharpness the map site is told it has (0 = automatic, 1-2 is normal; higher = sharper but slower)
        public int LivePct = 30;              // live view width as % of the monitor's width (big enough to read a video comfortably)
        public int LiveX = int.MinValue;      // left edge from the monitor's left (MinValue = bottom-right corner)
        public int LiveY = int.MinValue;      // top edge from the monitor's top (MinValue = resting on the bottom)
        public bool LabelInset = false;       // pilot display: shrink the page so it never sits under the soft-key labels
        public bool ShowPanel = true;         // clock / timer panel on the screen
        public bool AlertBeep = true;         // beep when a timer ends
        public bool Zulu = false;             // clock shows UTC instead of local time
        public int[] TimerSecs = new int[] { 60, 300, 600, 900 };   // the four timer presets, in seconds
        public bool GameOnly = false;         // hide the tablet / live view unless the game window is in front
        public string GameProcess = "RobloxPlayerBeta";
        public string[] Bookmarks = new string[6] { "", "", "", "", "", "" };   // "name|address" for the BM buttons
        public string[] Presets = new string[3] { "", "", "" };                  // saved window layouts
        // what each pilot-display button does, per page: order is T1-T6, L1-L6, R1-R6, B1-B6
        public string[][] Pages = DefaultPages();

        public static string[][] DefaultPages()
        {
            return new string[][]
            {
                "MAP,WEB,NOTE,TIME,LIVE,PAGE,TM1,TM2,TM3,TM4,STW,CLR,BACK,FWD,RFSH,ADS,XPAR,LOCK,MSN,MRST,UTC,CPY,SHOT,ZRST".Split(','),
                "MAP,LOG,CALC,N9L,CHK,PAGE,BM1,BM2,BM3,BM4,BM5,BM6,ZM+,ZM-,ZRST,SZ+,SZ-,DRAG,LOGV,PR1,PR2,PR3,SHOT,CPY".Split(','),
                "MAP,WEB,NOTE,TIME,LIVE,PAGE,SET,UPD,CLRD,NONE,NONE,NONE,BACK,FWD,RFSH,ADS,LOCK,XPAR,PR1,PR2,PR3,UTC,SHOT,CPY".Split(',')
            };
        }

        public string ThemeFor(string layoutId) { return layoutId == "pilot" ? PilotTheme : Theme; }
        public void SetThemeFor(string layoutId, string t) { if (layoutId == "pilot") PilotTheme = t; else Theme = t; }

        public static Config Load(string path)
        {
            Config c = new Config();
            if (!File.Exists(path)) return c;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq < 1) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                string v = line.Substring(eq + 1).Trim();
                int n;
                switch (k)
                {
                    case "url": c.Url = v; break;
                    case "togglekey": c.ToggleKey = v; break;
                    case "sizekey": c.SizeKey = v; break;
                    case "opacitykey": c.OpacityKey = v; break;
                    case "peekkey": c.PeekKey = v; break;
                    case "noteskey": c.NotesKey = v; break;
                    case "livekey": c.LiveKey = v; break;
                    case "dragkey": c.DragKey = v; break;
                    case "timer1key": c.Timer1Key = v; break;
                    case "timer2key": c.Timer2Key = v; break;
                    case "stopwatchkey": c.StopwatchKey = v; break;
                    case "theme": c.Theme = Themes.Normalize(v); break;
                    case "monitor": c.Monitor = v; break;
                    case "opensize": if (int.TryParse(v, out n)) c.OpenSize = Math.Max(25, Math.Min(90, n)); break;
                    case "anchor": v = v.ToLowerInvariant(); if (v == "center" || v == "bottomright" || v == "custom") c.Anchor = v; break;
                    case "cx": if (int.TryParse(v, out n)) c.CX = n; break;
                    case "cy": if (int.TryParse(v, out n)) c.CY = n; break;
                    case "opacity": if (int.TryParse(v, out n)) c.Opacity = Math.Max(25, Math.Min(100, n)); break;
                    case "pagewidth": if (int.TryParse(v, out n)) c.PageWidth = Math.Max(0, n); break;
                    case "animate": c.Animate = v.ToLowerInvariant() != "false"; break;
                    case "animms": if (int.TryParse(v, out n)) c.AnimMs = Math.Max(0, Math.Min(3000, n)); break;
                    case "showdock": c.ShowDock = v.ToLowerInvariant() != "false"; break;
                    case "dockwidth": if (int.TryParse(v, out n)) c.DockWidth = Math.Max(200, Math.Min(700, n)); break;
                    case "dockpeek": if (int.TryParse(v, out n)) c.DockPeek = Math.Max(20, Math.Min(70, n)); break;
                    case "dockopacity": if (int.TryParse(v, out n)) c.DockOpacity = Math.Max(25, Math.Min(100, n)); break;
                    case "dockx": if (int.TryParse(v, out n)) c.DockX = n; break;
                    case "docky": if (int.TryParse(v, out n)) c.DockY = n; break;
                    case "updateurl": c.UpdateUrl = v; break;
                    case "showonstart": c.ShowOnStart = v.ToLowerInvariant() == "true"; break;
                    case "lockmouse": c.LockMouse = v.ToLowerInvariant() != "false"; break;
                    case "layout": c.Layout = Layouts.Get(v).Id; break;
                    case "pilottheme": c.PilotTheme = Themes.Normalize(v); break;
                    case "adblock": c.AdBlock = v.ToLowerInvariant() != "false"; break;
                    case "httpsonly": c.HttpsOnly = v.ToLowerInvariant() != "false"; break;
                    case "searchurl": if (v.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) c.SearchUrl = v; break;
                    case "showlabels": c.ShowLabels = v.ToLowerInvariant() != "false"; break;
                    case "showpanel": c.ShowPanel = v.ToLowerInvariant() != "false"; break;
                    case "labelinset": c.LabelInset = v.ToLowerInvariant() == "true"; break;
                    case "livemini": c.LiveMini = v.ToLowerInvariant() == "true"; break;
                    case "livepct": if (int.TryParse(v, out n)) c.LivePct = Math.Max(15, Math.Min(60, n)); break;
                    case "mappixelratio": { double dv; if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dv)) c.MapPixelRatio = Math.Max(0, Math.Min(3, dv)); break; }
                    case "livex": if (int.TryParse(v, out n)) c.LiveX = n; break;
                    case "livey": if (int.TryParse(v, out n)) c.LiveY = n; break;
                    case "alertbeep": c.AlertBeep = v.ToLowerInvariant() != "false"; break;
                    case "zulu": c.Zulu = v.ToLowerInvariant() == "true"; break;
                    case "timers":
                        {
                            string[] parts = v.Split(',');
                            for (int ti = 0; ti < 4 && ti < parts.Length; ti++)
                                if (int.TryParse(parts[ti].Trim(), out n)) c.TimerSecs[ti] = Math.Max(1, Math.Min(86399, n));
                            break;
                        }
                    case "gameonly": c.GameOnly = v.ToLowerInvariant() == "true"; break;
                    case "gameprocess": if (v.Length > 0) c.GameProcess = v; break;
                    default:
                        if (k.Length == 5 && k.StartsWith("page") && k[4] >= '1' && k[4] <= '3')
                        {
                            string[] ids = v.Split(',');
                            if (ids.Length == 24) { for (int pi = 0; pi < 24; pi++) ids[pi] = ids[pi].Trim().ToUpperInvariant(); c.Pages[k[4] - '1'] = ids; }
                        }
                        else if (k.Length == 9 && k.StartsWith("bookmark") && k[8] >= '1' && k[8] <= '6') c.Bookmarks[k[8] - '1'] = v;
                        else if (k.Length == 7 && k.StartsWith("preset") && k[6] >= '1' && k[6] <= '3') c.Presets[k[6] - '1'] = v;
                        break;
                }
            }
            return c;
        }

        public void Save(string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("; Tac Map Overlay settings. Most of these can be changed from the gear icon on the tablet.");
            sb.AppendLine("; Keys: any keyboard key (F1-F24, letters, digits, arrows, `, ...), with optional Ctrl+ / Alt+ / Shift+ (e.g. Ctrl+F1, Ctrl+Alt+M)");
            sb.AppendLine("; Theme: Coyote Tan, Black, Ranger Green, Arid, JG Camo, MultiCam");
            sb.AppendLine("Url=" + Url);
            sb.AppendLine("ToggleKey=" + ToggleKey);
            sb.AppendLine("SizeKey=" + SizeKey);
            sb.AppendLine("OpacityKey=" + OpacityKey);
            sb.AppendLine("; More keys (leave empty to switch one off): PeekKey is HOLD-to-peek, DragKey moves the live view");
            sb.AppendLine("PeekKey=" + PeekKey);
            sb.AppendLine("NotesKey=" + NotesKey);
            sb.AppendLine("LiveKey=" + LiveKey);
            sb.AppendLine("DragKey=" + DragKey);
            sb.AppendLine("Timer1Key=" + Timer1Key);
            sb.AppendLine("Timer2Key=" + Timer2Key);
            sb.AppendLine("StopwatchKey=" + StopwatchKey);
            sb.AppendLine("Theme=" + Theme);
            sb.AppendLine("Monitor=" + Monitor);
            sb.AppendLine("OpenSize=" + OpenSize);
            sb.AppendLine("Anchor=" + Anchor);
            if (CX != int.MinValue) { sb.AppendLine("CX=" + CX); sb.AppendLine("CY=" + CY); }
            sb.AppendLine("Opacity=" + Opacity);
            sb.AppendLine("PageWidth=" + PageWidth);
            sb.AppendLine("Animate=" + (Animate ? "true" : "false"));
            sb.AppendLine("AnimMs=" + AnimMs);
            sb.AppendLine("ShowDock=" + (ShowDock ? "true" : "false"));
            sb.AppendLine("DockWidth=" + DockWidth);
            sb.AppendLine("DockPeek=" + DockPeek);
            sb.AppendLine("DockOpacity=" + DockOpacity);
            if (DockX != int.MinValue) sb.AppendLine("DockX=" + DockX);
            if (DockY != int.MinValue) sb.AppendLine("DockY=" + DockY);
            if (UpdateUrl.Length > 0) sb.AppendLine("UpdateUrl=" + UpdateUrl);
            sb.AppendLine("ShowOnStart=" + (ShowOnStart ? "true" : "false"));
            sb.AppendLine("LockMouse=" + (LockMouse ? "true" : "false"));
            sb.AppendLine("; Layout: atak | pilot.  PilotTheme is the theme used by the pilot display.");
            sb.AppendLine("Layout=" + Layout);
            sb.AppendLine("PilotTheme=" + PilotTheme);
            sb.AppendLine("; Built-in browser: ad/tracker blocking, https-only, and the search page used for typed words");
            sb.AppendLine("AdBlock=" + (AdBlock ? "true" : "false"));
            sb.AppendLine("HttpsOnly=" + (HttpsOnly ? "true" : "false"));
            sb.AppendLine("SearchUrl=" + SearchUrl);
            sb.AppendLine("; Soft-key labels (pilot display), clock/timer panel, timer beep, UTC clock, and the 4 timer presets (seconds)");
            sb.AppendLine("ShowLabels=" + (ShowLabels ? "true" : "false"));
            sb.AppendLine("ShowPanel=" + (ShowPanel ? "true" : "false"));
            sb.AppendLine("LabelInset=" + (LabelInset ? "true" : "false"));
            sb.AppendLine("LiveMini=" + (LiveMini ? "true" : "false"));
            sb.AppendLine("LivePct=" + LivePct);
            sb.AppendLine("; MapPixelRatio: how sharp the map site is told to draw (0 = automatic). Try 2 if the map ever looks soft.");
            sb.AppendLine("MapPixelRatio=" + MapPixelRatio.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            if (LiveX != int.MinValue) sb.AppendLine("LiveX=" + LiveX);
            if (LiveY != int.MinValue) sb.AppendLine("LiveY=" + LiveY);
            sb.AppendLine("AlertBeep=" + (AlertBeep ? "true" : "false"));
            sb.AppendLine("Zulu=" + (Zulu ? "true" : "false"));
            sb.AppendLine("Timers=" + string.Join(",", Array.ConvertAll(TimerSecs, delegate (int s) { return s.ToString(); })));
            sb.AppendLine("; Game-aware: hide the tablet / live view unless this program is in front (GameOnly=true)");
            sb.AppendLine("GameOnly=" + (GameOnly ? "true" : "false"));
            sb.AppendLine("GameProcess=" + GameProcess);
            sb.AppendLine("; Pilot-display buttons per page (T1-T6, L1-L6, R1-R6, B1-B6), bookmarks (name|address), saved layouts");
            for (int pg = 0; pg < 3; pg++) sb.AppendLine("Page" + (pg + 1) + "=" + string.Join(",", Pages[pg]));
            for (int bm = 0; bm < 6; bm++) if (Bookmarks[bm].Length > 0) sb.AppendLine("Bookmark" + (bm + 1) + "=" + Bookmarks[bm]);
            for (int pr = 0; pr < 3; pr++) if (Presets[pr].Length > 0) sb.AppendLine("Preset" + (pr + 1) + "=" + Presets[pr]);
            File.WriteAllText(path, sb.ToString());
        }
    }

    // The window that holds the web page. It sits behind the tablet frame's transparent screen.
    class WebHost : Form
    {
        public WebHost()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            Text = "Tac Map Overlay (page)";
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }
    }

    // The tablet frame. Painted with UpdateLayeredWindow (per-pixel alpha), never with WM_PAINT.
    partial class OverlayForm : Form
    {
        static int ImgW { get { return DeviceArt.LW; } }      // logical size; the photo may have more pixels (DeviceArt.K per unit)
        static int ImgH { get { return DeviceArt.LH; } }
        static Rectangle HoleRect { get { return DeviceArt.ScreenArea; } }   // the screen opening in the photo
        static int HoleRadius { get { return DeviceArt.Cur.HoleRadius; } }
        // how much of the tablet shows when minimized (all of it, as a frameless live view, when LiveMini is on)
        double PeekFrac { get { return cfg.LiveMini ? 1.0 : Math.Max(0.2, Math.Min(0.7, cfg.DockPeek / 100.0)); } }
        public bool LiveShown;          // the frameless live view is on screen
        static Point GearPt { get { return DeviceArt.Cur.Gear; } }           // gear button position on the photo
        static Point GlobePt { get { return new Point(DeviceArt.Cur.Gear.X - 31, DeviceArt.Cur.Gear.Y); } }   // browser button, just left of the gear
        const int GearR = 12;
        string CurTheme { get { return cfg.ThemeFor(DeviceArt.Cur.Id); } }
        const int HK_TOGGLE = 1, HK_INTERACT = 2, HK_SIZE = 3, HK_OPACITY = 4;
        const int HK_PEEK = 5, HK_NOTES = 6, HK_LIVE = 7, HK_DRAG = 8, HK_T1 = 9, HK_T2 = 10, HK_STW = 11, HK_LAST = 12;

        readonly Config cfg;
        readonly string cfgPath;
        Bitmap deviceOrig;              // original photo
        Bitmap srcP;                    // themed photo, premultiplied ARGB with soft edge
        bool[] outside;
        byte[] softAlpha;
        WebHost webHost;
        CoreWebView2Controller ctl;

        Rectangle frame;                // where the frame window currently is on screen
        Rectangle openRect, dockRect;
        bool open;                      // logical state: tablet is open (or opening)
        bool bezelShown;
        bool webShown;
        bool grabbed;                   // mouse + keyboard are currently handed to the tablet
        bool inSettings;
        bool gearHover, globeHover;
        class ArtEntry { public int W, H, Ver; public Bitmap B; }
        readonly List<ArtEntry> artCaches = new List<ArtEntry>();   // finished (sharpened) art at sizes used before: dock, open ...
        List<Bitmap> mips;              // pre-shrunk copies of the art, for fast scaling while animating / resizing
        int mipVer = -1, artVersion;
        System.Windows.Forms.Timer warmTimer;

        ArtEntry FindArt(int w, int h)
        {
            foreach (ArtEntry e in artCaches) if (e.W == w && e.H == h && e.Ver == artVersion) return e;
            return null;
        }

        void StoreArt(int w, int h, Bitmap b)
        {
            for (int i = artCaches.Count - 1; i >= 0; i--)
                if (artCaches[i].Ver != artVersion || artCaches.Count >= 4) { artCaches[i].B.Dispose(); artCaches.RemoveAt(i); }
            ArtEntry n = new ArtEntry(); n.W = w; n.H = h; n.Ver = artVersion; n.B = (Bitmap)b.Clone();
            artCaches.Add(n);
        }

        // Half-step ladder (each about 0.71x the one before) of the themed art: a frame is drawn from the nearest bigger one,
        // so scaling it is quick and still looks clean.
        void BuildMips()
        {
            if (mips != null) foreach (Bitmap m in mips) m.Dispose();
            mips = new List<Bitmap>();
            Bitmap prev = srcP; double wd = srcP.Width;
            while (true)
            {
                wd *= 0.7071;
                int mw = (int)wd;
                if (mw < 260) break;
                int mh = (int)Math.Round(mw * (double)srcP.Height / srcP.Width);
                Bitmap m = new Bitmap(mw, mh, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(m))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(prev, new Rectangle(0, 0, mw, mh), 0, 0, prev.Width, prev.Height, GraphicsUnit.Pixel);
                }
                mips.Add(m); prev = m;
            }
            mipVer = artVersion;
        }

        Bitmap DrawFromMips(int w, int h)
        {
            if (mips == null || mipVer != artVersion) BuildMips();
            Bitmap src = srcP;
            foreach (Bitmap m in mips) { if (m.Width >= w) src = m; else break; }
            double srcH = Math.Min(src.Height, h * (double)src.Width / w);
            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            BitmapData sd = src.LockBits(new Rectangle(0, 0, src.Width, src.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            BitmapData dd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try { ScaleBilinear(sd.Scan0, sd.Stride, src.Width, src.Height, dd.Scan0, dd.Stride, w, h, srcH); }
            finally { src.UnlockBits(sd); bmp.UnlockBits(dd); }
            return bmp;
        }

        // Bilinear scaling of premultiplied 32-bit pixels, spread over all CPU cores (GDI+'s own scaler is several times slower).
        static unsafe void ScaleBilinear(IntPtr srcPtr, int sStride, int sw, int sh, IntPtr dstPtr, int dStride, int dw, int dh, double srcRectH)
        {
            int[] xi = new int[dw], xw = new int[dw];
            double kx = (double)sw / dw;
            for (int x = 0; x < dw; x++)
            {
                double fx = (x + 0.5) * kx - 0.5;
                if (fx < 0) fx = 0; if (fx > sw - 1) fx = sw - 1;
                int i = (int)fx; xi[x] = i; xw[x] = (int)((fx - i) * 256);
            }
            double ky = srcRectH / dh;
            System.Threading.Tasks.Parallel.For(0, dh, delegate (int y)
            {
                double fy = (y + 0.5) * ky - 0.5;
                if (fy < 0) fy = 0; if (fy > sh - 1) fy = sh - 1;
                int y0 = (int)fy, y1 = Math.Min(sh - 1, y0 + 1), wy = (int)((fy - y0) * 256), iy = 256 - wy;
                byte* r0 = (byte*)srcPtr.ToPointer() + (long)y0 * sStride;
                byte* r1 = (byte*)srcPtr.ToPointer() + (long)y1 * sStride;
                byte* d = (byte*)dstPtr.ToPointer() + (long)y * dStride;
                for (int x = 0; x < dw; x++)
                {
                    int xa = xi[x] * 4, xb = Math.Min(sw - 1, xi[x] + 1) * 4, wx = xw[x], ix = 256 - wx, o = x * 4;
                    for (int c = 0; c < 4; c++)
                    {
                        int top = (r0[xa + c] * ix + r0[xb + c] * wx) >> 8;
                        int bot = (r1[xa + c] * ix + r1[xb + c] * wx) >> 8;
                        d[o + c] = (byte)((top * iy + bot * wy) >> 8);
                    }
                }
            });
        }

        // A moment after the look or size changes (and at start-up), quietly prepare what the next open / close will need,
        // so the animation itself has nothing heavy left to do.
        void ScheduleWarmup()
        {
            if (warmTimer == null)
            {
                warmTimer = new System.Windows.Forms.Timer();
                warmTimer.Interval = 300;
                warmTimer.Tick += delegate
                {
                    warmTimer.Stop();
                    if (animating || inSizeMove || inSettings) { warmTimer.Start(); return; }
                    try
                    {
                        if (mips == null || mipVer != artVersion) BuildMips();
                        Rectangle o = ComputeOpenRect();
                        int w = o.Width, h = HeightAt(w, 1.0);
                        if (FindArt(w, h) == null) using (Bitmap warm = RenderTablet(w, 1.0, false, true, false)) { }
                    }
                    catch { }
                    ScheduleTrim();   // building the art leaves a lot of scratch memory behind: give it back
                };
            }
            warmTimer.Stop(); warmTimer.Start();
        }

        // ---------- memory ----------
        // Building / scaling the art uses big temporary arrays. Once everything is idle, collect them and (while minimized)
        // hand the unused pages back to Windows. The page engine is also told to use less memory while it is hidden.
        System.Windows.Forms.Timer trimTimer;

        void ScheduleTrim()
        {
            if (trimTimer == null)
            {
                trimTimer = new System.Windows.Forms.Timer();
                trimTimer.Interval = 2500;
                trimTimer.Tick += delegate
                {
                    trimTimer.Stop();
                    if (animating || inSizeMove || inSettings) { trimTimer.Start(); return; }
                    TrimMemory(!open && !LiveShown);
                };
            }
            trimTimer.Stop(); trimTimer.Start();
        }

        void TrimMemory(bool giveBackPages)
        {
            try
            {
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                if (giveBackPages) using (Process me = Process.GetCurrentProcess()) Native.EmptyWorkingSet(me.Handle);
            }
            catch { }
            SetWebMemory(!webShown && !LiveShown);
        }

        // low = the page is hidden (minimized without the live view): the browser engine may trim its caches
        void SetWebMemory(bool low)
        {
            try
            {
                if (ctl != null && ctl.CoreWebView2 != null)
                    ctl.CoreWebView2.MemoryUsageTargetLevel = low ? CoreWebView2MemoryUsageTargetLevel.Low : CoreWebView2MemoryUsageTargetLevel.Normal;
            }
            catch { }
        }
        bool showOnFirstRun;
        bool inPush;
        bool inSizeMove;
        Rectangle dragStart;
        IntPtr prevFg = IntPtr.Zero;
        NotifyIcon tray;
        System.Windows.Forms.Timer clipTimer;
        readonly uint myPid = (uint)Process.GetCurrentProcess().Id;
        uint browserPid;                // the page engine's process: its pop-ups (menus, drop-downs) count as the tablet
        int awayTicks;                 // consecutive timer ticks with another program in front while expanded
        int clipTick;
        int grabUntil;                // Environment.TickCount until which we keep re-taking focus after expanding
        bool fsFore;                    // a fullscreen app/game is in front: the minimized tablet may sit at the very bottom edge
        System.Windows.Forms.Timer dockTimer;
        readonly uint procId = (uint)Process.GetCurrentProcess().Id;
        bool dockDown, dockDragging;    // dragging the minimized tablet
        Point downScreen;
        Rectangle downFrame;
        ContextMenuStrip menu;
        ToolStripMenuItem miAutostart;

        // open/close animation
        System.Windows.Forms.Timer animTimer;
        Stopwatch animSw = new Stopwatch();
        long animLast;
        bool animating;
        int animDir;                    // +1 opening, -1 closing
        double animC;                   // linear clock 0..1
        double animV;                   // visual progress: 0 = docked, 1 = fully open

        static readonly int[] SizePresets = new int[] { 45, 62, 78, 90 };
        static readonly int[] OpacityLevels = new int[] { 100, 80, 60, 40 };

        static int HeightAt(int w, double vis) { return Math.Max(1, (int)Math.Round(ImgH * (w / (double)ImgW) * vis)); }
        static int HeightFor(int w) { return HeightAt(w, 1.0); }

        public Rectangle Frame { get { return frame; } }

        public OverlayForm(Config c, string path)
        {
            cfg = c; cfgPath = path;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "Tac Map Overlay";
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);

            InitTimers();
            LoadArt();
        }

        // (Re)load the device picture for the layout in cfg.Layout.
        void LoadArt()
        {
            DeviceArt.Use(Layouts.Get(cfg.Layout));
            Themes.ClearWrapCache();
            Bitmap old = deviceOrig;
            deviceOrig = DeviceArt.LoadOriginal();
            if (old != null) old.Dispose();
            outside = DeviceArt.BuildMask(deviceOrig);
            softAlpha = DeviceArt.BuildSoftAlpha(outside);
            RebuildSource();
        }

        void RebuildSource()
        {
            Bitmap themed = Themes.Build(deviceOrig, outside, CurTheme);
            Bitmap np = DeviceArt.MakeSource(themed, softAlpha);
            themed.Dispose();
            Bitmap old = srcP;
            srcP = np;
            if (old != null) old.Dispose();
            artVersion++;
            if (warmTimer != null || bezelShown) ScheduleWarmup();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        // ---------- layout helpers ----------
        Screen CurrentScreen()
        {
            if (!string.IsNullOrEmpty(cfg.Monitor))
                foreach (Screen s in Screen.AllScreens) if (s.DeviceName == cfg.Monitor) return s;
            return Screen.PrimaryScreen;
        }

        Rectangle ComputeOpenRect()
        {
            Rectangle wa = CurrentScreen().WorkingArea;
            int w = (int)(wa.Width * cfg.OpenSize / 100.0);
            int h = HeightFor(w);
            if (h > wa.Height * 0.94) { h = (int)(wa.Height * 0.94); w = (int)(h * (double)ImgW / ImgH); }
            w = Math.Max(300, w); h = HeightFor(w);
            int x, y;
            if (cfg.Anchor == "bottomright") { x = wa.Right - w - 20; y = wa.Bottom - h - 20; }
            else if (cfg.Anchor == "custom" && cfg.CX != int.MinValue) { x = cfg.CX - w / 2; y = cfg.CY - h / 2; }
            else { x = wa.Left + (wa.Width - w) / 2; y = wa.Top + (wa.Height - h) / 2; }
            x = Math.Max(wa.Left, Math.Min(x, wa.Right - w));
            y = Math.Max(wa.Top, Math.Min(y, wa.Bottom - h));
            return new Rectangle(x, y, w, h);
        }

        // Is a fullscreen app (a game) the window in front? Then there is no taskbar on screen and the minimized tablet
        // can sit at the very bottom edge of the monitor. Otherwise it rests on top of the taskbar.
        bool IsFullscreenForeground()
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == Handle || (webHost != null && fg == webHost.Handle)) return fsFore;
            uint pid; Native.GetWindowPid(fg, out pid);
            if (pid == procId) return fsFore;
            StringBuilder cls = new StringBuilder(64);
            Native.GetClassName(fg, cls, 64);
            string c = cls.ToString();
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd") return false;
            Native.RECT r;
            if (!Native.GetWindowRect(fg, out r)) return false;
            Rectangle b = CurrentScreen().Bounds;
            return r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
        }

        int FloorBottom()
        {
            return fsFore ? CurrentScreen().Bounds.Bottom : CurrentScreen().WorkingArea.Bottom;
        }

        // The minimized tablet: the whole model, pushed below the floor (the screen's bottom edge in a fullscreen game,
        // the top of the taskbar otherwise) so only its top shows - the window is cut off exactly at the floor, so there is
        // no visible crop line. Default is the bottom-right corner; you can drag it anywhere.
        // The frameless live view: the whole tablet's footprint, a comfortable size (LivePct of the monitor's width).
        Rectangle ComputeLiveRect()
        {
            Rectangle b = CurrentScreen().Bounds;
            int w = (int)(b.Width * cfg.LivePct / 100.0);
            int h = HeightAt(w, 1.0);
            int maxH = (int)(b.Height * 0.6);
            if (h > maxH) { h = maxH; w = (int)(h * (double)ImgW / ImgH); }
            w = Math.Max(260, w); h = HeightAt(w, 1.0);
            int margin = 14;
            int x = cfg.LiveX == int.MinValue ? b.Right - w - margin : b.Left + cfg.LiveX;
            int y = cfg.LiveY == int.MinValue ? FloorBottom() - h - margin : b.Top + cfg.LiveY;
            x = Math.Max(b.Left, Math.Min(x, b.Right - w));
            y = Math.Max(b.Top, Math.Min(y, b.Bottom - h));
            return new Rectangle(x, y, w, h);
        }

        Rectangle ComputeDockRect()
        {
            if (cfg.LiveMini) return ComputeLiveRect();
            Rectangle b = CurrentScreen().Bounds;
            int w = cfg.DockWidth;
            int h = HeightAt(w, PeekFrac);
            int x = cfg.DockX == int.MinValue ? b.Right - w - 16 : b.Left + cfg.DockX;
            int y = cfg.DockY == int.MinValue ? FloorBottom() - h : b.Top + cfg.DockY;
            x = Math.Max(b.Left, Math.Min(x, b.Right - w));
            y = Math.Max(b.Top, Math.Min(y, b.Bottom - h));
            return new Rectangle(x, y, w, h);
        }

        // Opacity for a given amount of the tablet showing: minimized opacity -> open opacity.
        int AlphaFor(double vis)
        {
            // live view: the frame is always full height, so fade by animation progress instead
            double t = cfg.LiveMini ? (animating ? animV : (open ? 1.0 : 0.0)) : (vis - PeekFrac) / Math.Max(0.01, 1.0 - PeekFrac);
            t = Math.Max(0.0, Math.Min(1.0, t));
            return (int)Math.Round(cfg.DockOpacity + (cfg.Opacity - cfg.DockOpacity) * t);
        }

        // Soft fade on the cut-off edge when the minimized tablet is dragged up off the bottom of the screen.
        static void FadeBottom(Bitmap bmp, int rows)
        {
            int w = bmp.Width, h = bmp.Height;
            rows = Math.Max(2, Math.Min(rows, h / 2));
            BitmapData bd = bmp.LockBits(new Rectangle(0, h - rows, w, rows), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
            byte[] px = new byte[bd.Stride * rows];
            Marshal.Copy(bd.Scan0, px, 0, px.Length);
            for (int y = 0; y < rows; y++)
            {
                double f = (rows - 1 - y) / (double)rows;   // 1 at the top of the fade, ~0 at the very bottom
                for (int x = 0; x < w; x++)
                {
                    int i = y * bd.Stride + x * 4;
                    px[i] = (byte)(px[i] * f); px[i + 1] = (byte)(px[i + 1] * f); px[i + 2] = (byte)(px[i + 2] * f); px[i + 3] = (byte)(px[i + 3] * f);
                }
            }
            Marshal.Copy(px, 0, bd.Scan0, px.Length);
            bmp.UnlockBits(bd);
        }

        // Frame rectangle + how much of the tablet is visible, for animation progress e (0 = docked, 1 = open).
        void GeomAt(double e, out Rectangle r, out double vis)
        {
            Rectangle d = dockRect, o = openRect;
            int w = (int)Math.Round(d.Width + (o.Width - d.Width) * e);
            vis = PeekFrac + (1.0 - PeekFrac) * e;
            int h = HeightAt(w, vis);
            double left = d.Left + (o.Left - d.Left) * e;
            double bottom = d.Bottom + (o.Bottom - d.Bottom) * e;
            r = new Rectangle((int)Math.Round(left), (int)Math.Round(bottom) - h, w, h);
        }

        // ---------- rendering ----------
        static GraphicsPath RoundRectF(RectangleF r, float radius)
        {
            float d = radius * 2f;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        PointF GearCenter(int w) { double sc = w / (double)ImgW; return new PointF((float)(GearPt.X * sc), (float)(GearPt.Y * sc)); }
        float GearRadius(int w) { return (float)Math.Max(9.0, GearR * w / (double)ImgW); }

        bool OverGear(Point p)
        {
            PointF c = GearCenter(frame.Width); float r = Math.Max(13f, GearRadius(frame.Width));
            float dx = p.X - c.X, dy = p.Y - c.Y;
            return dx * dx + dy * dy <= r * r;
        }

        PointF GlobeCenter(int w) { double sc = w / (double)ImgW; return new PointF((float)(GlobePt.X * sc), (float)(GlobePt.Y * sc)); }

        bool OverGlobe(Point p)
        {
            PointF c = GlobeCenter(frame.Width); float r = Math.Max(13f, GearRadius(frame.Width));
            float dx = p.X - c.X, dy = p.Y - c.Y;
            return dx * dx + dy * dy <= r * r;
        }

        // Draws text centred on (cx, cy), shrinking the font if it would be wider than maxW.
        static void CenterText(Graphics g, string text, float px, Brush brush, float cx, float cy, float maxW)
        {
            while (true)
            {
                using (Font f = new Font("Consolas", px, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    SizeF sz = g.MeasureString(text, f);
                    if (sz.Width <= maxW || px <= 6f) { g.DrawString(text, f, brush, cx - sz.Width / 2f, cy - sz.Height / 2f); return; }
                }
                px -= 0.5f;
            }
        }

        static GraphicsPath GearPath(float cx, float cy, float ro, float ri, int teeth)
        {
            GraphicsPath path = new GraphicsPath();
            List<PointF> pts = new List<PointF>();
            double step = 2 * Math.PI / teeth;
            Func<float, double, PointF> P = delegate (float rr, double a) { return new PointF(cx + (float)(rr * Math.Cos(a)), cy + (float)(rr * Math.Sin(a))); };
            for (int i = 0; i < teeth; i++)
            {
                double a = i * step;
                pts.Add(P(ri, a - 0.30 * step)); pts.Add(P(ro, a - 0.16 * step));
                pts.Add(P(ro, a + 0.16 * step)); pts.Add(P(ri, a + 0.30 * step));
            }
            path.AddPolygon(pts.ToArray());
            path.AddEllipse(cx - ri * 0.42f, cy - ri * 0.42f, ri * 0.84f, ri * 0.84f); // hole (even-odd fill)
            return path;
        }

        public void DumpRender(string path, int w, double vis, bool cut)
        {
            using (Bitmap b = RenderTablet(w, vis, cut, true, false))
            {
                if (!cut) { b.Save(path); return; }
                // hole cut out: lay the picture over a stand-in "page" so the labels can be judged
                using (Bitmap comp = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(comp))
                {
                    g.Clear(Color.FromArgb(255, 20, 30, 46));
                    using (Pen p = new Pen(Color.FromArgb(60, 90, 140), 1f))
                        for (int x = 0; x < b.Width; x += 60) g.DrawLine(p, x, 0, x, b.Height);
                    g.DrawImage(b, 0, 0);
                    comp.Save(path);
                }
            }
        }

        // Draws the top `vis` fraction of the tablet at width w. holeCut = punch the screen out so the page shows through.
        Bitmap RenderTablet(int w, double vis, bool holeCut, bool hq, bool fade)
        {
            double sc = w / (double)ImgW;
            int h = HeightAt(w, vis);
            // The scaled + sharpened photo is the expensive part and doesn't change between redraws (clock ticks, hover ...): keep it.
            bool cacheable = hq && !animating;
            Bitmap bmp = null;
            if (cacheable) { ArtEntry hit = FindArt(w, h); if (hit != null) bmp = (Bitmap)hit.B.Clone(); }
            if (bmp == null && !hq) bmp = DrawFromMips(w, h);   // animating / dragging: scale from the pre-shrunk copies
            if (bmp == null)
            {
                bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = hq ? InterpolationMode.HighQualityBicubic : InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    using (ImageAttributes ia = new ImageAttributes())
                    {
                        ia.SetWrapMode(WrapMode.TileFlipXY);
                        float k = (float)DeviceArt.K;
                        g.DrawImage(srcP, new Rectangle(0, 0, w, h), 0f, 0f, ImgW * k, (float)(h / sc) * k, GraphicsUnit.Pixel, ia);
                    }
                }
                if (hq && sc / DeviceArt.SrcK > 1.05) DeviceArt.Sharpen(bmp, 0.55f);   // only when the real photo is being enlarged
                if (cacheable) StoreArt(w, h, bmp);
            }

            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                bool dark = Themes.IsDark(CurTheme);
                Color txtCol = dark ? Color.FromArgb(210, 215, 195) : Color.FromArgb(38, 40, 30);
                LayoutDef lay = DeviceArt.Cur;
                Color accent = open ? Color.FromArgb(255, 160, 20) : Color.FromArgb(60, 200, 70);
                if (AnyAlert) accent = StateColor(3);   // a timer is up: the status light flashes amber / red

                RectangleF hr = new RectangleF((float)(HoleRect.X * sc), (float)(HoleRect.Y * sc), (float)(HoleRect.Width * sc), (float)(HoleRect.Height * sc));
                using (GraphicsPath hp = RoundRectF(hr, (float)(HoleRadius * sc)))
                {
                    if (holeCut)
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        using (Brush t = new SolidBrush(Color.FromArgb(0, 0, 0, 0))) g.FillPath(t, hp);
                        g.CompositingMode = CompositingMode.SourceOver;
                    }
                    else
                    {
                        using (Brush bk = new SolidBrush(Color.FromArgb(8, 10, 8))) g.FillPath(bk, hp);
                        if (hr.Width > 220 && hr.Top + hr.Height / 2 < h - 12)
                            using (Font f = new Font("Consolas", (float)(14 * sc), FontStyle.Bold, GraphicsUnit.Pixel))
                            using (Brush gb = new SolidBrush(Color.FromArgb(150, 205, 90)))
                            {
                                StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                                g.DrawString("TAC MAP", f, gb, hr, sf);
                            }
                    }
                }

                // status light + text
                if (lay.CompactLabels)
                {
                    // small centred block on the top plate: a light, then one or two short lines
                    float cx = (float)(lay.LabelAt.X * sc), cy = (float)(lay.LabelAt.Y * sc);
                    float d = (float)Math.Max(5.0, 7 * sc), maxW = (float)(170 * sc);
                    txtCol = Color.FromArgb(215, 220, 200);   // always light text, on a dark backing so it reads on any theme
                    float fs0 = (float)Math.Max(9.0, 8.5 * sc);
                    float l1 = cy + (float)(15 * sc), l2 = Math.Max(cy + (float)(28 * sc), l1 + fs0 * 1.15f);
                    float bw = (float)(150 * sc), bh = Math.Max((float)(46 * sc), l2 + fs0 * 0.75f - (cy - (float)(10 * sc)));
                    using (GraphicsPath back = RoundRectF(new RectangleF(cx - bw / 2f, cy - (float)(10 * sc), bw, bh), (float)(7 * sc)))
                    using (Brush bb = new SolidBrush(Color.FromArgb(vis > 0.9 ? 150 : 120, 12, 14, 10))) g.FillPath(bb, back);
                    if (vis <= 0.9) using (Brush glow = new SolidBrush(Color.FromArgb(70, accent))) g.FillEllipse(glow, cx - d, cy - d, d * 2, d * 2);
                    using (Brush led = new SolidBrush(accent)) g.FillEllipse(led, cx - d / 2, cy - d / 2, d, d);
                    using (Brush txt = new SolidBrush(txtCol))
                    {
                        float fs = (float)Math.Max(9.0, 8.5 * sc);
                        if (vis > 0.9)
                        {
                            CenterText(g, cfg.LockMouse ? "MOUSE LOCKED" : "TABLET ACTIVE", fs, txt, cx, l1, maxW);
                            CenterText(g, "[" + cfg.ToggleKey + "] minimize", fs, txt, cx, l2, maxW);
                        }
                        else
                        {
                            CenterText(g, "[" + cfg.ToggleKey + "]", fs, txt, cx, l1, maxW);
                            string di = DockInfo();
                            if (di == null) di = ClockText(false);   // nothing running: show the time
                            if (di.Length > 0) using (Brush hot = new SolidBrush(AnyAlert ? StateColor(3) : Color.FromArgb(125, 235, 95)))
                                CenterText(g, di, fs, hot, cx, l2, maxW);
                        }
                    }
                }
                else if (vis > 0.9)
                {
                    float d = (float)(7 * sc), cx = (float)(150 * sc), cy = (float)(334 * sc);
                    using (Brush led = new SolidBrush(accent)) g.FillEllipse(led, cx - d / 2, cy - d / 2, d, d);
                    string status = (cfg.LockMouse ? "MOUSE LOCKED" : "TABLET ACTIVE") + "   [" + cfg.ToggleKey + "] minimize";
                    using (Font f = new Font("Consolas", Math.Max(7f, (float)(9.5 * sc)), FontStyle.Bold, GraphicsUnit.Pixel))
                    using (Brush txt = new SolidBrush(txtCol))
                    {
                        SizeF sz = g.MeasureString(status, f);
                        g.DrawString(status, f, txt, cx + d, cy - sz.Height / 2f);
                    }
                }
                else
                {
                    // docked: a glowing "program is on" light and a short label on the top frame
                    float d = (float)Math.Max(6.0, 7 * sc), cx = (float)(92 * sc), cy = (float)(40 * sc);
                    using (Brush glow = new SolidBrush(Color.FromArgb(70, accent))) g.FillEllipse(glow, cx - d, cy - d, d * 2, d * 2);
                    using (Brush led = new SolidBrush(accent)) g.FillEllipse(led, cx - d / 2, cy - d / 2, d, d);
                    using (Font f = new Font("Consolas", Math.Max(9f, (float)(10 * sc)), FontStyle.Bold, GraphicsUnit.Pixel))
                    using (Brush txt = new SolidBrush(txtCol))
                    {
                        string dl = DockInfo();
                        string label = dl != null && dl.Length > 0 ? dl : "TAC MAP  [" + cfg.ToggleKey + "]";
                        SizeF sz = g.MeasureString(label, f);
                        g.DrawString(label, f, txt, cx + d, cy - sz.Height / 2f);
                    }
                }

                // settings gear (top right of the tablet)
                PointF gc = GearCenter(w); float gr = GearRadius(w);
                using (Brush back = new SolidBrush(Color.FromArgb(30, 32, 26)))
                using (Pen ring = new Pen(gearHover ? Color.FromArgb(150, 205, 90) : Color.FromArgb(110, 120, 95), Math.Max(1f, gr / 10f)))
                {
                    g.FillEllipse(back, gc.X - gr, gc.Y - gr, gr * 2, gr * 2);
                    g.DrawEllipse(ring, gc.X - gr, gc.Y - gr, gr * 2, gr * 2);
                }
                using (GraphicsPath gp = GearPath(gc.X, gc.Y, gr * 0.68f, gr * 0.50f, 8))
                using (Brush gb = new SolidBrush(gearHover ? Color.FromArgb(150, 205, 90) : Color.FromArgb(205, 212, 192)))
                    g.FillPath(gb, gp);

                // browser button (a little globe) just left of the gear: shows / hides the address bar
                PointF bc = GlobeCenter(w);
                bool lit = globeHover || barVisible;
                Color gcol = lit ? Color.FromArgb(150, 205, 90) : Color.FromArgb(205, 212, 192);
                using (Brush back = new SolidBrush(Color.FromArgb(30, 32, 26)))
                using (Pen ring = new Pen(lit ? Color.FromArgb(150, 205, 90) : Color.FromArgb(110, 120, 95), Math.Max(1f, gr / 10f)))
                {
                    g.FillEllipse(back, bc.X - gr, bc.Y - gr, gr * 2, gr * 2);
                    g.DrawEllipse(ring, bc.X - gr, bc.Y - gr, gr * 2, gr * 2);
                }
                float gr2 = gr * 0.60f;
                using (Pen gp2 = new Pen(gcol, Math.Max(1f, gr / 9f)))
                {
                    g.DrawEllipse(gp2, bc.X - gr2, bc.Y - gr2, gr2 * 2, gr2 * 2);
                    g.DrawEllipse(gp2, bc.X - gr2 * 0.42f, bc.Y - gr2, gr2 * 0.84f, gr2 * 2);
                    g.DrawLine(gp2, bc.X - gr2, bc.Y, bc.X + gr2, bc.Y);
                    g.DrawLine(gp2, bc.X - gr2 * 0.86f, bc.Y - gr2 * 0.5f, bc.X + gr2 * 0.86f, bc.Y - gr2 * 0.5f);
                    g.DrawLine(gp2, bc.X - gr2 * 0.86f, bc.Y + gr2 * 0.5f, bc.X + gr2 * 0.86f, bc.Y + gr2 * 0.5f);
                }

                DrawAtakButtons(g, w, gr);                      // ATAK: timer + notes buttons
                if (holeCut && vis > 0.9) DrawScreenOverlays(g, w, sc);   // soft-key labels and the clock / timer panel over the page
            }
            if (fade) FadeBottom(bmp, 14);
            return bmp;
        }

        // Sends a bitmap to the layered window: position, size and pixels in ONE atomic call.
        void Push(Bitmap bmp, Point pos, int alphaPct)
        {
            int w = bmp.Width, h = bmp.Height;
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            Native.BITMAPINFOHEADER bi = new Native.BITMAPINFOHEADER();
            bi.biSize = 40; bi.biWidth = w; bi.biHeight = -h; bi.biPlanes = 1; bi.biBitCount = 32; bi.biCompression = 0;
            IntPtr bits;
            IntPtr hBmp = Native.CreateDIBSection(memDc, ref bi, 0, out bits, IntPtr.Zero, 0);
            IntPtr old = Native.SelectObject(memDc, hBmp);
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            Native.CopyMemory(bits, bd.Scan0, (UIntPtr)(uint)(w * h * 4));
            bmp.UnlockBits(bd);

            Native.POINT dst = new Native.POINT(pos.X, pos.Y);
            Native.SIZE sz = new Native.SIZE(w, h);
            Native.POINT src = new Native.POINT(0, 0);
            Native.BLENDFUNCTION bf = new Native.BLENDFUNCTION();
            bf.BlendOp = 0; bf.BlendFlags = 0; bf.AlphaFormat = 1;
            bf.SourceConstantAlpha = (byte)Math.Max(1, Math.Min(255, alphaPct * 255 / 100));
            inPush = true;
            Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref sz, memDc, ref src, 0, ref bf, Native.ULW_ALPHA);
            inPush = false;

            Native.SelectObject(memDc, old);
            Native.DeleteObject(hBmp);
            Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }

        void SetFrame(Rectangle r, double vis, bool holeCut, bool hq)
        {
            // frameless live view: the frame window only carries the alarm banner / move outline, otherwise fully transparent
            if (LiveShown && !open && !animating) { RenderLiveOverlay(dockRect); return; }
            // fade the cut-off edge only when the minimized tablet is floating above the bottom of the screen
            bool fade = vis < 0.9 && r.Y + HeightAt(r.Width, vis) < FloorBottom() - 1;
            using (Bitmap b = RenderTablet(r.Width, vis, holeCut, hq, fade))
            {
                r.Height = b.Height;
                frame = r;
                Push(b, r.Location, AlphaFor(vis));
            }
        }

        // Re-draw the frame where it currently is (after a theme / mode / opacity change).
        void RenderState(bool hq)
        {
            if (!bezelShown || animating) return;
            bool isOpen = open;
            SetFrame(frame, isOpen ? 1.0 : PeekFrac, isOpen && webShown, hq);
        }

        void ShowBezel()
        {
            if (bezelShown) return;
            Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            bezelShown = true;
        }

        // Put the frame back on top - but only if something actually got above it. Re-asserting "topmost" on every tick makes
        // Windows re-compose the screen each time, which is what a game in front would feel.
        void RaiseBezel()
        {
            if (!bezelShown) return;
            if (Native.GetWindow(Handle, Native.GW_HWNDPREV) == IntPtr.Zero) return;   // nothing above us already
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        // ---------- the page window ----------
        void PositionWeb()
        {
            if (webHost == null || ctl == null) return;
            double sc = frame.Width / (double)ImgW;
            int m = Math.Max(2, (int)Math.Round(3 * sc));   // tuck a few pixels under the frame so there is no gap
            int x = frame.Left + (int)Math.Round(HoleRect.X * sc) - m, y = frame.Top + (int)Math.Round(HoleRect.Y * sc) - m;
            int w = (int)Math.Round(HoleRect.Width * sc) + 2 * m, h = (int)Math.Round(HoleRect.Height * sc) + 2 * m;
            Native.SetWindowPos(webHost.Handle, IntPtr.Zero, x, y, w, h, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            Native.SetWindowRgn(webHost.Handle, Native.CreateRoundRectRgn(0, 0, w + 1, h + 1, (int)((HoleRadius * sc + m) * 2), (int)((HoleRadius * sc + m) * 2)), true);
            LayoutBar(sc, m, w, h);   // address bar on top (if shown) and the page below it
            // Lay the page out as if the screen were PageWidth CSS pixels wide, then scale it to fit.
            if (cfg.PageWidth > 0) ctl.ZoomFactor = Math.Max(0.25, Math.Min(3.0, (HoleRect.Width * sc) / cfg.PageWidth));
        }

        void ShowWeb()
        {
            if (webHost == null || ctl == null) return;
            PositionWeb();
            SetWebMemory(false);
            Native.ShowWindow(webHost.Handle, Native.SW_SHOWNOACTIVATE);
            Native.SetWindowPos(webHost.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            ctl.IsVisible = true;
            webShown = true;
            RaiseBezel();
        }

        void HideWeb()
        {
            webShown = false;
            LiveShown = false;
            if (ctl != null) ctl.IsVisible = false;
            if (webHost != null) Native.ShowWindow(webHost.Handle, Native.SW_HIDE);
        }

        // ---------- startup ----------
        public void Init(bool firstRun)
        {
            showOnFirstRun = firstRun;
            IntPtr h = Handle; // create the window handle (still hidden)
            webHost = new WebHost();
            IntPtr wh = webHost.Handle;
            InitPanels();   // timers, notes pad (Panels.cs)
            webHost.Activated += delegate
            {
                BeginInvoke((MethodInvoker)delegate { RaiseBezel(); if (grabbed && open && !animating && !inSettings && !inSizeMove) UpdateClip(); });
            };
            // While expanded, check focus a few times a second: keep the mouse locked while the tablet has focus,
            // and never leave it trapped if you alt-tab away or a game pop-up takes focus.
            clipTimer = new System.Windows.Forms.Timer();
            clipTimer.Interval = 15;   // (Windows ticks timers every ~15.6 ms anyway; no need to raise the whole system's timer rate for this)
            clipTimer.Tick += delegate
            {
                if (!grabbed || inSettings || inSizeMove || animating) return;
                IntPtr fgw = Native.GetForegroundWindow();
                uint pid; Native.GetWindowPid(fgw, out pid);
                if (fgw == IntPtr.Zero) return;   // Windows is mid-switch (alt-tab): no verdict yet
                if (pid == myPid || (browserPid != 0 && pid == browserPid))
                {
                    awayTicks = 0;
                    if ((++clipTick & 3) == 0) UpdateClip();   // the lock itself stays in force; refresh it a few times a second
                    if (cfg.LockMouse)
                    {
                        // second layer: if anything cleared the clip, snap the cursor straight back into the tablet
                        Native.POINT cp;
                        if (Native.GetCursorPos(out cp))
                        {
                            int nx = Math.Max(frame.Left, Math.Min(frame.Right - 1, cp.X));
                            int ny = Math.Max(frame.Top, Math.Min(frame.Bottom - 1, cp.Y));
                            if (nx != cp.X || ny != cp.Y) Native.SetCursorPos(nx, ny);
                        }
                    }
                }
                else if (unchecked(Environment.TickCount - grabUntil) < 0)
                {
                    // right after expanding, insist on getting focus (the game may still be grabbing it back)
                    Native.ClipCursor(IntPtr.Zero);
                    if (webHost != null) Native.ForceForeground(webHost.Handle);
                }
                else if (++awayTicks >= 8)
                {
                    // Focus went to another window for good (alt-tab, a game pop-up, another app): tuck the tablet away
                    // instead of leaving it open on top of that window, and do not touch the mouse or steal focus back.
                    awayTicks = 0;
                    CloseTablet();
                }
            };

            fsFore = IsFullscreenForeground();
            dockRect = ComputeDockRect();
            openRect = ComputeOpenRect();
            frame = dockRect;
            BuildTray();
            RegisterKeys();

            animTimer = new System.Windows.Forms.Timer();
            animTimer.Interval = 8;
            animTimer.Tick += AnimTick;

            // While minimized: follow whether a fullscreen game is in front (bottom edge vs. above the taskbar),
            // and keep the little tablet above other topmost windows such as the taskbar.
            dockTimer = new System.Windows.Forms.Timer();
            dockTimer.Interval = 500;
            dockTimer.Tick += delegate
            {
                if (cfg.GameOnly && !open && !animating) ApplyGameVisibility();
                else if (gameHidden && (open || !cfg.GameOnly)) RestoreFromGameHidden();
                if (gameHidden) return;
                if (LiveShown && !open && !animating && !inSettings)
                {
                    // frameless live view: follow fullscreen games (bottom edge vs. above the taskbar) and stay on top
                    bool lfs = IsFullscreenForeground();
                    if (lfs != fsFore) { fsFore = lfs; dockRect = ComputeDockRect(); PositionLive(); }
                    RaiseLive();
                    return;
                }
                if (open || animating || !bezelShown || dockDown || inSettings) return;
                bool fs = IsFullscreenForeground();
                if (fs != fsFore) { fsFore = fs; RelayoutDock(); }
                RaiseBezel();
            };
            dockTimer.Start();

            ApplyMode();
            if (cfg.ShowDock) { ShowBezel(); SetFrame(dockRect, PeekFrac, false, true); }
            ScheduleWarmup();   // prepare the open animation in the background

            BeginInvoke((MethodInvoker)delegate { InitWeb(); });
        }

        async void InitWeb()
        {
            try
            {
                string dataDir = Environment.GetEnvironmentVariable("TACOVERLAY_DATA");   // (tests keep their browser data elsewhere)
                if (string.IsNullOrEmpty(dataDir)) dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TacOverlay");
                string ud = Path.Combine(dataDir, "WebView2");
                CoreWebView2EnvironmentOptions opts = new CoreWebView2EnvironmentOptions();
                try { opts.EnableTrackingPrevention = true; } catch { }
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, ud, opts);
                ctl = await env.CreateCoreWebView2ControllerAsync(webHost.Handle);
                ctl.DefaultBackgroundColor = Color.Black;
                ctl.IsVisible = false;
                CoreWebView2 wv = ctl.CoreWebView2;
                try { browserPid = (uint)wv.BrowserProcessId; } catch { }
                SetupBrowser(env, wv);   // security rules, ad blocking, address bar (Browser.cs)
                NavigateConfigured();
                if (cfg.ShowOnStart || showOnFirstRun) OpenTablet();
                else if (cfg.LiveMini) ShowLive();   // started minimized with the live view switched on
                tray.ShowBalloonTip(4000, "Tac Map Overlay",
                    Program.SafeModeNote.Length > 0 ? Program.SafeModeNote :
                    "Press " + cfg.ToggleKey + " to expand the tablet (it takes your mouse) and again to minimize it", ToolTipIcon.Info);
                if (update != null)
                {
                    tray.BalloonTipClicked += delegate { RunUpdate(); };
                    tray.ShowBalloonTip(10000, "Update available - v" + (update.Latest ?? update.Minimum),
                        "A newer version of Tac Map Overlay is out. Click here to update." + (update.Notes.Length > 0 ? "\r\n" + update.Notes : ""), ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not start the embedded browser (WebView2):\r\n\r\n" + ex.Message,
                    "Tac Map Overlay", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Quit();
            }
        }

        string NormalizeUrl(string u)
        {
            u = (u ?? "").Trim();
            if (u.Length == 0) return "";
            if (u.IndexOf("://", StringComparison.Ordinal) < 0 && !u.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            {
                return "https://" + u;
            }
            return u;
        }

        void NavigateConfigured()
        {
            string url = NormalizeUrl(cfg.Url);
            if (url.Length == 0)
            {
                internalNav = true;
                ctl.CoreWebView2.NavigateToString("<html><body style='background:#111;color:#9c6;font-family:Consolas,monospace;" +
                    "display:flex;align-items:center;justify-content:center;height:100vh;margin:0;text-align:center'>" +
                    "<div>NO MAP URL SET<br><br><small>Click the gear &rarr; Map URL</small></div></body></html>");
            }
            else ctl.CoreWebView2.Navigate(url);
        }

        // ---------- open / close (dock <-> full tablet) ----------
        void ToggleOverlay()
        {
            bool target = animating ? animDir > 0 : open;
            if (target) CloseTablet(); else OpenTablet();
        }

        void OpenTablet()
        {
            if (animating && animDir > 0) return;
            if (open && !animating) return;

            if (!(animating && animDir < 0))
            {
                fsFore = IsFullscreenForeground();   // while the game is still in front: where will the dock go when we minimize?
                IntPtr fg = Native.GetForegroundWindow();
                if (fg != Handle && fg != IntPtr.Zero && (webHost == null || fg != webHost.Handle)) prevFg = fg;
            }
            dockRect = ComputeDockRect();
            openRect = ComputeOpenRect();
            if (gameHidden) RestoreFromGameHidden();   // you asked for it: show it even if the game isn't in front
            bool wasLive = LiveShown;
            if (wasLive)
            {
                // coming out of the frameless live view: draw the frame over it first (same size and place), then expand from there
                LiveShown = false; liveDrag = false;
                frame = dockRect; SetFrame(dockRect, PeekFrac, false, true);
            }
            else if (!bezelShown) { ShowBezel(); frame = dockRect; }
            HideWeb();
            open = true;
            ApplyMode();
            if (wasLive) NotifyPageMode();

            if (cfg.Animate && cfg.AnimMs > 0) StartAnim(1);
            else
            {
                animating = false;
                FinishOpen();
            }
        }

        void CloseTablet()
        {
            if (!open && !(animating && animDir > 0)) return;
            if (!animating) openRect = frame;       // remember where the user left it
            dockRect = ComputeDockRect();
            ReleaseInput();                         // mouse and keyboard go back to the game
            HideWeb();
            SaveState();
            open = false;
            ApplyMode();

            if (cfg.Animate && cfg.AnimMs > 0) StartAnim(-1);
            else
            {
                animating = false;
                FinishClose();
            }
        }

        // Minimizing: free the mouse and hand the keyboard/mouse back to the game.
        void ReleaseInput()
        {
            if (!grabbed) return;
            grabbed = false;
            awayTicks = 0;
            if (clipTimer != null && clipTimer.Enabled) clipTimer.Stop();
            Native.ClipCursor(IntPtr.Zero);
            // Give focus back to the game only if the tablet itself still has it. If you already alt-tabbed somewhere
            // else, leave focus exactly where you put it.
            IntPtr fg = Native.GetForegroundWindow();
            uint pid; Native.GetWindowPid(fg, out pid);
            if (fg == IntPtr.Zero || pid == myPid || (browserPid != 0 && pid == browserPid)) Native.ForceForeground(prevFg);
        }

        // Keep the mouse inside the tablet while it is open (if enabled).
        void UpdateClip()
        {
            if (!grabbed) return;
            // a settings / prompt / update window is open: the mouse must stay completely free, whatever just changed
            if (inSettings) { Native.ClipCursor(IntPtr.Zero); return; }
            if (cfg.LockMouse)
            {
                Native.RECT r = new Native.RECT();
                r.Left = frame.Left; r.Top = frame.Top; r.Right = frame.Right; r.Bottom = frame.Bottom;
                Native.ClipCursorRect(ref r);
            }
            else Native.ClipCursor(IntPtr.Zero);
        }

        static double EaseOut(double c) { return 1.0 - Math.Pow(1.0 - c, 3.0); }

        // Anything the animation needs that isn't ready yet is made BEFORE it starts (a brief pause), never in the middle of it.
        void EnsureAnimReady()
        {
            if (mips == null || mipVer != artVersion) BuildMips();
            int w = openRect.Width, h = HeightAt(w, 1.0);
            if (FindArt(w, h) == null) using (Bitmap warm = RenderTablet(w, 1.0, false, true, false)) { }
        }

        void StartAnim(int dir)
        {
            if (!animating) EnsureAnimReady();
            if (animating)
            {
                if (dir == animDir) return;
                animDir = dir;
                // continue from the current visual position, no jump
                animC = dir > 0 ? 1.0 - Math.Pow(1.0 - animV, 1.0 / 3.0) : 1.0 - Math.Pow(animV, 1.0 / 3.0);
            }
            else
            {
                animating = true;
                animDir = dir;
                animV = dir > 0 ? 0.0 : 1.0;
                animC = 0.0;
                Native.timeBeginPeriod(1);
                animTimer.Start();
            }
            animSw.Reset(); animSw.Start(); animLast = 0;
        }

        void StopAnim()
        {
            animTimer.Stop();
            Native.timeEndPeriod(1);
        }

        public readonly List<double> AnimStamps = new List<double>();   // test only: when each animation frame was drawn

        void AnimTick(object sender, EventArgs e)
        {
            if (Program.TestMode) AnimStamps.Add(Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
            long now = animSw.ElapsedMilliseconds;
            double dt = (now - animLast) / (double)Math.Max(1, cfg.AnimMs);
            animLast = now;
            animC = Math.Min(1.0, animC + dt);
            double eased = EaseOut(animC);
            animV = animDir > 0 ? eased : 1.0 - eased;
            if (animC >= 1.0)
            {
                StopAnim();
                animating = false;
                if (animDir > 0) FinishOpen(); else FinishClose();
                return;
            }
            Rectangle r; double vis;
            GeomAt(animV, out r, out vis);
            SetFrame(r, vis, false, false);
        }

        public double LastFinishMs;   // test only

        void FinishOpen()
        {
            long t0 = Stopwatch.GetTimestamp();
            animating = false;
            animV = 1.0;
            SetFrame(openRect, 1.0, false, true);   // frame at full size, screen still dark
            ShowWeb();                               // page appears behind the screen
            SetFrame(openRect, 1.0, true, true);    // now punch the screen out so it shows through
            ApplyMode();
            GrabInput();                             // expanded = the tablet owns the mouse and keyboard
            if (focusNotesOnOpen && notes != null) { focusNotesOnOpen = false; if (!Program.TestMode) notes.FocusBox(); }
            LastFinishMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        }

        void FinishClose()
        {
            animating = false;
            animV = 0.0;
            ScheduleTrim();   // minimized and idle: tidy up memory a moment from now
            if (cfg.LiveMini && ctl != null)
            {
                ShowLive();      // minimized: no frame, just the live screen in the corner
                return;
            }
            if (cfg.ShowDock)
            {
                SetFrame(dockRect, PeekFrac, false, true);
            }
            else
            {
                Native.ShowWindow(Handle, Native.SW_HIDE);
                bezelShown = false;
            }
            ApplyMode();
        }

        // Expanded: the tablet takes the keyboard and the mouse (and, if enabled, keeps the mouse inside it).
        void GrabInput()
        {
            if (Program.TestMode) return;
            IntPtr fg = Native.GetForegroundWindow();
            if (fg != Handle && fg != IntPtr.Zero && (webHost == null || fg != webHost.Handle)) prevFg = fg;
            grabbed = true;
            awayTicks = 0;
            grabUntil = Environment.TickCount + 1200;
            Native.ClipCursor(IntPtr.Zero); // release any cursor lock the game set
            Native.ForceForeground(webHost.Handle);
            if (ctl != null) ctl.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
            UpdateClip();
            if (clipTimer != null && !clipTimer.Enabled) clipTimer.Start();
        }

        // The frame is always clickable (drag, gear, click-to-open). The page window can take focus only while expanded.
        void ApplyMode()
        {
            int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
            ex |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            if (LiveShown && !open && !liveDrag) ex |= Native.WS_EX_TRANSPARENT;   // live view: the mouse goes through to the game
            else ex &= ~Native.WS_EX_TRANSPARENT;
            Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex);

            if (webHost != null)
            {
                int wx = Native.GetWindowLong(webHost.Handle, Native.GWL_EXSTYLE);
                wx |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW;
                if (open) wx &= ~(Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE);
                else wx |= Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE;
                Native.SetWindowLong(webHost.Handle, Native.GWL_EXSTYLE, wx);
                int alphaPct = (!open && LiveShown) ? cfg.DockOpacity : cfg.Opacity;   // the live view uses the minimized transparency
                Native.SetLayeredWindowAttributes(webHost.Handle, 0, (byte)Math.Max(1, Math.Min(255, alphaPct * 255 / 100)), Native.LWA_ALPHA);
            }
        }

        // ---------- settings that the gear window and hotkeys change ----------
        void MoveToOpenRect()
        {
            if (open && !animating)
            {
                openRect = ComputeOpenRect();
                SetFrame(openRect, 1.0, webShown, true);
                PositionWeb();
                UpdateClip();
            }
            else if (!animating)
            {
                dockRect = ComputeDockRect();
                if (bezelShown) SetFrame(dockRect, PeekFrac, false, true);
                if (LiveShown) PositionLive();
            }
            SaveState();
            ScheduleWarmup();
        }

        void CycleSize()
        {
            int next = SizePresets[0];
            foreach (int p in SizePresets) if (p > cfg.OpenSize + 2) { next = p; break; }
            cfg.OpenSize = next;
            MoveToOpenRect();
        }

        void CycleOpacity()
        {
            int next = OpacityLevels[0];
            foreach (int lv in OpacityLevels) if (lv < cfg.Opacity) { next = lv; break; }
            SetOpacity(next);
        }

        public void SetOpacity(int pct)
        {
            cfg.Opacity = Math.Max(25, Math.Min(100, pct));
            ApplyMode();
            RenderState(true);
            SaveState();
        }

        public void SetOpenSize(int pct)
        {
            cfg.OpenSize = Math.Max(25, Math.Min(90, pct));
            MoveToOpenRect();
        }

        public void SetAnchor(string a)
        {
            cfg.Anchor = a;
            if (a == "custom" && cfg.CX == int.MinValue)
            {
                Rectangle b = open ? frame : ComputeOpenRect();
                cfg.CX = b.Left + b.Width / 2; cfg.CY = b.Top + b.Height / 2;
            }
            MoveToOpenRect();
        }

        public void SetMonitor(string deviceName)
        {
            cfg.Monitor = deviceName;
            if (cfg.Anchor == "custom") cfg.Anchor = "center";
            MoveToOpenRect();
        }

        public void SetTheme(string name)
        {
            cfg.SetThemeFor(DeviceArt.Cur.Id, Themes.Normalize(name));
            RebuildSource();
            RenderState(true);
            SaveState();
        }

        public string CurrentTheme { get { return CurTheme; } }
        public string CurrentLayoutId { get { return DeviceArt.Cur.Id; } }

        // Switch between the ATAK tablet and the pilot display. Reloads the picture (a second or two).
        public void SetLayout(string id)
        {
            id = Layouts.Get(id).Id;
            if (id == cfg.Layout || animating) return;
            Cursor.Current = Cursors.WaitCursor;
            cfg.Layout = id;
            LoadArt();
            Cursor.Current = Cursors.Default;
            gearHover = false; globeHover = false;
            if (open)
            {
                openRect = ComputeOpenRect();
                SetFrame(openRect, 1.0, webShown, true);
                if (webShown) PositionWeb();
                UpdateClip();
            }
            else
            {
                dockRect = ComputeDockRect();
                if (bezelShown) SetFrame(dockRect, PeekFrac, false, true);
            }
            ApplyLayoutToPage();
            SaveState();
        }

        // Custom wrap: the person's own picture painted over the tablet body.
        public bool HasWrap { get { return Themes.HasWrap(); } }

        public bool SetCustomWrap(string path, out string error)
        {
            if (!Themes.SaveWrap(path, out error)) return false;
            cfg.SetThemeFor(DeviceArt.Cur.Id, Themes.CustomName);
            RebuildSource();
            RenderState(true);
            SaveState();
            return true;
        }

        public void SetShowDock(bool on)
        {
            cfg.ShowDock = on;
            if (!open && !animating && !cfg.LiveMini)
            {
                if (on) { dockRect = ComputeDockRect(); ShowBezel(); frame = dockRect; SetFrame(dockRect, PeekFrac, false, true); }
                else { Native.ShowWindow(Handle, Native.SW_HIDE); bezelShown = false; }
            }
            SaveState();
        }

        public void ResetPosition()
        {
            cfg.Anchor = "center";
            cfg.CX = int.MinValue; cfg.CY = int.MinValue;
            MoveToOpenRect();
        }

        public void RefreshPage() { if (ctl != null) ctl.CoreWebView2.Reload(); }

        void SaveState()
        {
            if (noSave || string.IsNullOrEmpty(cfgPath)) return;
            try { cfg.Save(cfgPath); } catch (Exception ex) { AppLog.Write("saving settings failed: " + ex.Message); }
        }

        // ---------- hotkeys ----------
        static bool ParseHotkey(string s, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrEmpty(s)) return false;
            foreach (string part in s.Split('+'))
            {
                string p = part.Trim().ToLowerInvariant();
                if (p == "ctrl" || p == "control") mods |= 2;
                else if (p == "alt") mods |= 1;
                else if (p == "shift") mods |= 4;
                else if (p == "win") mods |= 8;
                else if (p == "`" || p == "backtick" || p == "grave" || p == "tilde") vk = (uint)Keys.Oemtilde;
                else
                {
                    try { vk = (uint)(Keys)Enum.Parse(typeof(Keys), part.Trim(), true); }
                    catch { return false; }
                }
            }
            return vk != 0;
        }

        void RegisterKeys()
        {
            for (int i = 1; i <= HK_LAST; i++) Native.UnregisterHotKey(Handle, i);
            List<string> failed = new List<string>();
            RegOne(HK_TOGGLE, cfg.ToggleKey, failed);
            RegOne(HK_SIZE, cfg.SizeKey, failed);
            RegOne(HK_OPACITY, cfg.OpacityKey, failed);
            RegOne(HK_PEEK, cfg.PeekKey, failed, true);   // hold-to-peek WANTS the key's auto-repeat (that is how "still held" is known)
            RegOne(HK_NOTES, cfg.NotesKey, failed);
            RegOne(HK_LIVE, cfg.LiveKey, failed);
            RegOne(HK_DRAG, cfg.DragKey, failed);
            RegOne(HK_T1, cfg.Timer1Key, failed);
            RegOne(HK_T2, cfg.Timer2Key, failed);
            RegOne(HK_STW, cfg.StopwatchKey, failed);
            if (failed.Count > 0)
                MessageBox.Show("These hotkeys could not be registered (already used by another program, or invalid):\r\n\r\n" +
                    string.Join("\r\n", failed.ToArray()) + "\r\n\r\nPick different ones in the settings (gear icon).",
                    "Tac Map Overlay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void RegOne(int id, string key, List<string> failed) { RegOne(id, key, failed, false); }

        void RegOne(int id, string key, List<string> failed, bool allowRepeat)
        {
            if (string.IsNullOrEmpty(key)) return;   // an empty key box = that shortcut is switched off
            uint mods, vk;
            if (!ParseHotkey(key, out mods, out vk) || !Native.RegisterHotKey(Handle, id, allowRepeat ? mods : (mods | 0x4000), vk))
                failed.Add(key);
        }

        // ---------- settings window ----------
        void OpenSettings()
        {
            if (inSettings || animating) return;
            inSettings = true;
            for (int i = 1; i <= HK_LAST; i++) Native.UnregisterHotKey(Handle, i); // so the key boxes can capture them
            if (grabbed) Native.ClipCursor(IntPtr.Zero);                      // free the mouse so the dialog is reachable
            bool quit = false, moveLive = false;
            using (SettingsForm dlg = new SettingsForm(this, cfg))
            {
                dlg.ShowDialog();
                quit = dlg.QuitRequested;
                moveLive = dlg.MoveLiveRequested;
            }
            inSettings = false;
            SaveState();
            if (quit) { Quit(); return; }
            if (moveLive) BeginInvoke((MethodInvoker)delegate { MoveLiveByHand(); });
            RegisterKeys();
            BuildMenu();
            RenderState(true);
            if (open && grabbed) { if (!Program.TestMode) Native.ForceForeground(webHost.Handle); UpdateClip(); }
        }

        // ---------- tray / context menu ----------
        void BuildMenu()
        {
            menu.Items.Clear();
            if (update != null)
            {
                ToolStripItem up = menu.Items.Add("Update available: v" + (update.Latest ?? update.Minimum) + "  -  update now", null, delegate { RunUpdate(); });
                up.ForeColor = Color.FromArgb(0, 130, 40);
                menu.Items.Add(new ToolStripSeparator());
            }
            menu.Items.Add("Expand / minimize  (" + cfg.ToggleKey + ")", null, delegate { ToggleOverlay(); });
            ToolStripMenuItem liveItem = new ToolStripMenuItem("Live screen in the corner when minimized");
            liveItem.Checked = cfg.LiveMini;
            liveItem.Click += delegate { SetLiveMini(!cfg.LiveMini); };
            menu.Items.Add(liveItem);
            menu.Items.Add("Settings...", null, delegate { OpenSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Bigger / smaller  (" + cfg.SizeKey + ")", null, delegate { CycleSize(); });
            menu.Items.Add("Transparency  (" + cfg.OpacityKey + ")", null, delegate { CycleOpacity(); });
            menu.Items.Add("Refresh page", null, delegate { RefreshPage(); });
            menu.Items.Add("Map URL...", null, delegate { ChangeUrl(); });
            menu.Items.Add("Reset position", null, delegate { ResetPosition(); });
            menu.Items.Add("Restart with default settings (safe mode)...", null, delegate { RestartInSafeMode(null); });
            miAutostart = new ToolStripMenuItem("Start with Windows");
            miAutostart.Checked = IsAutostart();
            miAutostart.Click += delegate { SetAutostart(!IsAutostart()); miAutostart.Checked = IsAutostart(); };
            menu.Items.Add(miAutostart);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quit", null, delegate { Quit(); });
        }

        void BuildTray()
        {
            menu = new ContextMenuStrip();
            BuildMenu();
            ContextMenuStrip = menu;

            tray = new NotifyIcon();
            tray.Icon = MakeIcon();
            tray.Text = "Tac Map Overlay";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.MouseClick += delegate (object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ToggleOverlay(); };
        }

        static Icon MakeIcon()
        {
            try
            {
                using (Stream s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("TacOverlay.emblem.png"))
                using (Image img = Image.FromStream(s))
                using (Bitmap bmp = new Bitmap(32, 32))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(img, 0, 0, 32, 32);
                    return Icon.FromHandle(bmp.GetHicon());
                }
            }
            catch { }
            using (Bitmap bmp = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(24, 27, 22));
                using (Pen p = new Pen(Color.FromArgb(150, 205, 90), 2.5f))
                {
                    g.DrawEllipse(p, 6, 6, 20, 20);
                    g.DrawLine(p, 16, 1, 16, 31);
                    g.DrawLine(p, 1, 16, 31, 16);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static bool IsAutostart()
        {
            using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue("TacMapOverlay") != null;
        }
        public static void SetAutostart(bool on)
        {
            using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (k == null) return;
                if (on) k.SetValue("TacMapOverlay", "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue("TacMapOverlay", false);
            }
        }

        // Windows opened while the tablet is expanded (prompts, the update window) must not be trapped by the mouse lock.
        void FreeMouseBegin(out bool was)
        {
            was = inSettings;
            inSettings = true;
            if (grabbed) Native.ClipCursor(IntPtr.Zero);
        }

        void FreeMouseEnd(bool was)
        {
            inSettings = was;
            if (!was && open && grabbed && !Program.TestMode) { Native.ForceForeground(webHost.Handle); UpdateClip(); }
        }

        public void ChangeUrl()
        {
            bool was; FreeMouseBegin(out was);
            string u;
            try { u = PromptUrl(cfg.Url, CurrentScreen().Bounds); }
            finally { FreeMouseEnd(was); }
            if (u == null) return;
            cfg.Url = u;
            SaveState();
            if (ctl != null) { HomeChanged(); NavigateConfigured(); }
        }

        public static string PromptUrl(string current, Rectangle area)
        {
            using (Form f = new Form())
            {
                f.Text = "Tac Map Overlay - map URL";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.Manual;
                f.MinimizeBox = false; f.MaximizeBox = false; f.TopMost = true; f.ShowInTaskbar = false;
                f.ClientSize = new Size(540, 104);
                f.Location = new Point(area.Left + (area.Width - f.Width) / 2, area.Top + (area.Height - f.Height) / 2);
                Label l = new Label(); l.Text = "Paste the web address of your tac map:";
                l.SetBounds(12, 12, 510, 18);
                TextBox tb = new TextBox(); tb.Text = current ?? ""; tb.SetBounds(12, 36, 516, 24);
                Button ok = new Button(); ok.Text = "OK"; ok.DialogResult = DialogResult.OK; ok.SetBounds(372, 68, 75, 26);
                Button cancel = new Button(); cancel.Text = "Cancel"; cancel.DialogResult = DialogResult.Cancel; cancel.SetBounds(453, 68, 75, 26);
                f.Controls.AddRange(new Control[] { l, tb, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog() == DialogResult.OK ? tb.Text.Trim() : null;
            }
        }

        // ---------- updates ----------
        Updater.Info update;            // a newer (but not required) version exists

        public void SetUpdate(Updater.Info i) { update = i; }

        // The "Update available" menu item / tray balloon / settings button: one-click update when possible,
        // otherwise open the download page.
        public void RunUpdate()
        {
            if (update == null) return;
            if (!Updater.CanAutoUpdate(update)) { Updater.OpenDownload(update); return; }
            bool was; FreeMouseBegin(out was);
            bool restarting;
            try { restarting = Updater.ShowUpdateDialog(update, false, CurrentScreen().Bounds, cfg.UpdateUrl.Length > 0); }
            finally { FreeMouseEnd(was); }
            if (restarting)
            {
                // the new version is already starting and waits for this one to close
                Quit();
                Environment.Exit(0);
            }
        }

        // Settings > "Check for updates"
        public void CheckForUpdatesNow()
        {
            string url = cfg.UpdateUrl.Length > 0 ? cfg.UpdateUrl : Updater.DefaultInfoUrl;
            if (url.Length == 0)
            {
                MessageBox.Show("Update checking isn't set up in this build yet.", "Tac Map Overlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Cursor.Current = Cursors.WaitCursor;
            Updater.Info i = Updater.Check(url, 5000);
            Cursor.Current = Cursors.Default;
            if (!i.Ok)
                MessageBox.Show("Couldn't reach the update server. Check your internet connection and try again.", "Tac Map Overlay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else if (Updater.IsRequired(i) || Updater.IsNewer(i))
            {
                update = i;
                BuildMenu();
                if (Updater.CanAutoUpdate(i)) RunUpdate();
                else if (MessageBox.Show("Version " + (i.Latest ?? i.Minimum) + " is available (you have " + Updater.Version + ")." +
                    (i.Notes.Length > 0 ? "\r\n\r\n" + i.Notes : "") + "\r\n\r\nOpen the download page?", "Tac Map Overlay - update",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes) Updater.OpenDownload(i);
            }
            else
                MessageBox.Show("You're up to date (version " + Updater.Version + ").", "Tac Map Overlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public void SetLockMouse(bool on)
        {
            cfg.LockMouse = on;
            if (grabbed) UpdateClip();
            RenderState(true);
            SaveState();
        }

        void Quit()
        {
            SaveState();
            if (clipTimer != null && clipTimer.Enabled) clipTimer.Stop();
            Native.ClipCursor(IntPtr.Zero);
            if (animating) StopAnim();
            if (animTimer != null) animTimer.Stop();
            if (dockTimer != null) dockTimer.Stop();
            if (infoTimer != null) infoTimer.Stop();
            if (notes != null) notes.Save();
            for (int i = 1; i <= HK_LAST; i++) Native.UnregisterHotKey(Handle, i);
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            Application.Exit();
        }

        // ---------- window behaviour ----------
        void SyncFrameFromWindow()
        {
            Native.RECT r;
            if (!Native.GetWindowRect(Handle, out r)) return;
            Rectangle nf = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            if (nf == frame) return;
            bool sizeChanged = nf.Size != frame.Size;
            frame = nf;
            if (open && !animating && bezelShown)
            {
                if (sizeChanged) RenderState(!inSizeMove);
                if (webShown) PositionWeb();
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 /*WM_HOTKEY*/)
            {
                switch (m.WParam.ToInt32())
                {
                    case HK_TOGGLE: ToggleOverlay(); break;
                    case HK_SIZE: CycleSize(); break;
                    case HK_OPACITY: CycleOpacity(); break;
                    case HK_PEEK: PeekPressed(); break;
                    case HK_NOTES: NotesHotkey(); break;
                    case HK_LIVE: SetLiveMini(!cfg.LiveMini); HotkeyNote("Live corner view " + (cfg.LiveMini ? "ON - minimize to see it" : "OFF")); break;
                    case HK_DRAG: ToggleLiveDrag(); break;
                    case HK_T1: TimerHotkey(0); break;
                    case HK_T2: TimerHotkey(1); break;
                    case HK_STW: PressStopwatch(); lastSig = ""; RenderState(true); break;
                }
                return;
            }
            if (m.Msg == 0x0084 /*WM_NCHITTEST*/)
            {
                long lp = m.LParam.ToInt64();
                Point pt = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                bool corner = open && !animating && pt.X >= Width - 30 && pt.Y >= Height - 30;
                m.Result = corner ? (IntPtr)17 /*HTBOTTOMRIGHT*/ : (IntPtr)1 /*HTCLIENT*/;
                return;
            }
            if (m.Msg == 0x0214 /*WM_SIZING*/)
            {
                // keep the tablet's proportions while resizing from the corner
                Native.RECT r = (Native.RECT)Marshal.PtrToStructure(m.LParam, typeof(Native.RECT));
                int w = Math.Max(300, r.Right - r.Left);
                r.Right = r.Left + w;
                r.Bottom = r.Top + HeightFor(w);
                Marshal.StructureToPtr(r, m.LParam, false);
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
            if (m.Msg == 0x0047 /*WM_WINDOWPOSCHANGED*/ && !inPush) SyncFrameFromWindow();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || animating) return;
            if (OverGear(e.Location)) { BeginInvoke((MethodInvoker)delegate { OpenSettings(); }); return; }
            if (open && OverGlobe(e.Location)) { BeginInvoke((MethodInvoker)delegate { ToggleBar(); }); return; }
            if (open)
            {
                string hit = HitExtra(e.Location);      // soft keys, rockers, timer / notes buttons
                if (hit != null) { BeginInvoke((MethodInvoker)delegate { PressExtra(hit); }); return; }
                if (OverInfoPanel(e.Location)) return;   // the clock panel: not a drag handle
            }
            if (!open)
            {
                // minimized tablet: a click expands it, a drag moves it (decided on mouse-up / after a few pixels of movement)
                dockDown = true; dockDragging = false;
                downScreen = Cursor.Position; downFrame = frame;
                Capture = true;
                return;
            }
            // drag the tablet by its frame
            Native.ReleaseCapture();
            Native.SendMessage(Handle, 0x00A1 /*WM_NCLBUTTONDOWN*/, (IntPtr)2 /*HTCAPTION*/, IntPtr.Zero);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !dockDown) return;
            dockDown = false;
            Capture = false;
            if (dockDragging)
            {
                dockDragging = false;
                Cursor = Cursors.Hand;
                EndDockDrag();
            }
            else if (LiveShown) BeginInvoke((MethodInvoker)delegate { SetLiveDrag(false); });   // a click in move mode just ends it
            else BeginInvoke((MethodInvoker)delegate { OpenTablet(); });   // plain click: expand
        }

        // After dragging the minimized tablet: remember where it is. Close to the bottom edge it snaps flush to it.
        void EndDockDrag()
        {
            if (LiveShown) { EndLiveDrag(); return; }
            Screen sc = Screen.FromRectangle(frame);
            Rectangle b = sc.Bounds;
            if (sc.DeviceName != cfg.Monitor && !(string.IsNullOrEmpty(cfg.Monitor) && sc.Primary)) cfg.Monitor = sc.DeviceName;
            cfg.DockX = frame.X - b.Left;
            cfg.DockY = (frame.Bottom >= FloorBottom() - 40) ? int.MinValue : frame.Y - b.Top;
            RelayoutDock();
        }

        void RelayoutDock()
        {
            if (open || animating) { SaveState(); return; }
            dockRect = ComputeDockRect();
            if (bezelShown) SetFrame(dockRect, PeekFrac, false, true);
            if (LiveShown) PositionLive();
            SaveState();
        }

        public void SetDockWidth(int w) { cfg.DockWidth = Math.Max(200, Math.Min(700, w)); RelayoutDock(); }
        public void SetDockPeek(int p) { cfg.DockPeek = Math.Max(20, Math.Min(70, p)); RelayoutDock(); }
        public void SetDockOpacity(int p) { cfg.DockOpacity = Math.Max(25, Math.Min(100, p)); RelayoutDock(); }

        // Lift the minimized tablet this many pixels above the bottom (0 = resting on the bottom edge).
        public void SetDockRaise(int px)
        {
            Rectangle b = CurrentScreen().Bounds;
            int h = HeightAt(cfg.DockWidth, PeekFrac);
            if (px <= 0) cfg.DockY = int.MinValue;
            else cfg.DockY = (FloorBottom() - h - px) - b.Top;
            if (cfg.DockX == int.MinValue) cfg.DockX = b.Width - cfg.DockWidth - 16;
            RelayoutDock();
        }

        // current "raise" in pixels, for the settings slider
        public int GetDockRaise()
        {
            if (cfg.DockY == int.MinValue) return 0;
            Rectangle b = CurrentScreen().Bounds;
            int h = HeightAt(cfg.DockWidth, PeekFrac);
            return Math.Max(0, FloorBottom() - (b.Top + cfg.DockY + h));
        }

        // where = "left" | "center" | "right": park the minimized tablet along the bottom edge
        public void SetDockPreset(string where)
        {
            Rectangle b = CurrentScreen().Bounds;
            cfg.DockY = int.MinValue;
            if (where == "left") cfg.DockX = 16;
            else if (where == "center") cfg.DockX = (b.Width - cfg.DockWidth) / 2;
            else cfg.DockX = int.MinValue;
            RelayoutDock();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dockDown)
            {
                Point cp = Cursor.Position;
                int dx = cp.X - downScreen.X, dy = cp.Y - downScreen.Y;
                if (!dockDragging && (Math.Abs(dx) > 4 || Math.Abs(dy) > 4)) { dockDragging = true; Cursor = Cursors.SizeAll; }
                if (dockDragging)
                {
                    Rectangle b = Screen.FromPoint(cp).Bounds;
                    int nx = Math.Max(b.Left, Math.Min(downFrame.X + dx, b.Right - frame.Width));
                    int ny = Math.Max(b.Top, Math.Min(downFrame.Y + dy, b.Bottom - frame.Height));
                    Native.SetWindowPos(Handle, IntPtr.Zero, nx, ny, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                    if (LiveShown && webHost != null) Native.SetWindowPos(webHost.Handle, IntPtr.Zero, nx, ny, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                }
                return;
            }
            bool over = !animating && OverGear(e.Location);
            bool overG = !animating && open && OverGlobe(e.Location);
            string hk = (!animating && open) ? HitExtra(e.Location) : null;
            if (over != gearHover || overG != globeHover || hk != hoverKey)
            {
                gearHover = over; globeHover = overG; hoverKey = hk;
                Cursor = (over || overG || hk != null) ? Cursors.Hand : (open ? Cursors.Default : Cursors.Hand);
                RenderState(true);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (gearHover || globeHover || hoverKey != null) { gearHover = false; globeHover = false; hoverKey = null; Cursor = Cursors.Default; RenderState(true); }
        }

        protected override void OnResizeBegin(EventArgs e)
        {
            base.OnResizeBegin(e);
            inSizeMove = true;
            dragStart = frame;
            if (grabbed) Native.ClipCursor(IntPtr.Zero);   // free the mouse while you drag / resize the tablet
        }

        // After you drag or resize it yourself, remember that (and which monitor it is on).
        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            inSizeMove = false;
            if (animating || !open) return;
            SyncFrameFromWindow();
            Rectangle b = frame;
            Screen sc = Screen.FromRectangle(b);
            if (b.Size != dragStart.Size)
            {
                Rectangle wa = sc.WorkingArea;
                cfg.OpenSize = Math.Max(25, Math.Min(90, (int)Math.Round(b.Width * 100.0 / wa.Width)));
            }
            else if (b.Location != dragStart.Location)
            {
                cfg.Anchor = "custom";
                cfg.CX = b.Left + b.Width / 2; cfg.CY = b.Top + b.Height / 2;
                cfg.Monitor = sc.DeviceName;
            }
            openRect = frame;
            RenderState(true);
            if (webShown) PositionWeb();
            UpdateClip();
            SaveState();
        }
    }

    // A small log file (crashes, errors) so a problem can be sent for diagnosis. Never throws.
    static class AppLog
    {
        static int count;
        public static string Dir
        {
            get
            {
                string d = Environment.GetEnvironmentVariable("TACOVERLAY_DATA");
                if (string.IsNullOrEmpty(d)) d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TacOverlay");
                return Path.Combine(d, "logs");
            }
        }

        public static void Write(string text)
        {
            try
            {
                if (++count > 300) return;   // a runaway error can't fill the disk
                Directory.CreateDirectory(Dir);
                string path = Path.Combine(Dir, "tacoverlay.log");
                try { if (File.Exists(path) && new FileInfo(path).Length > 600 * 1024) File.Move(path, path + ".old"); } catch { }
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  v" + Updater.Version + "  " + text + "\r\n");
            }
            catch { }
        }
    }

    static class Program
    {
        public static bool TestMode;   // --selftest: never take the keyboard / mouse focus from whatever the person is doing
        public static string SafeModeNote = "";   // shown once at start when safe mode was used

        [STAThread]
        static void Main(string[] args)
        {
            // developer helper: render every theme to PNG files, then exit
            if (args.Length >= 2 && args[0] == "--dump-themes")
            {
                Directory.CreateDirectory(args[1]);
                if (args.Length >= 3) DeviceArt.Use(Layouts.Get(args[2]));
                Bitmap orig = DeviceArt.LoadOriginal();
                bool[] outside = DeviceArt.BuildMask(orig);
                byte[] alpha = DeviceArt.BuildSoftAlpha(outside);
                foreach (string n in Themes.Names)
                    using (Bitmap t = Themes.Build(orig, outside, n))
                    using (Bitmap b = DeviceArt.MakeSource(t, alpha))
                        b.Save(Path.Combine(args[1], n.Replace(' ', '_') + ".png"));
                return;
            }

            // developer helper: draw the expanded tablet at a given width (and theme) to a PNG, then exit
            if (args.Length >= 4 && args[0] == "--dump-render")
            {
                Config rc = new Config();
                if (args.Length >= 5) rc.Layout = Layouts.Get(args[4]).Id;
                rc.SetThemeFor(rc.Layout, Themes.Normalize(args[2]));
                OverlayForm rf = new OverlayForm(rc, "");
                double dv = args.Length >= 6 ? double.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture) : 1.0;
                rf.DumpRender(args[1], int.Parse(args[3]), dv, args.Length >= 7 && args[6] == "cut");
                return;
            }

            // developer helper: really start the overlay and run scripted browser checks, writing results into a folder
            if (args.Length >= 2 && args[0] == "--selftest")
            {
                Directory.CreateDirectory(args[1]);
                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Program.TestMode = true;
                string testDir = args[1];
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate (object s, System.Threading.ThreadExceptionEventArgs e) { try { File.AppendAllText(Path.Combine(testDir, "crash.txt"), "THREAD: " + e.Exception + "\r\n"); } catch { } };
                AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e) { try { File.AppendAllText(Path.Combine(testDir, "crash.txt"), "FATAL: " + e.ExceptionObject + "\r\n"); } catch { } };
                Config tc = new Config();
                tc.LockMouse = false; tc.Animate = false;
                tc.ToggleKey = "Ctrl+Alt+Shift+F11"; tc.SizeKey = "Ctrl+Alt+Shift+F10"; tc.OpacityKey = "Ctrl+Alt+Shift+F9";   // don't clash with a running copy
                tc.PeekKey = tc.NotesKey = tc.LiveKey = tc.DragKey = tc.Timer1Key = tc.Timer2Key = tc.StopwatchKey = "";
                if (Screen.AllScreens.Length > 1) tc.Monitor = Screen.AllScreens[Screen.AllScreens.Length - 1].DeviceName;    // keep it off the main screen
                OverlayForm tf = new OverlayForm(tc, "");
                tf.Init(true);
                tf.RunSelfTest(args[1]);
                Application.Run();
                return;
            }

            // developer helper: run with a test PEEK hotkey and log when the tablet opens / closes (a script holds the key)
            if (args.Length >= 2 && args[0] == "--peektest")
            {
                Directory.CreateDirectory(args[1]);
                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Program.TestMode = true;
                Config pc = new Config();
                pc.LockMouse = false; pc.Animate = false; pc.AlertBeep = false;
                pc.ToggleKey = "Ctrl+Alt+Shift+F11"; pc.SizeKey = "Ctrl+Alt+Shift+F10"; pc.OpacityKey = "Ctrl+Alt+Shift+F9";
                pc.PeekKey = "Ctrl+Alt+Shift+F12";
                pc.NotesKey = pc.LiveKey = pc.DragKey = pc.Timer1Key = pc.Timer2Key = pc.StopwatchKey = "";
                if (Screen.AllScreens.Length > 1) pc.Monitor = Screen.AllScreens[Screen.AllScreens.Length - 1].DeviceName;
                OverlayForm pf = new OverlayForm(pc, "");
                pf.Init(false);
                pf.RunPeekTest(args[1]);
                Application.Run();
                return;
            }

            // developer helper: start with the live view switched on and report the pixel ratio the map site sees
            if (args.Length >= 2 && args[0] == "--livestarttest")
            {
                Directory.CreateDirectory(args[1]);
                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Program.TestMode = true;
                Config lc = new Config();
                lc.LiveMini = true; lc.LockMouse = false; lc.Animate = false; lc.AlertBeep = false;
                lc.ToggleKey = "Ctrl+Alt+Shift+F11"; lc.SizeKey = "Ctrl+Alt+Shift+F10"; lc.OpacityKey = "Ctrl+Alt+Shift+F9";
                lc.PeekKey = lc.NotesKey = lc.LiveKey = lc.DragKey = lc.Timer1Key = lc.Timer2Key = lc.StopwatchKey = "";
                if (Screen.AllScreens.Length > 1) lc.Monitor = Screen.AllScreens[Screen.AllScreens.Length - 1].DeviceName;
                OverlayForm lf = new OverlayForm(lc, "");
                lf.Init(false);
                lf.RunLiveStartTest(args[1]);
                Application.Run();
                return;
            }

            // developer helper: draw the button-assignment window and the whole settings page (tall) to PNGs
            if (args.Length >= 3 && args[0] == "--dump-keys")
            {
                Application.EnableVisualStyles();
                Config kc = new Config(); kc.Layout = "pilot";
                OverlayForm ko = new OverlayForm(kc, "");
                using (SoftKeyForm kf = new SoftKeyForm(kc, ko))
                {
                    kf.Show();
                    using (Bitmap kb = new Bitmap(kf.ClientSize.Width, kf.ClientSize.Height)) { kf.DrawToBitmap(kb, new Rectangle(0, 0, kb.Width, kb.Height)); kb.Save(args[1]); }
                }
                using (SettingsForm sf = new SettingsForm(ko, kc))
                {
                    sf.Show();
                    // the settings window scrolls: capture it at several scroll positions
                    for (int part = 1; part <= 3; part++)
                    {
                        sf.AutoScrollPosition = new Point(0, part * 900);
                        Application.DoEvents();
                        using (Bitmap sb = new Bitmap(sf.ClientSize.Width, sf.ClientSize.Height))
                        {
                            sf.DrawToBitmap(sb, new Rectangle(0, 0, sb.Width, sb.Height));
                            sb.Save(args[2].Replace(".png", "_" + part + ".png"));
                        }
                    }
                }
                return;
            }

            // developer helper: really open / close the tablet with animation on and report how smooth it was
            if (args.Length >= 2 && args[0] == "--animtest")
            {
                Directory.CreateDirectory(args[1]);
                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Program.TestMode = true;
                Config ac = new Config();
                ac.LockMouse = false; ac.Animate = true; ac.AnimMs = 300; ac.AlertBeep = false;
                ac.ToggleKey = "Ctrl+Alt+Shift+F11"; ac.SizeKey = "Ctrl+Alt+Shift+F10"; ac.OpacityKey = "Ctrl+Alt+Shift+F9";
                ac.PeekKey = ac.NotesKey = ac.LiveKey = ac.DragKey = ac.Timer1Key = ac.Timer2Key = ac.StopwatchKey = "";
                if (args.Length >= 3) ac.Layout = Layouts.Get(args[2]).Id;
                if (Screen.AllScreens.Length > 1) ac.Monitor = Screen.AllScreens[Screen.AllScreens.Length - 1].DeviceName;
                OverlayForm af = new OverlayForm(ac, "");
                af.Init(true);
                af.RunAnimTest(args[1]);
                Application.Run();
                return;
            }

            // developer helper: time animation frames for a layout
            if (args.Length >= 3 && args[0] == "--bench")
            {
                Native.SetProcessDPIAware();
                Config bc = new Config(); bc.Layout = Layouts.Get(args[2]).Id;
                OverlayForm bf = new OverlayForm(bc, "");
                File.WriteAllText(args[1], bc.Layout + ":\r\n" + bf.BenchFrames());
                return;
            }

            // developer helper: draw the address bar to a PNG, then exit
            if (args.Length >= 4 && args[0] == "--dump-bar")
            {
                Application.EnableVisualStyles();
                int bw = int.Parse(args[2]), bh = int.Parse(args[3]);
                using (Form hostForm = new Form())
                {
                    hostForm.ClientSize = new Size(bw, bh);
                    BrowserBar bb = new BrowserBar();
                    hostForm.Controls.Add(bb);
                    bb.SetBounds(0, 0, bw, bh);
                    bb.Relayout(bw, bh);
                    bb.Addr.Text = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";
                    bb.SetUrl(bb.Addr.Text);
                    bb.SetNav(true, false, false);
                    bb.SetAds(true, 37);
                    hostForm.Show();
                    using (Bitmap bm = new Bitmap(bw, bh))
                    {
                        bb.DrawToBitmap(bm, new Rectangle(0, 0, bw, bh));
                        bm.Save(args[1]);
                    }
                }
                return;
            }

            // developer helper: draw the settings window to a PNG, then exit
            if (args.Length >= 2 && args[0] == "--dump-settings")
            {
                Application.EnableVisualStyles();
                Config sc = new Config(); if (args.Length >= 3) sc.Theme = args[2]; if (args.Length >= 4) sc.Layout = Layouts.Get(args[3]).Id;
                OverlayForm so = new OverlayForm(sc, "");
                using (SettingsForm sf = new SettingsForm(so, sc))
                {
                    sf.Show();
                    using (Bitmap sb = new Bitmap(sf.ClientSize.Width, sf.ClientSize.Height))
                    {
                        sf.DrawToBitmap(sb, new Rectangle(0, 0, sb.Width, sb.Height));
                        sb.Save(args[1]);
                    }
                }
                return;
            }

            // developer helper: run the update check against a URL and write what it found to a file
            if (args.Length >= 3 && args[0] == "--check-update")
            {
                Updater.Info t = Updater.Check(args[1], 4000);
                File.WriteAllText(args[2], "ok=" + t.Ok + " latest=" + t.Latest + " minimum=" + t.Minimum + " url=" + t.Url +
                    " required=" + Updater.IsRequired(t) + " newer=" + Updater.IsNewer(t) + " current=" + Updater.Current);
                return;
            }

            // just updated: the old copy is closing - wait for it to go before taking over
            int au = Array.IndexOf(args, "--after-update");
            if (au >= 0 && au + 1 < args.Length)
            {
                int oldPid;
                if (int.TryParse(args[au + 1], out oldPid))
                    try { Process.GetProcessById(oldPid).WaitForExit(20000); } catch { }
            }

            bool created;
            string mutexName = Environment.GetEnvironmentVariable("TACOVERLAY_MUTEX");   // (tests only)
            using (Mutex mutex = new Mutex(true, string.IsNullOrEmpty(mutexName) ? "TacMapOverlay_SingleInstance" : mutexName, out created))
            {
                if (!created) return;
                Updater.CleanOld();
                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // never die on an unexpected error: write it to the log and carry on
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate (object s, System.Threading.ThreadExceptionEventArgs e) { AppLog.Write("ERROR " + e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e) { AppLog.Write("FATAL " + e.ExceptionObject); };

                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string cfgPath = Path.Combine(dir, "TacOverlay.ini");
                bool firstRun = !File.Exists(cfgPath);
                Config cfg = Config.Load(cfgPath);
                AppLog.Write("started (layout " + cfg.Layout + ")");

                // Safe mode: start with --safe (Settings > "Restart in safe mode", or the tray menu) = default settings.
                // Your old settings are kept in TacOverlay.ini.bak.
                if (!firstRun && Array.IndexOf(args, "--safe") >= 0)
                {
                    try
                    {
                        File.Copy(cfgPath, cfgPath + ".bak", true);
                        Config safe = new Config();
                        safe.Url = cfg.Url; safe.UpdateUrl = cfg.UpdateUrl;
                        cfg = safe;
                        SafeModeNote = "Safe mode: started with default settings. Your old settings are saved as TacOverlay.ini.bak.";
                        AppLog.Write("safe mode");
                    }
                    catch { }
                }

                // the monitor the tablet lives on (primary unless you picked another one): pop-ups appear there
                Rectangle area = Screen.PrimaryScreen.Bounds;
                foreach (Screen s in Screen.AllScreens) if (s.DeviceName == cfg.Monitor) area = s.Bounds;

                if (cfg.Url.Trim().Length == 0)
                {
                    string u = OverlayForm.PromptUrl("", area);
                    if (u != null) cfg.Url = u;
                }
                if (firstRun) { try { cfg.Save(cfgPath); } catch { } }

                // Update check (a few seconds at most; if it fails for any reason the app just starts normally).
                string upUrl = cfg.UpdateUrl.Length > 0 ? cfg.UpdateUrl : Updater.DefaultInfoUrl;
                Updater.Info upInfo = Updater.Check(upUrl, 4000);
                bool localTest = cfg.UpdateUrl.Length > 0;
                if (Updater.IsRequired(upInfo))
                {
                    // out of date: don't start until they update
                    if (Updater.CanAutoUpdate(upInfo)) Updater.ShowUpdateDialog(upInfo, true, area, localTest);   // true = the new version is starting
                    else Updater.ShowRequiredDialog(upInfo, area);
                    return;
                }
                if (Updater.IsNewer(upInfo) && Updater.CanAutoUpdate(upInfo))
                {
                    // a newer version exists: offer the one-click update right at startup ("Later" just carries on)
                    if (Updater.ShowUpdateDialog(upInfo, false, area, localTest)) return;
                }

                OverlayForm form = new OverlayForm(cfg, cfgPath);
                if (Updater.IsNewer(upInfo)) form.SetUpdate(upInfo);
                form.Init(firstRun);
                Application.Run();
            }
        }
    }
}
