// Settings window opened from the gear icon: theme, display, keybinds, actions.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TacOverlay
{
    // A box that records the key combination you press.
    class HotkeyBox : TextBox
    {
        public event EventHandler PlainKey;   // raised after a key with no Ctrl/Alt/Shift was chosen (it works, but is taken from the game)

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right) Text = "";   // right-click clears a box (= that shortcut is off)
        }

        public HotkeyBox()
        {
            ContextMenuStrip = new ContextMenuStrip();   // an empty menu, so right-click only clears
            ReadOnly = true;
            ShortcutsEnabled = false;
            TextAlign = HorizontalAlignment.Center;
            BorderStyle = BorderStyle.FixedSingle;
            Cursor = Cursors.Hand;
        }

        static bool IsFunctionOrNav(Keys k)
        {
            return (k >= Keys.F1 && k <= Keys.F24) || k == Keys.Oemtilde || k == Keys.Insert || k == Keys.Delete || k == Keys.Home ||
                   k == Keys.End || k == Keys.PageUp || k == Keys.PageDown || k == Keys.Pause || k == Keys.Scroll;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys code = keyData & Keys.KeyCode;
            Keys mods = keyData & Keys.Modifiers;
            // a modifier on its own is not a hotkey: wait for the real key
            if (code == Keys.ControlKey || code == Keys.ShiftKey || code == Keys.Menu || code == Keys.LWin || code == Keys.RWin || code == Keys.None) return true;
            // bare Tab / Esc still move around and close the window; with Ctrl/Alt/Shift they can be bound
            if (mods == 0 && (code == Keys.Tab || code == Keys.Escape)) return base.ProcessCmdKey(ref msg, keyData);

            string s = "";
            if ((mods & Keys.Control) != 0) s += "Ctrl+";
            if ((mods & Keys.Alt) != 0) s += "Alt+";
            if ((mods & Keys.Shift) != 0) s += "Shift+";
            Text = s + (code == Keys.Oemtilde ? "`" : code.ToString());
            if (mods == 0 && !IsFunctionOrNav(code) && PlainKey != null) PlainKey(this, EventArgs.Empty);
            return true;
        }
    }

    // Clickable colour tile for picking a theme.
    class SwatchButton : Control
    {
        public string ThemeName;
        public bool Selected;
        Color[] colors;

        public SwatchButton(string name)
        {
            ThemeName = name;
            colors = Themes.Swatch(name);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(34, 38, 31));
            Rectangle chip = new Rectangle(6, 6, 22, Height - 12);
            if (colors.Length == 1)
                using (Brush b = new SolidBrush(colors[0])) g.FillRectangle(b, chip);
            else
            {
                int h2 = chip.Height / 2, w2 = chip.Width / 2;
                for (int i = 0; i < 4; i++)
                    using (Brush b = new SolidBrush(colors[i % colors.Length]))
                        g.FillRectangle(b, chip.X + (i % 2) * w2, chip.Y + (i / 2) * h2, w2 + 1, h2 + 1);
            }
            using (Pen p = new Pen(Color.FromArgb(10, 12, 8))) g.DrawRectangle(p, chip);
            using (Font f = new Font("Consolas", 9f, FontStyle.Bold))
            using (Brush t = new SolidBrush(Selected ? Color.FromArgb(150, 205, 90) : Color.FromArgb(200, 210, 185)))
                g.DrawString(ThemeName, f, t, 34, (Height - f.Height) / 2f);
            using (Pen border = new Pen(Selected ? Color.FromArgb(150, 205, 90) : Color.FromArgb(70, 78, 60), Selected ? 2f : 1f))
                g.DrawRectangle(border, 1, 1, Width - 3, Height - 3);
        }
    }

    // "What does each pilot-display button do?" - three pages of 24 buttons, each a drop-down.
    class SoftKeyForm : Form
    {
        readonly Config cfg; readonly OverlayForm owner;
        readonly ComboBox[] combos = new ComboBox[24];
        readonly Button[] pageBtns = new Button[3];
        int page;
        bool loading;
        static readonly Color Bg = Color.FromArgb(24, 27, 22), Panel = Color.FromArgb(34, 38, 31), Text1 = Color.FromArgb(205, 215, 190), Accent = Color.FromArgb(150, 205, 90), Dim = Color.FromArgb(130, 140, 115);

        public SoftKeyForm(Config c, OverlayForm o)
        {
            cfg = c; owner = o;
            Text = "Tac Map - assign the pilot-display buttons";
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.CenterParent; BackColor = Bg; ForeColor = Text1; Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(560, 560);

            for (int i = 0; i < 3; i++)
            {
                Button b = pageBtns[i] = MakeButton("PAGE " + (i + 1), 16 + i * 122, 12, 116, 30);
                b.Tag = i; b.Click += delegate (object s, EventArgs e) { page = (int)((Button)s).Tag; Fill(); };
            }
            Label hint = new Label(); hint.Text = "The PAGE button flips between pages on the display."; hint.ForeColor = Dim; hint.AutoSize = true; hint.Location = new Point(16, 48);
            Controls.Add(hint);

            string[] groups = new string[] { "TOP ROW (left to right)", "LEFT SIDE (top to bottom)", "RIGHT SIDE (top to bottom)", "BOTTOM ROW (left to right)" };
            for (int g = 0; g < 4; g++)
            {
                int gx = 16 + (g % 2) * 270, gy = 76 + (g / 2) * 206;
                Label gl = new Label(); gl.Text = groups[g]; gl.ForeColor = Accent; gl.Font = new Font("Consolas", 9f, FontStyle.Bold); gl.AutoSize = true; gl.Location = new Point(gx, gy);
                Controls.Add(gl);
                for (int r = 0; r < 6; r++)
                {
                    int idx = g * 6 + r;
                    ComboBox cb = combos[idx] = new ComboBox();
                    cb.DropDownStyle = ComboBoxStyle.DropDownList; cb.FlatStyle = FlatStyle.Flat; cb.BackColor = Panel; cb.ForeColor = Text1;
                    cb.SetBounds(gx, gy + 22 + r * 29, 252, 24); cb.Tag = idx;
                    foreach (string[] a in OverlayForm.ActionList) cb.Items.Add(a[1]);
                    cb.SelectedIndexChanged += delegate (object s, EventArgs e)
                    {
                        if (loading) return;
                        ComboBox c2 = (ComboBox)s;
                        if (c2.SelectedIndex >= 0) cfg.Pages[page][(int)c2.Tag] = OverlayForm.ActionList[c2.SelectedIndex][0];
                        owner.RefreshButtons();
                    };
                    Controls.Add(cb);
                }
            }
            Button reset = MakeButton("Reset this page", 16, 508, 150, 32);
            reset.Click += delegate { cfg.Pages[page] = Config.DefaultPages()[page]; Fill(); owner.RefreshButtons(); };
            Button resetAll = MakeButton("Reset all pages", 174, 508, 150, 32);
            resetAll.Click += delegate { cfg.Pages = Config.DefaultPages(); Fill(); owner.RefreshButtons(); };
            Button done = MakeButton("Done", 394, 508, 150, 32); done.ForeColor = Accent;
            done.Click += delegate { Close(); };
            Fill();
        }

        static Button MakeButton(string text, int x, int y, int w, int h)
        {
            Button b = new Button(); b.Text = text; b.FlatStyle = FlatStyle.Flat; b.BackColor = Panel; b.ForeColor = Text1;
            b.FlatAppearance.BorderColor = Color.FromArgb(90, 100, 75); b.SetBounds(x, y, w, h); b.Cursor = Cursors.Hand;
            return b;
        }

        void Fill()
        {
            loading = true;
            for (int i = 0; i < 3; i++) { pageBtns[i].ForeColor = i == page ? Accent : Text1; pageBtns[i].FlatAppearance.BorderColor = i == page ? Accent : Color.FromArgb(90, 100, 75); }
            for (int i = 0; i < 24; i++)
            {
                string id = cfg.Pages[page][i], found = null;
                int sel = 0;
                for (int k = 0; k < OverlayForm.ActionList.Length; k++) if (OverlayForm.ActionList[k][0] == id) { sel = k; found = id; break; }
                if (found == null) sel = 0;
                combos[i].SelectedIndex = sel;
            }
            loading = false;
        }

        protected override void Dispose(bool disposing) { base.Dispose(disposing); }
    }

    class SettingsForm : Form
    {
        static readonly Color Bg = Color.FromArgb(24, 27, 22), Panel = Color.FromArgb(34, 38, 31),
            Text1 = Color.FromArgb(205, 215, 190), Dim = Color.FromArgb(130, 140, 115), Accent = Color.FromArgb(150, 205, 90);

        readonly OverlayForm owner;
        readonly Config cfg;
        public bool QuitRequested;
        public bool MoveLiveRequested;     // "Move it by hand" was pressed: the app minimizes and lets you drag the live view
        HotkeyBox[] hk2 = new HotkeyBox[7];
        TrackBar tbLive; Label lblLive; TextBox tbBookmarks; CheckBox chkGame;
        static readonly string[] Hk2Labels = new string[] { "Peek (hold)", "Notes", "Live view", "Move live", "Timer 1", "Timer 2", "Stopwatch" };

        List<SwatchButton> swatches = new List<SwatchButton>();
        ComboBox cbMonitor, cbPos;
        TrackBar tbSize, tbOpacity, tbDockW, tbPeek, tbDockOp, tbRaise;
        Label lblSize, lblOpacity, lblHint, lblDockW, lblPeek, lblDockOp, lblRaise;
        CheckBox chkAnim, chkAuto, chkDock, chkLock, chkAds, chkHttps, chkPanel, chkBeep, chkZulu, chkLabels, chkInset, chkLive;
        List<Button> layoutBtns = new List<Button>();
        HotkeyBox[] hk = new HotkeyBox[3];
        static readonly string[] HkLabels = new string[] { "Expand / minimize", "Cycle size", "Cycle transparency" };
        static readonly string[] AnchorIds = new string[] { "center", "bottomright", "custom" };
        static readonly string[] AnchorNames = new string[] { "Center of screen", "Bottom-right corner", "Where I dragged it" };

        public SettingsForm(OverlayForm o, Config c)
        {
            owner = o; cfg = c;
            Text = "Tac Map - Settings   (v" + Updater.Version + ")";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Bg; ForeColor = Text1;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(460, 892);

            Section("THEME   (for the layout selected above)", 14);
            string[] names = Themes.Names;
            for (int i = 0; i < names.Length; i++)
            {
                SwatchButton sb = new SwatchButton(names[i]);
                sb.SetBounds(16 + (i % 3) * 144, 36 + (i / 3) * 42, 136, 34);
                sb.Selected = names[i] == Themes.Normalize(owner.CurrentTheme);
                sb.Click += delegate (object s, EventArgs e)
                {
                    SwatchButton clicked = (SwatchButton)s;
                    // "Custom Wrap" with no picture yet: ask for one first (cancel = nothing changes)
                    if (clicked.ThemeName == Themes.CustomName && !owner.HasWrap && !PickWrap()) return;
                    if (clicked.ThemeName != Themes.CustomName) owner.SetTheme(clicked.ThemeName);
                    else if (owner.HasWrap && Themes.Normalize(owner.CurrentTheme) != Themes.CustomName) owner.SetTheme(Themes.CustomName);
                    foreach (SwatchButton x in swatches) { x.Selected = (x == clicked); x.Invalidate(); }
                };
                swatches.Add(sb);
                Controls.Add(sb);
            }
            AddButton("Pick a picture for Custom Wrap...", 160, 120, 284, 34, false).Click += delegate { PickWrap(); };

            Section("DISPLAY", 132);
            AddLabel("Monitor", 16, 160);
            cbMonitor = AddCombo(110, 156, 334);
            Screen[] screens = Screen.AllScreens;
            int selMon = 0;
            for (int i = 0; i < screens.Length; i++)
            {
                cbMonitor.Items.Add((i + 1) + ":  " + screens[i].Bounds.Width + " x " + screens[i].Bounds.Height + (screens[i].Primary ? "  (primary)" : ""));
                if (!string.IsNullOrEmpty(cfg.Monitor) && screens[i].DeviceName == cfg.Monitor) selMon = i;
                else if (string.IsNullOrEmpty(cfg.Monitor) && screens[i].Primary) selMon = i;
            }
            cbMonitor.SelectedIndex = selMon;
            cbMonitor.SelectedIndexChanged += delegate
            {
                if (cbMonitor.SelectedIndex >= 0) owner.SetMonitor(Screen.AllScreens[cbMonitor.SelectedIndex].DeviceName);
                int idx = Array.IndexOf(AnchorIds, cfg.Anchor);
                if (idx >= 0) cbPos.SelectedIndex = idx;
            };

            AddLabel("Position", 16, 194);
            cbPos = AddCombo(110, 190, 334);
            foreach (string n in AnchorNames) cbPos.Items.Add(n);
            int ai = Array.IndexOf(AnchorIds, cfg.Anchor);
            cbPos.SelectedIndex = ai < 0 ? 0 : ai;
            cbPos.SelectedIndexChanged += delegate { if (cbPos.SelectedIndex >= 0) owner.SetAnchor(AnchorIds[cbPos.SelectedIndex]); };

            AddLabel("Size", 16, 230);
            tbSize = AddTrack(104, 224, 290, 25, 90, cfg.OpenSize);
            lblSize = AddLabel(cfg.OpenSize + "%", 400, 230);
            tbSize.ValueChanged += delegate { lblSize.Text = tbSize.Value + "%"; owner.SetOpenSize(tbSize.Value); };

            AddLabel("Transparency", 16, 266);
            tbOpacity = AddTrack(104, 260, 290, 25, 100, cfg.Opacity);
            lblOpacity = AddLabel(cfg.Opacity + "%", 400, 266);
            tbOpacity.ValueChanged += delegate { lblOpacity.Text = tbOpacity.Value + "%"; owner.SetOpacity(tbOpacity.Value); };

            chkAnim = AddCheck("Open / close animation", 16, 300, 200, cfg.Animate);
            chkAnim.CheckedChanged += delegate { cfg.Animate = chkAnim.Checked; };
            chkAuto = AddCheck("Start with Windows", 240, 300, 200, OverlayForm.IsAutostart());
            chkAuto.CheckedChanged += delegate { OverlayForm.SetAutostart(chkAuto.Checked); };
            chkDock = AddCheck("Show a small tablet when minimized", 16, 326, 226, cfg.ShowDock);
            chkDock.CheckedChanged += delegate { owner.SetShowDock(chkDock.Checked); };
            chkLock = AddCheck("Lock mouse while expanded", 240, 326, 210, cfg.LockMouse);
            chkLock.CheckedChanged += delegate { owner.SetLockMouse(chkLock.Checked); };

            Section("MINIMIZED TABLET", 362);
            AddLabel("Size", 16, 396);
            tbDockW = AddTrack(104, 390, 290, 200, 700, cfg.DockWidth);
            lblDockW = AddLabel(cfg.DockWidth + "px", 400, 396);
            tbDockW.ValueChanged += delegate { lblDockW.Text = tbDockW.Value + "px"; owner.SetDockWidth(tbDockW.Value); };

            AddLabel("How much shows", 16, 432);
            tbPeek = AddTrack(124, 426, 270, 20, 70, cfg.DockPeek);
            lblPeek = AddLabel(cfg.DockPeek + "%", 400, 432);
            tbPeek.ValueChanged += delegate { lblPeek.Text = tbPeek.Value + "%"; owner.SetDockPeek(tbPeek.Value); };

            AddLabel("Transparency", 16, 468);
            tbDockOp = AddTrack(104, 462, 290, 25, 100, cfg.DockOpacity);
            lblDockOp = AddLabel(cfg.DockOpacity + "%", 400, 468);
            tbDockOp.ValueChanged += delegate { lblDockOp.Text = tbDockOp.Value + "%"; owner.SetDockOpacity(tbDockOp.Value); };

            AddLabel("Raise from bottom", 16, 504);
            tbRaise = AddTrack(136, 498, 258, 0, 600, Math.Min(600, owner.GetDockRaise()));
            lblRaise = AddLabel(tbRaise.Value + "px", 400, 504);
            tbRaise.ValueChanged += delegate { lblRaise.Text = tbRaise.Value + "px"; owner.SetDockRaise(tbRaise.Value); };

            AddButton("Bottom-right", 16, 538, 138, 28, false).Click += delegate { owner.SetDockPreset("right"); tbRaise.Value = 0; };
            AddButton("Bottom-center", 162, 538, 138, 28, false).Click += delegate { owner.SetDockPreset("center"); tbRaise.Value = 0; };
            AddButton("Bottom-left", 308, 538, 136, 28, false).Click += delegate { owner.SetDockPreset("left"); tbRaise.Value = 0; };
            chkLive = AddCheck("Live screen, no frame, when minimized (size: LIVE VIEW below)", 16, 570, 428, owner.LiveMiniOn);
            chkLive.CheckedChanged += delegate { owner.SetLiveMini(chkLive.Checked); };

            Section("KEYBINDS   (click a box, then press the keys)", 602);
            string[] cur = new string[] { cfg.ToggleKey, cfg.SizeKey, cfg.OpacityKey };
            for (int i = 0; i < 3; i++)
            {
                AddLabel(HkLabels[i], 16, 632 + i * 34);
                HotkeyBox box = new HotkeyBox();
                box.SetBounds(200, 628 + i * 34, 150, 26);
                box.BackColor = Panel; box.ForeColor = Accent; box.Font = new Font("Consolas", 10f, FontStyle.Bold);
                box.Text = cur[i];
                box.TextChanged += delegate { lblHint.Text = "Applied when you press Done."; lblHint.ForeColor = Dim; };
                // fires after TextChanged, so this warning replaces the line above
                box.PlainKey += delegate { lblHint.Text = "Plain key: it won't type in the game while this app runs."; lblHint.ForeColor = Color.FromArgb(255, 160, 60); };
                hk[i] = box;
                Controls.Add(box);
            }
            Button reset = AddButton("Reset keys", 360, 628, 84, 26, false);
            reset.Click += delegate
            {
                Config d = new Config();
                hk[0].Text = d.ToggleKey; hk[1].Text = d.SizeKey; hk[2].Text = d.OpacityKey;
                hk2[0].Text = d.PeekKey; hk2[1].Text = d.NotesKey; hk2[2].Text = d.LiveKey; hk2[3].Text = d.DragKey;
                hk2[4].Text = d.Timer1Key; hk2[5].Text = d.Timer2Key; hk2[6].Text = d.StopwatchKey;
            };
            lblHint = AddLabel("", 16, 734);
            lblHint.ForeColor = Dim; lblHint.AutoSize = false; lblHint.Size = new Size(430, 18);

            Section("TIMERS, CLOCK & PANEL", 760);
            chkPanel = AddCheck("Show clock / timer panel", 16, 786, 214, cfg.ShowPanel);
            chkPanel.CheckedChanged += delegate { owner.SetShowPanel(chkPanel.Checked); };
            chkBeep = AddCheck("Beep when a timer ends", 240, 786, 210, cfg.AlertBeep);
            chkBeep.CheckedChanged += delegate { owner.SetAlertBeep(chkBeep.Checked); };
            chkZulu = AddCheck("Zulu (UTC) clock", 16, 812, 214, cfg.Zulu);
            chkZulu.CheckedChanged += delegate { owner.SetZulu(chkZulu.Checked); };
            chkLabels = AddCheck("Pilot display: button labels", 240, 812, 210, cfg.ShowLabels);
            chkLabels.CheckedChanged += delegate { owner.SetShowLabels(chkLabels.Checked); };
            AddLabel("Timer presets (m:ss)", 16, 848);
            for (int ti = 0; ti < 4; ti++)
            {
                int idx = ti;
                TextBox tb = new TextBox();
                tb.SetBounds(170 + ti * 68, 844, 60, 24);
                tb.BackColor = Panel; tb.ForeColor = Accent; tb.BorderStyle = BorderStyle.FixedSingle;
                tb.Font = new Font("Consolas", 10f, FontStyle.Bold); tb.TextAlign = HorizontalAlignment.Center;
                tb.Text = OverlayForm.DurationText(cfg.TimerSecs[ti]);
                tb.TextChanged += delegate { int sec = OverlayForm.ParseDuration(tb.Text); if (sec > 0) owner.SetTimerPreset(idx, sec); };
                tb.Leave += delegate { tb.Text = OverlayForm.DurationText(cfg.TimerSecs[idx]); };
                Controls.Add(tb);
            }
            chkInset = AddCheck("Pilot display: keep the page clear of the labels", 16, 872, 400, cfg.LabelInset);
            chkInset.CheckedChanged += delegate { owner.SetLabelInset(chkInset.Checked); };

            Section("BROWSER   (globe button next to the gear = address bar)", 900);
            chkAds = AddCheck("Block ads and trackers", 16, 926, 200, owner.AdBlockOn);
            chkAds.CheckedChanged += delegate { owner.SetAdBlock(chkAds.Checked); };
            chkHttps = AddCheck("Only allow secure (https) sites", 240, 926, 210, cfg.HttpsOnly);
            chkHttps.CheckedChanged += delegate { owner.SetHttpsOnly(chkHttps.Checked); };
            AddButton("Clear browsing data", 16, 954, 200, 28, false).Click += delegate
            {
                if (MessageBox.Show(this, "Clear history, cookies, cache and saved site logins for the tablet's browser?", "Tac Map - Settings",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) owner.ClearBrowsingData();
            };
            Label bnote = AddLabel("Downloads, camera / mic / location and pop-ups are always blocked.", 16, 988);
            bnote.ForeColor = Dim;

            // ---- LIVE VIEW ----
            Section("LIVE VIEW   (the frameless corner screen when minimized)", 1014);
            AddLabel("Size", 16, 1046);
            tbLive = AddTrack(104, 1040, 290, 15, 60, cfg.LivePct);
            lblLive = AddLabel(cfg.LivePct + "%", 400, 1046);
            tbLive.ValueChanged += delegate { lblLive.Text = tbLive.Value + "%"; owner.SetLivePct(tbLive.Value); };
            string[] cornerIds = new string[] { "br", "bl", "tr", "tl" }, cornerNames = new string[] { "Bottom-right", "Bottom-left", "Top-right", "Top-left" };
            for (int ci = 0; ci < 4; ci++)
            {
                string cid = cornerIds[ci];
                AddButton(cornerNames[ci], 16 + ci * 108, 1078, 104, 28, false).Click += delegate { owner.SetLiveCorner(cid); };
            }
            AddButton("Move it by hand...", 16, 1112, 200, 28, false).Click += delegate { MoveLiveRequested = true; Close(); };
            Label mvNote = AddLabel("then drag it, or use the Move key", 226, 1118); mvNote.ForeColor = Dim;

            // ---- MORE KEYS ----
            Section("MORE KEYS   (click a box, press keys; right-click clears)", 1150);
            string[] curKeys = new string[] { cfg.PeekKey, cfg.NotesKey, cfg.LiveKey, cfg.DragKey, cfg.Timer1Key, cfg.Timer2Key, cfg.StopwatchKey };
            for (int ki = 0; ki < 7; ki++)
            {
                int col = ki / 4, row = ki % 4;
                int lx = 16 + col * 222;
                AddLabel(Hk2Labels[ki], lx, 1184 + row * 32);
                HotkeyBox hb = new HotkeyBox();
                hb.SetBounds(lx + 84, 1180 + row * 32, 122, 26);
                hb.BackColor = Panel; hb.ForeColor = Accent; hb.Font = new Font("Consolas", 9f, FontStyle.Bold);
                hb.Text = curKeys[ki];
                hb.TextChanged += delegate { lblHint.Text = "Applied when you press Done."; lblHint.ForeColor = Dim; };
                hk2[ki] = hb;
                Controls.Add(hb);
            }
            Label keyNote = AddLabel("Peek: the tablet is open only while you HOLD that key.", 222 + 16, 1180 + 3 * 32 + 4); keyNote.ForeColor = Dim; keyNote.AutoSize = false; keyNote.Size = new Size(210, 30);

            // ---- PILOT BUTTONS, BOOKMARKS, PRESETS ----
            Section("PILOT BUTTONS, BOOKMARKS & LAYOUTS", 1330);
            AddButton("Assign what each pilot-display button does...", 16, 1356, 428, 30, false).Click += delegate
            {
                using (SoftKeyForm sk = new SoftKeyForm(cfg, owner)) sk.ShowDialog(this);
            };
            AddLabel("Bookmarks - one per line:  name | address   (the BM buttons open them)", 16, 1394);
            tbBookmarks = new TextBox();
            tbBookmarks.Multiline = true; tbBookmarks.SetBounds(16, 1414, 428, 100);
            tbBookmarks.BackColor = Panel; tbBookmarks.ForeColor = Text1; tbBookmarks.BorderStyle = BorderStyle.FixedSingle;
            tbBookmarks.Font = new Font("Consolas", 9f); tbBookmarks.WordWrap = false;
            {
                List<string> bl = new List<string>(); foreach (string b in cfg.Bookmarks) if (b.Length > 0) bl.Add(b);
                tbBookmarks.Text = string.Join("\r\n", bl.ToArray());
            }
            tbBookmarks.TextChanged += delegate
            {
                string[] lines = tbBookmarks.Text.Replace("\r", "").Split('\n');
                for (int bi = 0; bi < 6; bi++)
                {
                    string ln = bi < lines.Length ? lines[bi].Trim() : "";
                    if (ln.Length > 0 && ln.IndexOf('|') < 0)
                    {   // only an address: use its host as the name
                        string nm = ln; int sl = nm.IndexOf("://"); if (sl >= 0) nm = nm.Substring(sl + 3);
                        int cut = nm.IndexOfAny(new char[] { '/', '?' }); if (cut > 0) nm = nm.Substring(0, cut);
                        ln = nm + "|" + ln;
                    }
                    cfg.Bookmarks[bi] = ln;
                }
                owner.RefreshButtons();
            };
            Controls.Add(tbBookmarks);
            AddLabel("Layouts - save what you have now (size, place, transparency, zoom, panels):", 16, 1522);
            for (int pi = 0; pi < 3; pi++)
            {
                int pidx = pi;
                AddButton("Save as preset " + (pi + 1), 16 + pi * 146, 1544, 138, 28, false).Click += delegate { owner.SavePreset(pidx); };
            }

            // ---- SYSTEM ----
            Section("SYSTEM", 1590);
            chkGame = AddCheck("Only show the tablet / live view while the game is in front", 16, 1616, 428, cfg.GameOnly);
            chkGame.CheckedChanged += delegate { owner.SetGameOnly(chkGame.Checked); };
            AddButton("Export settings...", 16, 1648, 138, 28, false).Click += delegate { owner.ExportSettings(this); };
            AddButton("Import settings...", 162, 1648, 138, 28, false).Click += delegate { if (owner.ImportSettings(this)) { QuitRequested = true; Close(); } };
            AddButton("Open log folder", 308, 1648, 136, 28, false).Click += delegate { owner.OpenLogFolder(); };
            AddButton("Restart in safe mode (default settings)", 16, 1684, 428, 26, false).Click += delegate { if (owner.RestartInSafeMode(this)) { QuitRequested = true; Close(); } };

            Section("ACTIONS", 1716);
            AddButton("Refresh page", 16, 1740, 138, 30, false).Click += delegate { owner.RefreshPage(); };
            AddButton("Home page URL...", 162, 1740, 138, 30, false).Click += delegate { owner.ChangeUrl(); };
            AddButton("Reset position", 308, 1740, 136, 30, false).Click += delegate { owner.ResetPosition(); cbPos.SelectedIndex = 0; };

            AddButton("Check for updates", 162, 1798, 138, 34, false).Click += delegate { owner.CheckForUpdatesNow(); };
            Button quit = AddButton("Quit app", 16, 1798, 138, 34, true);
            quit.Click += delegate { QuitRequested = true; Close(); };
            Button done = AddButton("Done", 306, 1798, 138, 34, false);
            done.ForeColor = Accent;
            done.Click += delegate { Close(); };

            // the theme grid has a third row (the custom-wrap slot), and the layout choice sits on top: move everything down
            foreach (Control ctrl in Controls) { if (ctrl.Top >= 126) ctrl.Top += 42; ctrl.Top += 66; }
            Section("LAYOUT", 14);
            for (int li = 0; li < Layouts.All.Length; li++)
            {
                LayoutDef ld = Layouts.All[li];
                Button lb = AddButton(ld.Name, 16 + li * 216, 38, 212, 30, false);
                lb.Tag = ld.Id;
                lb.Click += delegate (object s, EventArgs e)
                {
                    owner.SetLayout((string)((Button)s).Tag);
                    RefreshLayoutUi();
                };
                layoutBtns.Add(lb);
            }
            RefreshLayoutUi();
            int totalH = 1798 + 42 + 66 + 34 + 16;
            ClientSize = new Size(460, totalH);

            Rectangle wa = Screen.FromRectangle(owner.Frame).WorkingArea;
            // taller than the screen allows? scroll instead of running off the bottom
            int fullH = totalH, maxH = wa.Height - 70;
            if (fullH > maxH)
            {
                AutoScroll = true;
                AutoScrollMinSize = new Size(0, fullH);
                ClientSize = new Size(460 + SystemInformation.VerticalScrollBarWidth, maxH);
            }
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + Math.Max(0, (wa.Height - Height) / 2));
        }

        // Highlight the active layout and show that layout's theme in the swatches.
        void RefreshLayoutUi()
        {
            foreach (Button b in layoutBtns)
            {
                bool on = (string)b.Tag == owner.CurrentLayoutId;
                b.ForeColor = on ? Accent : Text1;
                b.FlatAppearance.BorderColor = on ? Accent : Color.FromArgb(90, 100, 75);
                b.FlatAppearance.BorderSize = on ? 2 : 1;
            }
            string cur = Themes.Normalize(owner.CurrentTheme);
            foreach (SwatchButton x in swatches) { x.Selected = (x.ThemeName == cur); x.Invalidate(); }
        }

        // Choose a picture, wrap the tablet in it and highlight the Custom Wrap swatch. False if cancelled / unreadable.
        bool PickWrap()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Pick a picture to wrap the tablet in";
                dlg.Filter = "Pictures (png, jpg, webp, bmp, gif)|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|All files|*.*";
                dlg.CheckFileExists = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) return false;
                string err;
                if (!owner.SetCustomWrap(dlg.FileName, out err))
                {
                    MessageBox.Show(this, err, "Tac Map - Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }
            foreach (SwatchButton x in swatches) { x.Selected = (x.ThemeName == Themes.CustomName); x.Invalidate(); }
            return true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (QuitRequested) return;
            HashSet<string> seen = new HashSet<string>();
            foreach (HotkeyBox b in hk)
                if (string.IsNullOrEmpty(b.Text) || !seen.Add(b.Text.ToLowerInvariant()))
                {
                    MessageBox.Show(this, "Each action needs its own, different key.", "Tac Map - Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                    return;
                }
            foreach (HotkeyBox b in hk2)   // the extra keys may be empty (= off) but never the same as another key
                if (b.Text.Length > 0 && !seen.Add(b.Text.ToLowerInvariant()))
                {
                    MessageBox.Show(this, "Each action needs its own, different key (\"" + b.Text + "\" is used twice).", "Tac Map - Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                    return;
                }
            cfg.ToggleKey = hk[0].Text; cfg.SizeKey = hk[1].Text; cfg.OpacityKey = hk[2].Text;
            cfg.PeekKey = hk2[0].Text; cfg.NotesKey = hk2[1].Text; cfg.LiveKey = hk2[2].Text; cfg.DragKey = hk2[3].Text;
            cfg.Timer1Key = hk2[4].Text; cfg.Timer2Key = hk2[5].Text; cfg.StopwatchKey = hk2[6].Text;
        }

        // ---- small UI helpers ----
        void Section(string text, int y)
        {
            Label l = new Label();
            l.Text = text; l.ForeColor = Accent; l.Font = new Font("Consolas", 9.5f, FontStyle.Bold);
            l.AutoSize = true; l.Location = new Point(16, y); l.UseMnemonic = false;   // so "&" in a heading is shown, not turned into an underline
            Controls.Add(l);
            Panel line = new Panel();
            line.BackColor = Color.FromArgb(70, 78, 60);
            line.SetBounds(16, y + 18, 428, 1);
            Controls.Add(line);
        }

        Label AddLabel(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text; l.AutoSize = true; l.Location = new Point(x, y); l.ForeColor = Text1;
            Controls.Add(l);
            return l;
        }

        ComboBox AddCombo(int x, int y, int w)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.FlatStyle = FlatStyle.Flat;
            c.BackColor = Panel; c.ForeColor = Text1;
            c.SetBounds(x, y, w, 24);
            Controls.Add(c);
            return c;
        }

        TrackBar AddTrack(int x, int y, int w, int min, int max, int val)
        {
            TrackBar t = new TrackBar();
            t.Minimum = min; t.Maximum = max; t.Value = Math.Max(min, Math.Min(max, val));
            t.TickStyle = TickStyle.None; t.AutoSize = false; t.BackColor = Bg;
            t.SetBounds(x, y, w, 30);
            Controls.Add(t);
            return t;
        }

        CheckBox AddCheck(string text, int x, int y, int w, bool v)
        {
            CheckBox c = new CheckBox();
            c.Text = text; c.Checked = v; c.ForeColor = Text1; c.FlatStyle = FlatStyle.Flat;
            c.SetBounds(x, y, w, 22);
            Controls.Add(c);
            return c;
        }

        Button AddButton(string text, int x, int y, int w, int h, bool danger)
        {
            Button b = new Button();
            b.Text = text; b.FlatStyle = FlatStyle.Flat;
            b.BackColor = Panel; b.ForeColor = danger ? Color.FromArgb(255, 110, 100) : Text1;
            b.FlatAppearance.BorderColor = danger ? Color.FromArgb(150, 70, 60) : Color.FromArgb(90, 100, 75);
            b.SetBounds(x, y, w, h);
            b.Cursor = Cursors.Hand;
            Controls.Add(b);
            return b;
        }
    }
}
