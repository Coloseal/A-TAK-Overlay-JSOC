// Offline tools panel (bearing / distance between grids, 9-line template, checklist) and the actions that go with the
// pilot-display buttons: mission log, bookmarks, layout presets.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

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

    partial class OverlayForm
    {
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
