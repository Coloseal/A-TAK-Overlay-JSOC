// Update check: reads a tiny text file you host (for example on GitHub) and compares versions.
//
// The file looks like this (one "key=value" per line):
//     latest=1.2.0        newest version that exists          -> people get a "new version available" notice
//     minimum=1.1.0       oldest version still allowed to run -> older versions are stopped until they update
//     url=https://github.com/OWNER/REPO/releases/latest        where people download the update
//     notes=What changed in this version (optional, one line)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;

[assembly: AssemblyTitle("Tac Map Overlay")]
[assembly: AssemblyProduct("Tac Map Overlay")]
[assembly: AssemblyCompany("Tac Map Overlay (open-source community project)")]
[assembly: AssemblyCopyright("MIT License - see LICENSE.txt. No keyboard hooks, no data collection.")]
[assembly: AssemblyDescription("Tac map overlay (ATAK tablet / pilot display) for BRM5. Open source, MIT license.")]
[assembly: AssemblyInformationalVersion(TacOverlay.Updater.Version)]
[assembly: AssemblyVersion(TacOverlay.Updater.Version + ".0")]
[assembly: AssemblyFileVersion(TacOverlay.Updater.Version + ".0")]

namespace TacOverlay
{
    static class Updater
    {
        // Bump this every time you release a new exe (and rebuild).
        public const string Version = "1.1.5";

        // Address of the update info file. Set this once the file is hosted (e.g. a raw GitHub URL).
        // Empty = update checking is switched off.
        public const string DefaultInfoUrl = "https://raw.githubusercontent.com/Coloseal/A-TAK-Overlay-JSOC/main/version.txt";

        public class Info
        {
            public bool Ok;              // the file could be downloaded and read
            public Version Latest, Minimum;
            public string Url = "", Notes = "";
            public string Zip = "";      // direct download address of the update zip (optional: worked out from Url when missing)
            public string Sha256 = "";   // fingerprint of that zip (optional extra check)
        }

        // Where the update zip comes from: the "zip=" line, or - for a GitHub releases address - the release asset
        // named TacMapOverlay-v<latest>.zip. Empty = no one-click update possible (the person downloads it by hand).
        public static string ZipUrl(Info i)
        {
            if (i == null || !i.Ok) return "";
            if (i.Zip.Length > 0) return i.Zip;
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(i.Url ?? "", @"^https://github\.com/([^/]+)/([^/]+)/releases", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            Version v = i.Latest ?? i.Minimum;
            if (!m.Success || v == null) return "";
            return "https://github.com/" + m.Groups[1].Value + "/" + m.Groups[2].Value + "/releases/download/v" + v + "/TacMapOverlay-v" + v + ".zip";
        }

        public static bool CanAutoUpdate(Info i) { return ZipUrl(i).Length > 0; }

        // Only GitHub addresses are accepted (a local test server only when the update address was overridden for testing).
        static bool AllowedUrl(string url, bool allowLocal)
        {
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            if (allowLocal && u.Scheme == "http" && (u.Host == "localhost" || u.Host == "127.0.0.1")) return true;
            if (u.Scheme != "https") return false;
            string h = u.Host.ToLowerInvariant();
            return h == "github.com" || h.EndsWith(".github.com") || h.EndsWith(".githubusercontent.com");
        }

        // Delete the leftovers of a previous update (old files are renamed to *.old while the program is running).
        public static void CleanOld()
        {
            try { foreach (string f in Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.old")) { try { File.Delete(f); } catch { } } } catch { }
        }

        public static Version Current { get { return new Version(Version); } }

        public static bool IsRequired(Info i) { return i != null && i.Ok && i.Minimum != null && Current < i.Minimum; }
        public static bool IsNewer(Info i) { return i != null && i.Ok && i.Latest != null && Current < i.Latest; }

        // Downloads and parses the info file. Never throws: any problem just gives Ok = false (so a broken
        // connection or a missing file never stops anyone from using the app).
        public static Info Check(string url, int timeoutMs)
        {
            Info result = new Info();
            if (string.IsNullOrEmpty(url)) return result;
            Thread t = new Thread(delegate ()
            {
                try
                {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | (SecurityProtocolType)192;
                    string u = url;
                    if (u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        u += (u.IndexOf('?') >= 0 ? "&" : "?") + "t=" + DateTime.UtcNow.Ticks;   // skip caches
                    WebRequest req = WebRequest.Create(u);
                    req.Timeout = timeoutMs;
                    req.Headers.Add("Cache-Control", "no-cache");
                    string text;
                    using (WebResponse resp = req.GetResponse())
                    using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                        text = sr.ReadToEnd();
                    Parse(text, result);
                }
                catch { result.Ok = false; }
            });
            t.IsBackground = true;
            t.Start();
            if (!t.Join(timeoutMs + 1000)) result.Ok = false;
            return result;
        }

        static void Parse(string text, Info r)
        {
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                int eq = line.IndexOf('=');
                if (eq < 1) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                string v = line.Substring(eq + 1).Trim();
                Version ver;
                switch (k)
                {
                    case "latest": if (TryVer(v, out ver)) r.Latest = ver; break;
                    case "minimum": case "min": if (TryVer(v, out ver)) r.Minimum = ver; break;
                    case "url": r.Url = v; break;
                    case "notes": r.Notes = v; break;
                    case "zip": r.Zip = v; break;
                    case "sha256": r.Sha256 = v.ToLowerInvariant(); break;
                }
            }
            r.Ok = r.Latest != null || r.Minimum != null;
        }

        static bool TryVer(string s, out Version v)
        {
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
            return System.Version.TryParse(s, out v);
        }

        public static void OpenDownload(Info i)
        {
            try
            {
                string url = (i != null && i.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) ? i.Url : null;
                if (url != null) Process.Start(url);
            }
            catch { }
        }

        // ---------- one-click update ----------
        // Shows "update available" with an Update now button. Update now downloads the zip, swaps the files in place
        // (the running program's files are renamed to *.old, which Windows allows) and starts the new version.
        // Returns true when the new version has been started: the caller must exit right away.
        public static bool ShowUpdateDialog(Info i, bool required, Rectangle area, bool allowLocal)
        {
            string zipUrl = ZipUrl(i);
            bool restarting = false;
            Version target = i.Latest ?? i.Minimum;
            using (Form f = new Form())
            {
                f.Text = "Tac Map Overlay - " + (required ? "update required" : "update available");
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.Manual;
                f.MinimizeBox = false; f.MaximizeBox = false; f.TopMost = true; f.ShowInTaskbar = true;
                f.BackColor = Color.FromArgb(24, 27, 22); f.ForeColor = Color.FromArgb(205, 215, 190);
                f.Font = new Font("Segoe UI", 9.5f);
                f.ClientSize = new Size(460, 268);
                f.Location = new Point(area.Left + (area.Width - f.Width) / 2, area.Top + (area.Height - f.Height) / 2);

                Label title = new Label();
                title.Text = required ? "UPDATE REQUIRED" : "UPDATE AVAILABLE";
                title.Font = new Font("Consolas", 13f, FontStyle.Bold); title.ForeColor = Color.FromArgb(255, 160, 20);
                title.AutoSize = true; title.Location = new Point(20, 18);

                Label body = new Label();
                body.Text = "Version " + target + " is out (you have " + Version + ")." +
                    (i.Notes.Length > 0 ? "\r\n\r\nWhat's new: " + i.Notes : "") +
                    (required ? "\r\n\r\nThis version is too old to keep running." : "") +
                    "\r\n\r\nPress Update now: it downloads the update by itself and restarts the app.";
                body.SetBounds(20, 52, 420, 112);

                ProgressBar pb = new ProgressBar();
                pb.SetBounds(20, 170, 420, 16); pb.Visible = false;
                Label status = new Label();
                status.SetBounds(20, 190, 420, 20); status.ForeColor = Color.FromArgb(150, 205, 90);

                Button go = MakeButton("Update now", 20, 222, 130, Color.FromArgb(150, 205, 90));
                Button manual = MakeButton("Download page", 165, 222, 130, Color.FromArgb(205, 215, 190));
                manual.Visible = false;
                Button later = MakeButton(required ? "Quit" : "Later", 310, 222, 130, Color.FromArgb(205, 215, 190));
                f.Controls.AddRange(new Control[] { title, body, pb, status, go, manual, later });

                WebClient wc = null;
                bool busy = false;
                Action<string> fail = delegate (string msg)
                {
                    busy = false; pb.Visible = false;
                    go.Enabled = true; go.Text = "Try again"; later.Text = required ? "Quit" : "Later";
                    manual.Visible = i.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase);
                    status.ForeColor = Color.FromArgb(255, 150, 40); status.Text = msg;
                };

                go.Click += delegate
                {
                    if (busy) return;
                    if (!AllowedUrl(zipUrl, allowLocal)) { fail("The update address isn't a GitHub address, so it was not trusted."); return; }
                    busy = true; go.Enabled = false; manual.Visible = false; later.Text = "Cancel";
                    pb.Value = 0; pb.Visible = true; status.ForeColor = Color.FromArgb(150, 205, 90); status.Text = "Downloading...";
                    string dir = Path.Combine(Path.GetTempPath(), "TacOverlayUpdate");
                    try { if (Directory.Exists(dir)) Directory.Delete(dir, true); Directory.CreateDirectory(dir); }
                    catch (Exception ex) { fail("Couldn't prepare a temporary folder: " + ex.Message); return; }
                    string zipPath = Path.Combine(dir, "update.zip");
                    try
                    {
                        ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | (SecurityProtocolType)192;
                        wc = new WebClient();
                        wc.Headers[HttpRequestHeader.UserAgent] = "TacMapOverlay-updater";
                        wc.DownloadProgressChanged += delegate (object s, DownloadProgressChangedEventArgs e)
                        {
                            pb.Value = Math.Max(0, Math.Min(100, e.ProgressPercentage));
                            status.Text = "Downloading...  " + (e.BytesReceived / 1024) + " KB";
                        };
                        wc.DownloadFileCompleted += delegate (object s, System.ComponentModel.AsyncCompletedEventArgs e)
                        {
                            if (e.Cancelled) { busy = false; pb.Visible = false; go.Enabled = true; go.Text = "Update now"; later.Text = required ? "Quit" : "Later"; status.Text = ""; return; }
                            if (e.Error != null) { fail("Download failed: " + e.Error.Message); return; }
                            status.Text = "Installing...";
                            f.Update();
                            string err = Install(zipPath, dir, i, target);
                            if (err != null) { fail(err); return; }
                            try
                            {
                                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, "--after-update " + Process.GetCurrentProcess().Id);
                                psi.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
                                Process.Start(psi);
                                restarting = true;
                                f.Close();
                            }
                            catch (Exception ex) { fail("Updated, but the new version could not be started: " + ex.Message + "\r\nStart the app again by hand."); }
                        };
                        wc.DownloadFileAsync(new Uri(zipUrl), zipPath);
                    }
                    catch (Exception ex) { fail("Couldn't start the download: " + ex.Message); }
                };
                manual.Click += delegate { OpenDownload(i); };
                later.Click += delegate
                {
                    if (busy && wc != null && wc.IsBusy) wc.CancelAsync();
                    else f.Close();
                };
                f.FormClosing += delegate { if (wc != null && wc.IsBusy) wc.CancelAsync(); };
                f.ShowDialog();
            }
            return restarting;
        }

        static Button MakeButton(string text, int x, int y, int w, Color fore)
        {
            Button b = new Button();
            b.Text = text; b.FlatStyle = FlatStyle.Flat; b.ForeColor = fore;
            b.BackColor = Color.FromArgb(34, 38, 31); b.FlatAppearance.BorderColor = fore;
            b.SetBounds(x, y, w, 34);
            return b;
        }

        // Unpacks the downloaded zip, checks it, and swaps the files in. Returns an error message, or null when done.
        static string Install(string zipPath, string workDir, Info info, Version expected)
        {
            try
            {
                if (info.Sha256.Length > 0)
                {
                    string hash;
                    using (SHA256 sha = SHA256.Create())
                    using (FileStream fs = File.OpenRead(zipPath))
                        hash = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                    if (hash != info.Sha256) return "The download doesn't match its fingerprint, so it was not installed.";
                }

                string ex = Path.Combine(workDir, "unpacked");
                string exFull = Path.GetFullPath(ex) + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(ex);
                using (ZipArchive za = ZipFile.OpenRead(zipPath))
                    foreach (ZipArchiveEntry en in za.Entries)
                    {
                        if (en.FullName.EndsWith("/") || en.Name.Length == 0) continue;
                        string dest = Path.GetFullPath(Path.Combine(ex, en.FullName));
                        if (!dest.StartsWith(exFull, StringComparison.OrdinalIgnoreCase)) return "The download contains an unsafe file path and was not installed.";
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        en.ExtractToFile(dest, true);
                    }

                string root = null;
                if (File.Exists(Path.Combine(ex, "TacOverlay.exe"))) root = ex;
                else foreach (string d in Directory.GetDirectories(ex)) if (File.Exists(Path.Combine(d, "TacOverlay.exe"))) { root = d; break; }
                if (root == null) return "The download doesn't contain TacOverlay.exe.";

                Version nv;
                if (!System.Version.TryParse(FileVersionInfo.GetVersionInfo(Path.Combine(root, "TacOverlay.exe")).FileVersion ?? "", out nv)) return "Couldn't read the downloaded version.";
                if (nv < expected) return "The download is version " + nv + ", not " + expected + ". Nothing was changed.";
                if (nv <= Current) return "The download isn't newer than what you have. Nothing was changed.";

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string exeName = Path.GetFileName(Application.ExecutablePath);
                List<string> renamed = new List<string>(), added = new List<string>();
                try
                {
                    foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                    {
                        string rel = file.Substring(root.Length).TrimStart('\\', '/');
                        if (rel.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".old", StringComparison.OrdinalIgnoreCase)) continue;   // never touch settings
                        string dest = Path.Combine(baseDir, string.Equals(rel, "TacOverlay.exe", StringComparison.OrdinalIgnoreCase) ? exeName : rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        if (File.Exists(dest))
                        {
                            if (new FileInfo(dest).Length == new FileInfo(file).Length && SameBytes(dest, file)) continue;   // unchanged
                            string old = dest + ".old";
                            if (File.Exists(old)) { try { File.Delete(old); } catch { old = dest + "." + DateTime.Now.Ticks + ".old"; } }
                            File.Move(dest, old);          // a running exe / dll can be renamed, just not overwritten
                            renamed.Add(dest);
                        }
                        File.Copy(file, dest, true);
                        added.Add(dest);
                    }
                }
                catch (Exception ex2)
                {
                    foreach (string a in added) { try { File.Delete(a); } catch { } }
                    foreach (string r in renamed) { try { string o = r + ".old"; if (File.Exists(o) && !File.Exists(r)) File.Move(o, r); } catch { } }
                    return "Couldn't replace the files (" + ex2.Message + "). Nothing was changed. If the app lives in a protected folder, move it somewhere like Documents.";
                }
                return null;
            }
            catch (Exception ex3) { return "The update couldn't be installed: " + ex3.Message; }
        }

        static bool SameBytes(string a, string b)
        {
            byte[] x = File.ReadAllBytes(a), y = File.ReadAllBytes(b);
            if (x.Length != y.Length) return false;
            for (int k = 0; k < x.Length; k++) if (x[k] != y[k]) return false;
            return true;
        }

        // Full-screen-ish "you must update" window. Returns after the person clicks either button;
        // the caller then exits the app (so the old exe is free to be replaced).
        public static void ShowRequiredDialog(Info i, Rectangle area)
        {
            using (Form f = new Form())
            {
                f.Text = "Tac Map Overlay - update required";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.Manual;   // centred on the given monitor, not wherever the mouse is
                f.MinimizeBox = false; f.MaximizeBox = false; f.TopMost = true; f.ShowInTaskbar = true;
                f.BackColor = Color.FromArgb(24, 27, 22); f.ForeColor = Color.FromArgb(205, 215, 190);
                f.Font = new Font("Segoe UI", 9.5f);
                f.ClientSize = new Size(460, 260);
                f.Location = new Point(area.Left + (area.Width - f.Width) / 2, area.Top + (area.Height - f.Height) / 2);

                Label title = new Label();
                title.Text = "UPDATE REQUIRED";
                title.Font = new Font("Consolas", 13f, FontStyle.Bold); title.ForeColor = Color.FromArgb(255, 160, 20);
                title.AutoSize = true; title.Location = new Point(20, 18);

                Label body = new Label();
                body.Text = "This version (" + Version + ") is out of date.\r\nVersion " + i.Minimum + " or newer is needed to continue." +
                    (i.Notes.Length > 0 ? "\r\n\r\nWhat's new: " + i.Notes : "") +
                    "\r\n\r\nDownload the new version, replace the old files with it, and start the app again.";
                body.SetBounds(20, 52, 420, 150);

                Button dl = new Button();
                dl.Text = "Download update"; dl.FlatStyle = FlatStyle.Flat; dl.ForeColor = Color.FromArgb(150, 205, 90);
                dl.BackColor = Color.FromArgb(34, 38, 31); dl.FlatAppearance.BorderColor = Color.FromArgb(150, 205, 90);
                dl.SetBounds(20, 212, 200, 34);
                dl.Enabled = i.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase);
                dl.Click += delegate { OpenDownload(i); f.Close(); };

                Button quit = new Button();
                quit.Text = "Quit"; quit.FlatStyle = FlatStyle.Flat; quit.ForeColor = Color.FromArgb(205, 215, 190);
                quit.BackColor = Color.FromArgb(34, 38, 31); quit.FlatAppearance.BorderColor = Color.FromArgb(90, 100, 75);
                quit.SetBounds(240, 212, 200, 34);
                quit.Click += delegate { f.Close(); };

                f.Controls.AddRange(new Control[] { title, body, dl, quit });
                f.ShowDialog();
            }
        }
    }
}
