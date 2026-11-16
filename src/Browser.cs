// The tablet's built-in browser: address bar, security rules and the ad / tracker blocker.
//
// The page engine is Microsoft Edge's (WebView2): the same sandboxed, auto-updating Chromium that Brave and Chrome are built
// on. On top of it this file:
//   - upgrades http:// to https:// and refuses pages with bad certificates (no "proceed anyway")
//   - denies every permission request (camera, microphone, location, notifications, clipboard ...)
//   - blocks downloads, pop-up windows opened without a click, file:// pages, and links that launch other programs
//   - turns off password saving / autofill and the developer tools
//   - blocks requests to known ad and tracker servers, and skips / hides YouTube ads
//   - only tells the page it is inside the overlay when it is YOUR tac map site, never other sites
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace TacOverlay
{
    // The little toolbar that sits at the top of the tablet's screen: back, forward, reload, home, address, shield, close.
    class BrowserBar : Panel
    {
        public readonly Button Back = new Button(), Fwd = new Button(), Reload = new Button(), Home = new Button(), Ads = new Button(), CloseBtn = new Button();
        public readonly TextBox Addr = new TextBox();
        readonly Panel box = new Panel();
        readonly Label lockLbl = new Label();
        Font symFont, addrFont, lockFont;
        public event Action<string> Go;        // Enter pressed in the address box
        public event Action FocusPage;         // Esc pressed: hand the keyboard back to the page

        static readonly Color Bg = Color.FromArgb(24, 27, 22), BtnBg = Color.FromArgb(40, 45, 36), Field = Color.FromArgb(12, 15, 10),
            Fg = Color.FromArgb(205, 215, 190), Dim = Color.FromArgb(105, 112, 95), Green = Color.FromArgb(150, 205, 90), Warn = Color.FromArgb(255, 150, 40);

        public BrowserBar()
        {
            BackColor = Bg;
            Button[] all = new Button[] { Back, Fwd, Reload, Home, Ads, CloseBtn };
            foreach (Button b in all)
            {
                b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.UseVisualStyleBackColor = false;
                b.BackColor = BtnBg; b.ForeColor = Fg; b.TabStop = false; b.Cursor = Cursors.Hand; b.Padding = Padding.Empty;
                b.TextAlign = ContentAlignment.MiddleCenter;
                Controls.Add(b);
            }
            Back.Text = "←"; Fwd.Text = "→"; Reload.Text = "⟳"; Home.Text = "⌂"; CloseBtn.Text = "✕"; Ads.Text = "⛨";
            Controls.Add(box);
            box.BackColor = Field;
            lockLbl.BackColor = Field; lockLbl.TextAlign = ContentAlignment.MiddleCenter; lockLbl.AutoSize = false;
            Addr.BorderStyle = BorderStyle.None; Addr.BackColor = Field; Addr.ForeColor = Fg;
            box.Controls.Add(lockLbl); box.Controls.Add(Addr);
            Addr.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; if (Go != null) Go(Addr.Text); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; if (FocusPage != null) FocusPage(); }
            };
            Addr.Enter += delegate { BeginInvoke((MethodInvoker)delegate { Addr.SelectAll(); }); };
        }

        public void Relayout(int w, int h)
        {
            int p = Math.Max(3, h / 8), bs = Math.Max(14, h - 2 * p);
            float fpx = Math.Max(10f, bs * 0.55f);
            FontUtil.Apply(ref symFont, "Segoe UI Symbol", fpx, FontStyle.Regular, new Control[] { Back, Fwd, Reload, Home, Ads, CloseBtn });
            FontUtil.Apply(ref addrFont, "Segoe UI", Math.Max(10f, bs * 0.48f), FontStyle.Regular, new Control[] { Addr });
            FontUtil.Apply(ref lockFont, "Segoe UI Symbol", Math.Max(9f, bs * 0.46f), FontStyle.Regular, new Control[] { lockLbl });

            int x = p;
            Button[] left = new Button[] { Back, Fwd, Reload, Home };
            foreach (Button b in left) { b.SetBounds(x, p, bs, bs); x += bs + Math.Max(2, p / 2); }
            int closeX = w - p - bs;
            CloseBtn.SetBounds(closeX, p, bs, bs);
            int adsW = (int)(bs * 2.5);
            int adsX = closeX - Math.Max(2, p / 2) - adsW;
            Ads.SetBounds(adsX, p, adsW, bs);
            int bx = x, bw = Math.Max(40, adsX - Math.Max(2, p / 2) - bx);
            box.SetBounds(bx, p, bw, bs);
            lockLbl.SetBounds(2, 0, bs, bs);
            Addr.SetBounds(bs + 2, Math.Max(0, (bs - Addr.Height) / 2), Math.Max(10, bw - bs - 6), Addr.Height);
        }

        public void SetUrl(string url)
        {
            bool internalPage = string.IsNullOrEmpty(url) || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase);
            if (!Addr.Focused) Addr.Text = internalPage ? "" : url;
            if (internalPage) { lockLbl.Text = ""; return; }
            bool secure = url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            lockLbl.Text = secure ? "🔒" : "⚠";
            lockLbl.ForeColor = secure ? Green : Warn;
        }

        public void SetNav(bool canBack, bool canFwd, bool loading)
        {
            Back.Enabled = canBack; Fwd.Enabled = canFwd;
            Back.ForeColor = canBack ? Fg : Dim; Fwd.ForeColor = canFwd ? Fg : Dim;
            Reload.Text = loading ? "✕" : "⟳";
        }

        public void SetAds(bool on, int count)
        {
            Ads.Text = "⛨ " + (on ? count.ToString() : "off");
            Ads.ForeColor = on ? Green : Dim;
        }
    }

    partial class OverlayForm
    {
        BrowserBar bar;
        bool barVisible;
        bool fullscreenEl;             // a page element (a video) is fullscreen inside the screen: hide the bar
        bool internalNav;              // the next navigation is our own page (NavigateToString)
        int blocked;                   // ads / trackers stopped since the app started
        int upgrades;                  // http -> https upgrades in a row (guards against redirect loops)
        string homeScriptId, adScriptId;
        CoreWebView2Environment webEnv;
        System.Windows.Forms.Timer barTimer;
        int lastBlockedShown = -1;
        int lastDownloadNote;

        // ---------- ad / tracker list ----------
        static HashSet<string> blockHosts;
        static readonly string[] blockPatterns = new string[]
        {
            "youtube.com/api/stats/ads", "youtube.com/pagead/", "youtube.com/get_midroll_", "youtube.com/youtubei/v1/player/ad_break",
            "youtube.com/ptracking?", "facebook.com/tr/", "facebook.com/tr?", "/pagead/", "/gampad/ads", "/ads/ga-audiences"
        };

        const string DefaultBlockList =
            "doubleclick.net googlesyndication.com googleadservices.com googletagservices.com adservice.google.com google-analytics.com " +
            "amazon-adsystem.com adsystem.amazon.com adnxs.com adsrvr.org advertising.com criteo.com criteo.net taboola.com outbrain.com " +
            "scorecardresearch.com quantserve.com quantcount.com moatads.com rubiconproject.com pubmatic.com openx.net casalemedia.com " +
            "indexww.com sharethrough.com 3lift.com smartadserver.com adform.net bidswitch.net contextweb.com yieldmo.com teads.tv " +
            "mathtag.com bluekai.com krxd.net demdex.net everesttech.net omtrdc.net 2o7.net hotjar.com hotjar.io mouseflow.com fullstory.com " +
            "ads-twitter.com analytics.twitter.com ads.linkedin.com bat.bing.com clarity.ms analytics.tiktok.com ads.tiktok.com " +
            "ads.yahoo.com adtech.com yieldlab.net zedo.com popads.net popcash.net propellerads.com exoclick.com trafficjunky.net " +
            "juicyads.com adcash.com mgid.com revcontent.com adskeeper.com onclickads.net clickadu.com hilltopads.net a-ads.com " +
            "ad-delivery.net adsafeprotected.com doubleverify.com serving-sys.com tapad.com rlcdn.com agkn.com adition.com " +
            "adsco.re adsterra.com adspyglass.com trafficfactory.biz tsyndicate.com realsrv.com syndication.exoclick.com " +
            "pagead2.googlesyndication.com tpc.googlesyndication.com ads.pubmatic.com ib.adnxs.com secure.adnxs.com " +
            "app-measurement.com branch.io appsflyer.com adjust.com kochava.com ml314.com lijit.com sovrn.com media.net " +
            "applovin.com unityads.unity3d.com ads.mopub.com mopub.com inmobi.com smaato.net vungle.com chartboost.com ironsrc.com " +
            "cdn.carbonads.com carbonads.net buysellads.com adblade.com nativeads.com zemanta.com nend.net outbrainimg.com " +
            "imrworldwide.com chartbeat.net chartbeat.com parsely.com newrelic.com nr-data.net sentry-cdn.com " +
            "pixel.facebook.com pixel.quantserve.com ct.pinterest.com ads.pinterest.com snap.licdn.com px.ads.linkedin.com sc-static.net tr.snapchat.com";

        static void LoadBlockList()
        {
            if (blockHosts != null) return;
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string h in DefaultBlockList.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) set.Add(h);
            try
            {
                // optional extra list next to the exe: one domain per line (a leading "||" and trailing "^" are fine)
                string extra = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "blocklist.txt");
                if (File.Exists(extra))
                    foreach (string raw in File.ReadAllLines(extra))
                    {
                        string l = raw.Trim();
                        if (l.Length == 0 || l[0] == '#' || l[0] == '!' || l[0] == ';') continue;
                        if (l.StartsWith("||")) l = l.Substring(2);
                        int cut = l.IndexOfAny(new char[] { '^', '/', '$', '*' });
                        if (cut >= 0) l = l.Substring(0, cut);
                        if (l.IndexOf('.') > 0 && l.IndexOf(' ') < 0) set.Add(l.ToLowerInvariant());
                    }
            }
            catch { }
            blockHosts = set;
        }

        // host of an http(s) address without building a Uri (this runs for every request the page makes)
        static string HostOf(string url)
        {
            int s = url.IndexOf("://", StringComparison.Ordinal);
            if (s < 0) return "";
            s += 3;
            int e = s;
            while (e < url.Length && url[e] != '/' && url[e] != ':' && url[e] != '?' && url[e] != '#') e++;
            return url.Substring(s, e - s).ToLowerInvariant();
        }

        bool IsBlockedRequest(string url)
        {
            string host = HostOf(url);
            if (host.Length == 0) return false;
            string home = HomeHost();
            if (home.Length > 0 && (host == home || host.EndsWith("." + home, StringComparison.Ordinal))) return false;   // never block the map site itself
            string h = host;
            while (true)
            {
                if (blockHosts.Contains(h)) return true;
                int dot = h.IndexOf('.');
                if (dot < 0 || h.IndexOf('.', dot + 1) < 0) break;   // stop before the bare TLD
                h = h.Substring(dot + 1);
            }
            string low = url.ToLowerInvariant();
            foreach (string p in blockPatterns) if (low.IndexOf(p, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        // ---------- helpers ----------
        string HomeHost()
        {
            try
            {
                Uri u;
                if (Uri.TryCreate(NormalizeUrl(cfg.Url), UriKind.Absolute, out u)) return u.Host.ToLowerInvariant();
            }
            catch { }
            return "";
        }

        static bool IsLocalHost(string host)
        {
            if (string.IsNullOrEmpty(host)) return false;
            host = host.ToLowerInvariant();
            if (host == "localhost" || host == "::1" || host == "[::1]" || host.EndsWith(".local") || host.EndsWith(".localhost") || host.IndexOf('.') < 0) return true;
            if (host.StartsWith("127.") || host.StartsWith("10.") || host.StartsWith("192.168.")) return true;
            if (host.StartsWith("172."))
            {
                string[] p = host.Split('.');
                int second;
                if (p.Length > 1 && int.TryParse(p[1], out second) && second >= 16 && second <= 31) return true;
            }
            return false;
        }

        static string JsString(string s)
        {
            StringBuilder sb = new StringBuilder("'");
            foreach (char c in s ?? "")
            {
                if (c == '\\' || c == '\'') sb.Append('\\');
                if (c >= ' ' && c < 127) sb.Append(c);
            }
            return sb.Append('\'').ToString();
        }

        // What was typed in the address box -> a safe address (or null if it can't be one).
        string ResolveInput(string text)
        {
            string s = (text ?? "").Trim();
            if (s.Length == 0) return null;
            if (s.IndexOf("://", StringComparison.Ordinal) >= 0)
            {
                Uri u;
                if (Uri.TryCreate(s, UriKind.Absolute, out u) && (u.Scheme == "https" || u.Scheme == "http")) return u.AbsoluteUri;
                return null;   // file://, ftp://, javascript ... are not allowed
            }
            // "something:" that is not host:port  ->  javascript:, data:, mailto: ... are treated as plain search words
            bool schemeLike = Regex.IsMatch(s, @"^[A-Za-z][A-Za-z0-9+.\-]*:(?!\d)");
            bool hostLike = !schemeLike && s.IndexOf(' ') < 0 && (s.IndexOf('.') > 0 || s.StartsWith("localhost", StringComparison.OrdinalIgnoreCase));
            if (hostLike)
            {
                string h = s; int cut = h.IndexOfAny(new char[] { '/', '?', '#', ':' }); if (cut > 0) h = h.Substring(0, cut);
                return (IsLocalHost(h) ? "http://" : "https://") + s;
            }
            return cfg.SearchUrl + Uri.EscapeDataString(s);
        }

        // ---------- set-up (called once the page engine exists) ----------
        async void SetupBrowser(CoreWebView2Environment env, CoreWebView2 wv)
        {
            webEnv = env;
            LoadBlockList();
            CoreWebView2Settings st = wv.Settings;
            st.AreDevToolsEnabled = false;
            st.IsStatusBarEnabled = false;
            st.IsWebMessageEnabled = false;          // pages cannot talk to this program
            st.AreHostObjectsAllowed = false;
            st.IsZoomControlEnabled = false;         // the tablet decides the zoom
            st.IsPasswordAutosaveEnabled = false;
            st.IsGeneralAutofillEnabled = false;
            st.IsReputationCheckingRequired = true;  // Microsoft SmartScreen: warns about phishing / malware sites

            // --- address bar ---
            bar = new BrowserBar();
            bar.Visible = false;
            webHost.Controls.Add(bar);
            bar.Go += delegate (string text) { NavigateTyped(text); };
            bar.FocusPage += delegate { FocusPageNow(); };
            bar.Back.Click += delegate { if (wv.CanGoBack) wv.GoBack(); FocusPageNow(); };
            bar.Fwd.Click += delegate { if (wv.CanGoForward) wv.GoForward(); FocusPageNow(); };
            bar.Reload.Click += delegate { if (loadingNow) wv.Stop(); else wv.Reload(); FocusPageNow(); };
            bar.Home.Click += delegate { NavigateConfigured(); FocusPageNow(); };
            bar.Ads.Click += delegate { SetAdBlock(!cfg.AdBlock); };
            bar.CloseBtn.Click += delegate { ShowBar(false); };
            bar.SetAds(cfg.AdBlock, blocked);
            barTimer = new System.Windows.Forms.Timer();
            barTimer.Interval = 600;
            barTimer.Tick += delegate { if (barVisible && blocked != lastBlockedShown) { lastBlockedShown = blocked; bar.SetAds(cfg.AdBlock, blocked); } };
            barTimer.Start();

            // --- navigation rules ---
            wv.NavigationStarting += delegate (object s, CoreWebView2NavigationStartingEventArgs e)
            {
                Uri u;
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out u)) { e.Cancel = true; return; }
                string scheme = u.Scheme;
                if (scheme == "https") { upgrades = 0; loadingNow = true; UpdateBarState(); return; }
                if (scheme == "http")
                {
                    if (cfg.HttpsOnly && !IsLocalHost(u.Host))
                    {
                        e.Cancel = true;
                        if (++upgrades > 3) { upgrades = 0; ShowBlockedPage("This site only works over plain http, which this browser does not allow.", e.Uri); return; }
                        UriBuilder ub = new UriBuilder(u); ub.Scheme = "https"; ub.Port = -1;
                        string target = ub.Uri.AbsoluteUri;
                        BeginInvoke((MethodInvoker)delegate { wv.Navigate(target); });
                        return;
                    }
                    loadingNow = true; UpdateBarState(); return;
                }
                if (scheme == "about" && e.Uri.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase)) return;
                if (scheme == "chrome-error") return;
                if (scheme == "data" && internalNav) { internalNav = false; return; }
                e.Cancel = true;   // file:, ftp:, edge:, javascript: ... never
            };
            wv.NavigationCompleted += delegate { loadingNow = false; UpdateBarState(); };
            wv.SourceChanged += delegate { UpdateBarState(); };
            wv.HistoryChanged += delegate { UpdateBarState(); };
            wv.NewWindowRequested += delegate (object s, CoreWebView2NewWindowRequestedEventArgs e)
            {
                e.Handled = true;                       // never open a second window
                if (e.IsUserInitiated) wv.Navigate(e.Uri);   // a click opens it in the same view (checked again by the rules above)
            };
            wv.LaunchingExternalUriScheme += delegate (object s, CoreWebView2LaunchingExternalUriSchemeEventArgs e) { e.Cancel = true; };
            wv.ServerCertificateErrorDetected += delegate (object s, CoreWebView2ServerCertificateErrorDetectedEventArgs e)
            {
                e.Action = CoreWebView2ServerCertificateErrorAction.Cancel;   // bad certificate: no "continue anyway" button
                string bad = e.RequestUri;
                BeginInvoke((MethodInvoker)delegate { ShowBlockedPage("This site's security certificate is not valid, so the connection was refused. It may be fake or tampered with.", bad); });
            };
            wv.ClientCertificateRequested += delegate (object s, CoreWebView2ClientCertificateRequestedEventArgs e) { e.Cancel = true; };
            wv.BasicAuthenticationRequested += delegate (object s, CoreWebView2BasicAuthenticationRequestedEventArgs e) { e.Cancel = true; };
            wv.PermissionRequested += delegate (object s, CoreWebView2PermissionRequestedEventArgs e)
            {
                e.State = CoreWebView2PermissionState.Deny;   // camera, microphone, location, notifications, clipboard, sensors ...
                e.Handled = true;
            };
            wv.DownloadStarting += delegate (object s, CoreWebView2DownloadStartingEventArgs e)
            {
                e.Cancel = true; e.Handled = true;
                if (unchecked(Environment.TickCount - lastDownloadNote) > 4000)
                {
                    lastDownloadNote = Environment.TickCount;
                    tray.ShowBalloonTip(3000, "Tac Map Overlay", "Downloads are blocked in the tablet's browser.", ToolTipIcon.Info);
                }
            };
            wv.ContainsFullScreenElementChanged += delegate
            {
                fullscreenEl = wv.ContainsFullScreenElement;
                if (webShown) PositionWeb();
            };
            // Ctrl+L = jump to the address bar
            ctl.AcceleratorKeyPressed += delegate (object s, CoreWebView2AcceleratorKeyPressedEventArgs e)
            {
                if (e.KeyEventKind == CoreWebView2KeyEventKind.KeyDown && e.VirtualKey == 0x4C && (Control.ModifierKeys & Keys.Control) != 0)
                {
                    e.Handled = true;
                    BeginInvoke((MethodInvoker)delegate { ShowBar(true); });
                }
            };

            // --- ad and tracker blocking ---
            wv.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            wv.WebResourceRequested += delegate (object s, CoreWebView2WebResourceRequestedEventArgs e)
            {
                if (!cfg.AdBlock) return;
                string uri = e.Request.Uri;
                if (IsBlockedRequest(uri))
                {
                    e.Response = webEnv.CreateWebResourceResponse(null, 403, "Blocked", "Content-Type: text/plain");
                    blocked++;
                }
            };

            await RegisterScripts();
            ApplyLayoutToPage();
        }

        bool loadingNow;

        void UpdateBarState()
        {
            if (bar == null || ctl == null) return;
            CoreWebView2 wv = ctl.CoreWebView2;
            bar.SetNav(wv.CanGoBack, wv.CanGoForward, loadingNow);
            bar.SetUrl(wv.Source);
        }

        void FocusPageNow()
        {
            if (ctl != null && grabbed) { Native.ForceForeground(webHost.Handle); ctl.MoveFocus(CoreWebView2MoveFocusReason.Programmatic); }
        }

        void NavigateTyped(string text)
        {
            string u = ResolveInput(text);
            if (u == null)
            {
                bar.Addr.BackColor = Color.FromArgb(70, 24, 20);
                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer(); t.Interval = 500;
                t.Tick += delegate { t.Stop(); t.Dispose(); bar.Addr.BackColor = Color.FromArgb(12, 15, 10); };
                t.Start();
                return;
            }
            try { ctl.CoreWebView2.Navigate(u); } catch { }
            FocusPageNow();
        }

        void ShowBlockedPage(string why, string url)
        {
            internalNav = true;
            ctl.CoreWebView2.NavigateToString("<html><body style='background:#15180f;color:#cdd7be;font-family:Segoe UI,sans-serif;" +
                "display:flex;align-items:center;justify-content:center;height:100vh;margin:0;text-align:center'><div style='max-width:80%'>" +
                "<h2 style='color:#ffa014'>Page blocked</h2><p>" + System.Net.WebUtility.HtmlEncode(why) + "</p>" +
                "<p style='color:#7c8a6a;word-break:break-all'>" + System.Net.WebUtility.HtmlEncode(url) + "</p></div></body></html>");
        }

        // ---------- scripts injected into pages ----------
        const string YouTubeScript = @"(function(){
if(!/(^|\.)youtube\.com$/.test(location.hostname))return;
var css='ytd-ad-slot-renderer,ytd-in-feed-ad-layout-renderer,ytd-display-ad-renderer,ytd-promoted-sparkles-web-renderer,ytd-promoted-video-renderer,ytd-banner-promo-renderer,ytd-statement-banner-renderer,ytd-companion-slot-renderer,ytd-action-companion-ad-renderer,#masthead-ad,#player-ads,.video-ads,.ytp-ad-module,.ytp-ad-overlay-container,.ytp-ad-image-overlay{display:none!important}';
function addCss(){var s=document.createElement('style');s.textContent=css;(document.head||document.documentElement).appendChild(s);}
if(document.documentElement)addCss();else document.addEventListener('DOMContentLoaded',addCss);
var was=null;
setInterval(function(){
 var p=document.querySelector('.html5-video-player');if(!p)return;
 var v=p.querySelector('video.html5-main-video')||p.querySelector('video');
 var ad=p.classList.contains('ad-showing')||p.classList.contains('ad-interrupting');
 if(ad&&v){
  if(was===null)was=v.muted;
  v.muted=true;
  try{v.playbackRate=16;}catch(e){}
  if(isFinite(v.duration)&&v.duration>0&&v.currentTime<v.duration-0.2){try{v.currentTime=v.duration-0.1;}catch(e){}}
 }else if(was!==null&&v){v.muted=was;v.playbackRate=1;was=null;}
 var b=document.querySelector('.ytp-skip-ad-button,.ytp-ad-skip-button,.ytp-ad-skip-button-modern,.ytp-ad-skip-button-container button');
 if(b)b.click();
},250);
})();";

        string HomeScript()
        {
            string host = HomeHost();
            if (host.Length == 0) return null;
            string tag = DeviceArt.Cur.PageTag;
            string aspect = ((double)HoleRect.Width / HoleRect.Height).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            // mode: "full" = the tablet is expanded, "mini" = minimized into the frameless live corner view
            string mode = LiveShown ? "mini" : "full";
            string vw = cfg.PageWidth.ToString(), vh = ((int)(cfg.PageWidth * (double)(LiveShown ? dockRect.Height : HoleRect.Height) / Math.Max(1, LiveShown ? dockRect.Width : HoleRect.Width))).ToString();
            // The map picks its sharpness (pixel ratio) ONCE, when it is created. The live corner view zooms the page out, which
            // would make the map blurry for good if it was created then. So the map site always sees the same, steady pixel ratio:
            // the one the expanded tablet uses (at least 1, at most 2 = the most the map uses anyway). MapPixelRatio= in the ini overrides.
            double dpr = cfg.MapPixelRatio;
            if (dpr < 0.5)
            {
                dpr = 1.0;
                try { Rectangle o = ComputeOpenRect(); dpr = Math.Max(1.0, Math.Min(2.0, HoleRect.Width * (o.Width / (double)ImgW) / Math.Max(300, cfg.PageWidth))); } catch { }
            }
            dpr = Math.Max(0.5, Math.Min(3.0, dpr));
            string dprJs = "try{Object.defineProperty(window,'devicePixelRatio',{get:function(){return " + dpr.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + ";},configurable:true});}catch(e){}";
            return "(function(){if(location.hostname!==" + JsString(host) + ")return;" + dprJs +
                "window.TAC_OVERLAY={tablet:true,layout:" + JsString(tag) + ",aspect:" + aspect + ",mode:" + JsString(mode) + ",viewport:{w:" + vw + ",h:" + vh + "}};" +
                "function m(){document.documentElement.setAttribute('data-overlay'," + JsString(tag) + ");document.documentElement.setAttribute('data-overlay-mode'," + JsString(mode) + ");}" +
                "if(document.documentElement)m();document.addEventListener('DOMContentLoaded',m);" +
                "try{window.dispatchEvent(new CustomEvent('tacoverlay-mode',{detail:window.TAC_OVERLAY}));}catch(e){}})();";
        }

        // The tablet went to / came back from the frameless live corner view: tell the page (your map site only).
        void NotifyPageMode() { ApplyLayoutToPage(); }

        // (Re)register the scripts that run in every new page: the "you are in the overlay" flag (only on YOUR map site)
        // and the YouTube ad skipper (when ad blocking is on).
        async System.Threading.Tasks.Task RegisterScripts()
        {
            if (ctl == null) return;
            CoreWebView2 wv = ctl.CoreWebView2;
            try
            {
                if (homeScriptId != null) { wv.RemoveScriptToExecuteOnDocumentCreated(homeScriptId); homeScriptId = null; }
                string hs = HomeScript();
                if (hs != null) homeScriptId = await wv.AddScriptToExecuteOnDocumentCreatedAsync(hs);
                if (adScriptId != null) { wv.RemoveScriptToExecuteOnDocumentCreated(adScriptId); adScriptId = null; }
                if (cfg.AdBlock) adScriptId = await wv.AddScriptToExecuteOnDocumentCreatedAsync(YouTubeScript);
            }
            catch { }
        }

        // The layout changed (ATAK <-> pilot): re-register the flag and update the page that is already open.
        async void ApplyLayoutToPage()
        {
            if (ctl == null) return;
            await RegisterScripts();
            try
            {
                string hs = HomeScript();
                if (hs != null) await ctl.CoreWebView2.ExecuteScriptAsync(hs + ";window.dispatchEvent(new Event('resize'));");
            }
            catch { }
        }

        // The home / map address changed.
        void HomeChanged() { ApplyLayoutToPage(); }

        // ---------- bar show / hide ----------
        void ToggleBar() { ShowBar(!barVisible); }

        void ShowBar(bool show)
        {
            if (bar == null) return;
            barVisible = show;
            if (webShown) PositionWeb();
            RenderState(true);
            if (show)
            {
                UpdateBarState();
                bar.SetAds(cfg.AdBlock, blocked);
                if (!Program.TestMode) Native.ForceForeground(webHost.Handle);
                bar.Addr.Focus();
                bar.Addr.SelectAll();
            }
            else FocusPageNow();
        }

        // Called from PositionWeb: place the bar (if shown) and the page below it.
        void LayoutBar(double sc, int m, int w, int h)
        {
            bool show = bar != null && barVisible && !fullscreenEl;
            bool showNotes = notes != null && notesVisible && !fullscreenEl;
            bool showTools = tools != null && toolsVisible && !fullscreenEl && !showNotes;
            SidePanel rp = showNotes ? (SidePanel)notes : (showTools ? (SidePanel)tools : null);
            if (notes != null && rp != notes) notes.Visible = false;
            if (tools != null && rp != tools) tools.Visible = false;
            int barH = show ? (int)Math.Max(28, Math.Min(52, Math.Round(15.5 * sc))) : 0;
            int pad = (int)Math.Round(HoleRadius * sc * 0.35);
            // pilot display with labels: keep the bar / notes clear of the label strips along the screen edges
            bool lab = IsPilot && cfg.ShowLabels;
            int topInset = lab ? (int)Math.Round(19 * sc) : 0, botInset = lab ? (int)Math.Round(19 * sc) : 0, rightInset = lab ? (int)Math.Round(78 * sc) : 0;
            int pageTop = 0, notesTop = m + pad / 2 + topInset;
            double holeX = HoleRect.X * sc - m, holeY = HoleRect.Y * sc - m;   // this window's origin, in frame pixels
            infoMinTop = 0f; notesLeftInFrame = -1f;
            if (bar != null)
            {
                if (show)
                {
                    int bx = m + pad, by = m + pad / 2 + topInset, bw = w - 2 * (m + pad);
                    bar.SetBounds(bx, by, bw, barH);
                    bar.Relayout(bw, barH);
                    bar.Visible = true;
                    pageTop = by + barH; notesTop = pageTop;
                    infoMinTop = (float)(holeY + pageTop + 6 * sc);
                }
                else bar.Visible = false;
            }
            int pageW = w;
            if (rp != null)
            {
                int nw = (int)Math.Round((w - 2 * m) * (showTools ? 0.38 : 0.34));
                int nx = w - m - rightInset - nw, nb = m + Math.Max(2, pad / 2) + botInset;
                rp.SetBounds(nx, notesTop, nw, Math.Max(40, h - nb - notesTop));
                rp.Relayout(nw, rp.Height, sc);
                rp.Visible = true;
                pageW = nx;
                notesLeftInFrame = (float)(holeX + nx);
            }
            // optional: the page never sits under the labels (a bit smaller, nothing hidden)
            bool inset = lab && cfg.LabelInset;
            int pl = inset ? m + rightInset : 0, pr = inset ? m + rightInset : 0;
            int pt = pageTop > 0 ? pageTop : (inset ? m + topInset : 0), pb = inset ? m + botInset : 0;
            int pageRight = rp != null ? pageW : w - pr;
            ctl.Bounds = new Rectangle(pl, pt, Math.Max(10, pageRight - pl), Math.Max(10, h - pt - pb));
        }

        // ---------- developer self-test (--selftest <folder>) ----------
        static System.Threading.Tasks.Task Delay(int ms) { return System.Threading.Tasks.Task.Delay(ms); }

        async System.Threading.Tasks.Task<string> Eval(string js)
        {
            try { return await ctl.CoreWebView2.ExecuteScriptAsync(js); } catch (Exception ex) { return "ERR " + ex.Message; }
        }

        async System.Threading.Tasks.Task WaitLoaded(int maxMs)
        {
            int t = 0;
            while (t < maxMs && (loadingNow || t < 600)) { await Delay(150); t += 150; }
            await Delay(400);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool GetClipCursor(out Native.RECT r);

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr h);

        void Shot(string path) { ShotRect(frame, path); }

        void ShotRect(Rectangle f, string path)
        {
            try
            {
                using (Bitmap bm = new Bitmap(f.Width, f.Height))
                using (Graphics g = Graphics.FromImage(bm))
                {
                    IntPtr sdc = Native.GetDC(IntPtr.Zero), ddc = g.GetHdc();
                    BitBlt(ddc, 0, 0, f.Width, f.Height, sdc, f.Left, f.Top, 0x00CC0020 | 0x40000000);   // SRCCOPY | CAPTUREBLT (includes layered windows)
                    g.ReleaseHdc(ddc); Native.ReleaseDC(IntPtr.Zero, sdc);
                    bm.Save(path);
                }
            }
            catch (Exception ex) { try { File.AppendAllText(Path.ChangeExtension(path, ".err.txt"), ex.ToString()); } catch { } }
        }

        // developer helper: log exactly when the tablet opens / closes while a test script presses and holds the PEEK hotkey
        public async void RunPeekTest(string dir)
        {
            StringBuilder log = new StringBuilder();
            Action<string> L = delegate (string s) { log.AppendLine(DateTime.UtcNow.ToString("HH:mm:ss.fff") + "  " + s); try { File.WriteAllText(Path.Combine(dir, "peektest.txt"), log.ToString()); } catch { } };
            try
            {
                int waited = 0;
                while (ctl == null && waited < 25000) { await Delay(100); waited += 100; }
                await WaitLoaded(8000);
                L("keyboard delay setting = " + ((SystemInformation.KeyboardDelay + 1) * 250) + " ms, repeat speed index = " + SystemInformation.KeyboardSpeed);
                File.WriteAllText(Path.Combine(dir, "ready.txt"), "ready");
                bool last = open;
                L("READY  open=" + open);
                Stopwatch sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 25000 && !File.Exists(Path.Combine(dir, "stop.txt")))
                {
                    if (open != last) { last = open; L("tablet " + (open ? "OPENED" : "CLOSED")); }
                    await Delay(10);
                }
                L("DONE");
            }
            catch (Exception ex) { L("EXCEPTION " + ex); }
            Quit();
        }

        // developer helper: start with the live view ON (the page loads zoomed-out), expand, and see what pixel ratio the map site sees
        public async void RunLiveStartTest(string dir)
        {
            StringBuilder log = new StringBuilder();
            Action<string> L = delegate (string s) { log.AppendLine(s); try { File.WriteAllText(Path.Combine(dir, "livestart.txt"), log.ToString()); } catch { } };
            try
            {
                int waited = 0;
                while ((ctl == null || !LiveShown) && waited < 25000) { await Delay(150); waited += 150; }
                await WaitLoaded(12000);
                L("started in live view: LiveShown=" + LiveShown + "   page zoom factor = " + ctl.ZoomFactor.ToString("0.00") + "   rasterization scale = " + ctl.RasterizationScale);
                L("   what the MAP SITE sees: devicePixelRatio = " + await Eval("window.devicePixelRatio") + "   (a steady value >= 1 is correct; about 0.55 = the old blurry bug)");
                OpenTablet(); await Delay(1500);
                L("expanded: page zoom factor = " + ctl.ZoomFactor.ToString("0.00") + "   devicePixelRatio seen by the map site = " + await Eval("window.devicePixelRatio"));
                CloseTablet(); await Delay(1200);
                L("minimized again (live): page zoom factor = " + ctl.ZoomFactor.ToString("0.00") + "   devicePixelRatio seen by the map site = " + await Eval("window.devicePixelRatio"));
                ctl.CoreWebView2.Navigate("https://example.com/"); await WaitLoaded(10000);
                L("other site (example.com) in the live view: devicePixelRatio = " + await Eval("window.devicePixelRatio") + "   (real value, not changed)");
                L("DONE");
            }
            catch (Exception ex) { L("EXCEPTION " + ex); }
            Quit();
        }

        // developer helper: how smooth are the real open / close animations?
        public async void RunAnimTest(string dir)
        {
            StringBuilder log = new StringBuilder();
            Action<string> L = delegate (string s) { log.AppendLine(s); try { File.WriteAllText(Path.Combine(dir, "animtest.txt"), log.ToString()); } catch { } };
            Func<string, string> stats = delegate (string what)
            {
                List<double> st = new List<double>(AnimStamps); AnimStamps.Clear();
                if (st.Count < 3) return what + ": only " + st.Count + " frames";
                double tot = st[st.Count - 1] - st[0], max = 0; int slow = 0;
                for (int i = 1; i < st.Count; i++) { double d = st[i] - st[i - 1]; if (d > max) max = d; if (d > 25) slow++; }
                return string.Format("{0,-34} {1,3} frames over {2,4:0} ms  = {3,5:0} fps average, longest gap {4,4:0} ms, gaps over 25 ms: {5}", what, st.Count, tot, (st.Count - 1) * 1000.0 / Math.Max(1, tot), max, slow);
            };
            try
            {
                int waited = 0;
                while ((ctl == null || !open || animating) && waited < 25000) { await Delay(100); waited += 100; }
                L(stats("first open, nothing prepared yet (" + DeviceArt.Cur.Id + ")"));
                foreach (string lay in new string[] { "atak", "pilot" })
                {
                    if (lay != DeviceArt.Cur.Id) { SetLayout(lay); await Delay(1500); }
                    for (int round = 0; round < 2; round++)
                    {
                        AnimStamps.Clear();
                        CloseTablet(); while (animating) await Delay(20); await Delay(250);
                        L(stats("close (" + lay + ")"));
                        await Delay(700);
                        AnimStamps.Clear();
                        OpenTablet(); while (animating) await Delay(20); await Delay(250);
                        L(stats("open  (" + lay + ")" + (round == 0 ? "" : " 2nd time")) + "   | final hand-over to the live page took " + LastFinishMs.ToString("0") + " ms");
                    }
                }
                L("DONE");
            }
            catch (Exception ex) { L("EXCEPTION " + ex); }
            Quit();
        }

        public async void RunSelfTest(string dir)
        {
            StringBuilder log = new StringBuilder();
            Action<string> L = delegate (string s) { log.AppendLine(s); try { File.WriteAllText(Path.Combine(dir, "selftest.txt"), log.ToString()); } catch { } };
            try
            {
                int waited = 0;
                while ((ctl == null || !webShown || animating) && waited < 20000) { await Delay(200); waited += 200; }
                L("ready after " + waited + " ms; webShown=" + webShown + " open=" + open);
                CoreWebView2 wv = ctl.CoreWebView2;
                await WaitLoaded(8000);
                L("home loaded: " + wv.Source + "   title=" + wv.DocumentTitle);
                Shot(Path.Combine(dir, "01_home.png"));

                wv.Navigate("http://example.com/"); await WaitLoaded(10000);
                L("http://example.com -> " + wv.Source + "   (expect https)");
                string before = wv.Source;
                wv.Navigate("file:///C:/Windows/win.ini"); await Delay(1500);
                L("file:///C:/Windows/win.ini -> source stays " + wv.Source + "   (expect unchanged: " + before + ")");
                wv.Navigate("ftp://example.com/"); await Delay(800);
                L("ftp:// -> " + wv.Source + "   (expect unchanged)");

                L("ResolveInput 'cats playing piano'  -> " + ResolveInput("cats playing piano"));
                L("ResolveInput 'example.com/a b'     -> " + ResolveInput("example.com/a b"));
                L("ResolveInput 'youtube.com'         -> " + ResolveInput("youtube.com"));
                L("ResolveInput 'localhost:8080'      -> " + ResolveInput("localhost:8080"));
                L("ResolveInput 'javascript:alert(1)' -> " + ResolveInput("javascript:alert(1)"));
                L("ResolveInput 'file:///c:/x'        -> " + (ResolveInput("file:///c:/x") ?? "(rejected)"));
                L("ResolveInput 'data:text/html,hi'   -> " + ResolveInput("data:text/html,hi"));

                // on the example.com page: geolocation must be denied, an ad request must be blocked
                await WaitLoaded(5000);
                int b0 = blocked;
                await Eval("window.__geo='pending';navigator.geolocation.getCurrentPosition(function(){window.__geo='GRANTED';},function(e){window.__geo='denied('+e.code+')';});");
                await Eval("fetch('https://securepubads.g.doubleclick.net/tag/js/gpt.js').catch(function(){});fetch('https://www.google-analytics.com/analytics.js').catch(function(){});");
                await Delay(2500);
                L("geolocation request -> " + await Eval("window.__geo") + "   (expect denied(1))");
                L("ad/tracker requests blocked: " + (blocked - b0) + "   (expect 2)");
                L("ad blocker off, same fetches ->");
                cfg.AdBlock = false; int b1 = blocked;
                await Eval("fetch('https://securepubads.g.doubleclick.net/tag/js/gpt.js').catch(function(){});");
                await Delay(1500);
                L("   blocked: " + (blocked - b1) + "   (expect 0)");
                cfg.AdBlock = true;

                // bad certificate
                wv.Navigate("https://expired.badssl.com/"); await WaitLoaded(10000);
                L("expired certificate site -> source=" + wv.Source + "  title=" + wv.DocumentTitle + "   (expect an error page, not the site)");
                Shot(Path.Combine(dir, "02_badcert.png"));

                // address bar + youtube
                ShowBar(true); await Delay(800);
                L("bar visible=" + (bar != null && bar.Visible) + " bar size=" + bar.Size + " page bounds=" + ctl.Bounds);
                NavigateTyped("youtube.com"); await WaitLoaded(15000);
                L("typed youtube.com -> " + wv.Source + "   title=" + wv.DocumentTitle);
                await Delay(3000);
                L("youtube ad-skip script active (style tag present): " + await Eval("!!document.querySelector('style')"));
                L("youtube video count: " + await Eval("document.querySelectorAll('video').length"));
                Shot(Path.Combine(dir, "03_youtube_bar.png"));
                NavigateTyped("cat videos"); await WaitLoaded(12000);
                L("typed 'cat videos' -> " + wv.Source);
                Shot(Path.Combine(dir, "04_search.png"));

                // other layout
                SetLayout("pilot"); await Delay(1500);
                L("switched to pilot: frame=" + frame + " hole page bounds=" + ctl.Bounds + " data-overlay on current page=" + await Eval("document.documentElement.getAttribute('data-overlay')"));
                wv.Navigate(NormalizeUrl(cfg.Url)); await WaitLoaded(10000);
                L("home again: " + wv.Source + "   data-overlay=" + await Eval("document.documentElement.getAttribute('data-overlay')") + "   TAC_OVERLAY=" + await Eval("JSON.stringify(window.TAC_OVERLAY)"));
                Shot(Path.Combine(dir, "05_pilot_home.png"));
                ShowBar(false); await Delay(500);
                Shot(Path.Combine(dir, "06_pilot_nobar.png"));
                // ---- pilot display: soft keys, timers, alarm, notes ----
                cfg.AlertBeep = false;
                double sc2 = frame.Width / (double)ImgW; int bad = 0;
                foreach (SoftKey k in SoftKeys)
                {
                    string hid = HitExtra(new Point((int)(k.X * sc2), (int)(k.Y * sc2)));
                    if (hid != "SK:" + k.Id) { bad++; L("  hit-test mismatch: " + k.Id + " -> " + hid); }
                }
                L("soft key hit-test (button centres): " + (SoftKeys.Length - bad) + "/" + SoftKeys.Length + " ok");
                foreach (Hotspot hs in Spots)
                {
                    string hid = HitExtra(new Point((int)((hs.R.X + hs.R.Width / 2) * sc2), (int)((hs.R.Y + hs.R.Height / 2) * sc2)));
                    L("  rocker/switch " + hs.Id + " -> " + hid);
                }
                Shot(Path.Combine(dir, "07_pilot_labels.png"));
                SetTimerPreset(0, 2); PressExtra("TM1"); await Delay(600);
                L("timer 1 (2s preset) running: " + tmrs[0].Running + "   label: " + KeyTextFor("TM1"));
                await Delay(2600);
                L("timer 1 ended -> done=" + tmrs[0].Done + " alarm=" + AnyAlert + "   label: " + KeyTextFor("TM1") + "   dock line: '" + DockInfo() + "'");
                Shot(Path.Combine(dir, "08_alarm.png"));
                PressExtra("TM1");
                L("pressed again -> alarm cleared: " + !AnyAlert);
                PressExtra("STW"); await Delay(1300); L("stopwatch running: " + KeyTextFor("STW")); PressExtra("STW"); L("stopped: " + KeyTextFor("STW")); PressExtra("STW"); L("reset: " + KeyTextFor("STW"));
                PressExtra("MSN"); await Delay(1200); L("mission clock: " + KeyTextFor("MSN")); PressExtra("MRST"); L("mission reset: " + KeyTextFor("MSN"));
                PressExtra("UTC"); L("clock mode now: " + ClockText(true)); PressExtra("UTC");
                PressExtra("XPAR"); L("XPAR pressed -> opacity " + cfg.Opacity); PressExtra("XPAR"); PressExtra("XPAR"); PressExtra("XPAR");
                PressExtra("VC_UP"); L("zoom in (rocker) -> PageWidth " + cfg.PageWidth); PressExtra("ZRST"); L("zoom reset -> PageWidth " + cfg.PageWidth);
                PressExtra("WEB"); L("WEB key -> address bar visible=" + barVisible);
                PressExtra("NOTE"); await Delay(500);
                L("NOTE key -> notes visible=" + notes.Visible + " notes bounds=" + notes.Bounds + " page bounds=" + ctl.Bounds);
                foreach (Control c in notes.Controls) if (c is TextBox) c.Text = "9-LINE: grid 123456 / freq 40.5 / enemy 2x BMP";
                await Delay(1500);
                string nf = Path.Combine(NotesPanel.Dir, "notes1.txt");
                L("note saved to disk: " + (File.Exists(nf) ? File.ReadAllText(nf) : "(missing)"));
                Shot(Path.Combine(dir, "09_notes.png"));
                PressExtra("NOTE"); PressExtra("WEB"); await Delay(300);
                L("closed notes -> visible=" + notes.Visible + " page bounds=" + ctl.Bounds);
                PressExtra("OFF"); await Delay(800);
                L("OFF switch -> tablet open=" + open);
                OpenTablet(); await Delay(800);
                L("reopened -> open=" + open);

                // ---- live corner view: frameless screen while minimized ----
                SetLiveMini(true);
                L("LIVE switched on while expanded -> LiveMini=" + cfg.LiveMini + "  still open=" + open + "  LiveShown=" + LiveShown + "   (expect: on, open, not shown yet)");
                CloseTablet(); await Delay(1500);
                int exs = Native.GetWindowLong(webHost.Handle, Native.GWL_EXSTYLE);
                L("minimized, live on -> LiveShown=" + LiveShown + "  frame visible=" + IsWindowVisible(Handle) + "  page window visible=" + IsWindowVisible(webHost.Handle) +
                  "  click-through=" + ((exs & Native.WS_EX_TRANSPARENT) != 0) + "  rect=" + dockRect + "  (expect: shown, no frame, page visible, click-through)");
                L("   page was told: mode attr=" + await Eval("document.documentElement.getAttribute('data-overlay-mode')") + "  TAC_OVERLAY.mode=" + await Eval("window.TAC_OVERLAY && window.TAC_OVERLAY.mode") + "  viewport=" + await Eval("JSON.stringify(window.TAC_OVERLAY && window.TAC_OVERLAY.viewport)"));
                ShotRect(dockRect, Path.Combine(dir, "10_live_pilot.png"));
                OpenTablet(); await Delay(1500);
                L("expanded again -> open=" + open + "  LiveShown=" + LiveShown + "  frame visible=" + IsWindowVisible(Handle) + "  mode attr=" + await Eval("document.documentElement.getAttribute('data-overlay-mode')") + "   (expect full)");
                SetLayout("atak"); await Delay(1200);
                CloseTablet(); await Delay(1500);
                L("ATAK minimized, live on -> LiveShown=" + LiveShown + "  rect=" + dockRect);
                ShotRect(dockRect, Path.Combine(dir, "11_live_atak.png"));
                OpenTablet(); await Delay(1200);
                PressExtra("LIVE");
                L("ATAK top button pressed -> LiveMini=" + cfg.LiveMini + "   (expect off)");
                CloseTablet(); await Delay(1200);
                L("minimized, live off -> LiveShown=" + LiveShown + "  frame visible=" + IsWindowVisible(Handle) + "  page window visible=" + IsWindowVisible(webHost.Handle) + "   (expect the little tablet back: frame yes, page no)");
                OpenTablet(); await Delay(1200);

                // ================= NEW FEATURES =================
                SetLayout("pilot"); await Delay(1500);
                if (!open) { OpenTablet(); await Delay(800); }

                // --- button pages, bookmarks ---
                int s0; string sp = KeyTextAct("PAGE", out s0);
                PressExtra("PAGE"); await Delay(200);
                int[] sts = new int[3]; string t1 = KeyText(SoftKeys[6], out sts[0]), t2 = KeyText(SoftKeys[0], out sts[1]), t3 = KeyText(SoftKeys[1], out sts[2]);
                L("PAGE key -> page " + (softPage + 1) + "  labels on page 2: L1='" + t1 + "' T1='" + t2 + "' T2='" + t3 + "'   (expect page 2; BM1 -, MAP, LOG +)");
                string hid2 = HitExtra(new Point((int)(SoftKeys[6].X * sc2), (int)(SoftKeys[6].Y * sc2)));
                L("page 2 hit-test on L1 -> " + hid2);
                Shot(Path.Combine(dir, "12_page2.png"));
                cfg.Bookmarks[0] = "DDG|https://duckduckgo.com/"; RefreshButtons();
                PressExtra("SK:L1"); await WaitLoaded(12000);
                L("bookmark 1 pressed -> " + wv.Source + "   (expect duckduckgo)");
                PressExtra("PAGE"); PressExtra("PAGE"); L("pages cycle back to " + (softPage + 1));

                // --- mission log ---
                softPage = 1; PressExtra("LOG"); await Delay(600);
                L("LOG key -> notes visible=" + notesVisible + "  log starts with: '" + (notes.LogText.Length > 0 ? notes.LogText.Substring(0, Math.Min(14, notes.LogText.Length)) : "(empty)") + "'");
                ShowNotes(false); softPage = 0;

                // --- tools: bearing / distance ---
                ShowTools(0); await Delay(500);
                foreach (Control c in tools.Controls) { if (c.Name == "brgFrom") c.Text = "123456"; if (c.Name == "brgTo") c.Text = "125456"; }
                await Delay(300);
                string brg = ""; foreach (Control c in tools.Controls) if (c.Name == "brgRes") brg = c.Text.Replace("\r\n", " | ");
                L("bearing/distance 123456 -> 125456: " + brg + "   (expect 200 m, 090)");
                foreach (Control c in tools.Controls) { if (c.Name == "brgFrom") c.Text = "1234 5678"; if (c.Name == "brgTo") c.Text = "1234 5778"; }
                await Delay(300);
                foreach (Control c in tools.Controls) if (c.Name == "brgRes") brg = c.Text.Replace("\r\n", " | ");
                L("bearing/distance 8-digit due north 1000 m: " + brg + "   (expect 1,000 m, 000 deg)");
                Shot(Path.Combine(dir, "13_tools_brg.png"));
                ShowTools(1); await Delay(400);
                foreach (Control c in tools.Controls) { if (c.Name == "nine0") c.Text = "BP MIKE"; if (c.Name == "nine5") c.Text = "123456"; }
                tools.SaveAll();
                L("9-line saved: " + (File.Exists(Path.Combine(NotesPanel.Dir, "nineline.txt")) ? File.ReadAllLines(Path.Combine(NotesPanel.Dir, "nineline.txt"))[0] : "(missing)"));
                Shot(Path.Combine(dir, "14_tools_nine.png"));
                ShowTools(2); await Delay(400);
                CheckedListBox clb = null; foreach (Control c in tools.Controls) if (c.Name == "chkList") clb = (CheckedListBox)c;
                clb.SetItemChecked(1, true); await Delay(300);
                L("checklist items=" + clb.Items.Count + "  ticked saved: '" + File.ReadAllText(Path.Combine(NotesPanel.Dir, "checklist_state.txt")) + "'   (expect 8 items, '1')");
                Shot(Path.Combine(dir, "15_tools_check.png"));
                ShowTools(false); await Delay(300);

                // --- presets ---
                int sz0 = cfg.OpenSize; SavePreset(0); SetOpenSize(50); await Delay(500);
                L("size changed to " + cfg.OpenSize + ", applying preset 1 ...");
                ApplyPreset(0); await Delay(600);
                L("preset 1 applied -> size back to " + cfg.OpenSize + "   (expect " + sz0 + ")");

                // --- peek: key not held, so it should open and close again by itself ---
                CloseTablet(); await Delay(500);
                PeekPressed(); await Delay(2200);
                L("hold-to-peek with the key NOT held -> open=" + open + "   (expect false: it closed itself)");

                // --- hotkey helpers ---
                NotesHotkey(); await Delay(1000);
                L("notes hotkey while minimized -> open=" + open + " notes visible=" + notesVisible + "   (expect true, true)");
                ShowNotes(false);
                CloseTablet(); await Delay(400);
                TimerHotkey(1); L("timer 2 hotkey -> running=" + tmrs[1].Running); TimerHotkey(1); L("again -> running=" + tmrs[1].Running);

                // --- live view: default size, alarm banner, drag ---
                OpenTablet(); await Delay(600);
                cfg.LivePct = 30; cfg.LiveX = int.MinValue; cfg.LiveY = int.MinValue;
                SetLiveMini(true); CloseTablet(); await Delay(1200);
                Rectangle mon = CurrentScreen().Bounds;
                L("live view default size: " + dockRect.Width + " px wide = " + (100 * dockRect.Width / mon.Width) + "% of the monitor (" + mon.Width + ")   (expect about 30%)");
                SetTimerPreset(0, 2); PressExtra("TM1"); await Delay(2800);
                L("alarm while in live view -> AnyAlert=" + AnyAlert + "  frame visible=" + IsWindowVisible(Handle));
                ShotRect(dockRect, Path.Combine(dir, "16_live_alarm.png"));
                PressTimer(0);
                SetLiveDrag(true); await Delay(300);
                int exd = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
                L("move mode on -> liveDrag=" + liveDrag + "  frame grabbable (not click-through)=" + ((exd & Native.WS_EX_TRANSPARENT) == 0));
                ShotRect(dockRect, Path.Combine(dir, "17_live_move.png"));
                frame = new Rectangle(mon.Left + 120, mon.Top + 90, frame.Width, frame.Height);   // as if it had been dragged here
                EndLiveDrag(); await Delay(300);
                L("dropped -> LiveX=" + cfg.LiveX + " LiveY=" + cfg.LiveY + "  dragMode=" + liveDrag + "  new rect=" + dockRect + "   (expect 120,90 and drag mode off)");
                SetLiveCorner("br"); await Delay(200);

                // --- game-aware ---
                cfg.GameProcess = "ThisProgramDoesNotExist"; SetGameOnly(true); await Delay(1300);
                L("game-only on, game not in front -> hidden=" + gameHidden + "  page window visible=" + IsWindowVisible(webHost.Handle) + "   (expect hidden)");
                SetGameOnly(false); await Delay(300);
                L("game-only off -> hidden=" + gameHidden + "  page window visible=" + IsWindowVisible(webHost.Handle) + "   (expect shown)");
                cfg.GameProcess = "RobloxPlayerBeta";

                // --- settings file round trip + log ---
                cfg.Pages[1][2] = "SHOT"; cfg.Bookmarks[2] = "X|https://example.org/"; cfg.Presets[1] = "50|center|||80|1050|1|1|0";
                string tmpIni = Path.Combine(dir, "roundtrip.ini"); cfg.Save(tmpIni);
                Config back = Config.Load(tmpIni);
                L("settings round trip: page2 slot3=" + back.Pages[1][2] + "  bookmark3=" + back.Bookmarks[2] + "  preset2=" + back.Presets[1] + "  LivePct=" + back.LivePct + "  PeekKey=" + back.PeekKey + "  Timers=" + string.Join(",", Array.ConvertAll(back.TimerSecs, delegate (int q) { return q.ToString(); })));
                AppLog.Write("self-test log line");
                L("log file exists: " + File.Exists(Path.Combine(AppLog.Dir, "tacoverlay.log")));
                SetLiveMini(false);
                OpenTablet(); await Delay(800);
                SetLayout("pilot");

                // mouse lock must never trap the mouse while the settings window is open
                Rectangle screenAll = SystemInformation.VirtualScreen;
                Func<string> clipState = delegate
                {
                    Native.RECT cr; GetClipCursor(out cr);
                    bool free = cr.Left <= screenAll.Left && cr.Top <= screenAll.Top && cr.Right >= screenAll.Right && cr.Bottom >= screenAll.Bottom;
                    return free ? "FREE (whole desktop)" : "LOCKED to " + cr.Left + "," + cr.Top + "-" + cr.Right + "," + cr.Bottom;
                };
                cfg.LockMouse = true; grabbed = true;
                UpdateClip();
                L("expanded, mouse lock on        -> " + clipState() + "   (expect LOCKED)");
                string insideSettings = "";
                System.Windows.Forms.Timer ct = new System.Windows.Forms.Timer(); ct.Interval = 700;
                ct.Tick += delegate
                {
                    ct.Stop();
                    insideSettings += "settings just opened -> " + clipState() + " | ";
                    SetOpenSize(Math.Max(30, cfg.OpenSize - 7));      // like dragging the Size slider
                    insideSettings += "after changing size -> " + clipState() + " | ";
                    SetLayout("atak");                                 // like pressing a layout button
                    insideSettings += "after switching layout -> " + clipState() + " | ";
                    SetLockMouse(true);                                // like ticking "lock mouse"
                    insideSettings += "after ticking lock mouse -> " + clipState();
                    foreach (Form of in Application.OpenForms) if (of is SettingsForm) { of.Close(); break; }
                };
                ct.Start();
                OpenSettings();                                        // modal: returns when the timer closes it
                L("INSIDE SETTINGS: " + insideSettings + "   (expect all FREE)");
                L("settings closed                -> " + clipState() + "   (expect LOCKED again)");
                grabbed = false; Native.ClipCursor(IntPtr.Zero);
                L("minimized                      -> " + clipState() + "   (expect FREE)");
                L("DONE");
            }
            catch (Exception ex) { L("EXCEPTION " + ex); }
            Quit();
        }

        // ---------- settings the gear window changes ----------
        public bool AdBlockOn { get { return cfg.AdBlock; } }

        public async void SetAdBlock(bool on)
        {
            cfg.AdBlock = on;
            SaveState();
            if (bar != null) bar.SetAds(on, blocked);
            await RegisterScripts();
            if (ctl != null) ctl.CoreWebView2.Reload();
            FocusPageNow();
        }

        public void SetHttpsOnly(bool on) { cfg.HttpsOnly = on; SaveState(); }

        public async void ClearBrowsingData()
        {
            if (ctl == null) return;
            try
            {
                await ctl.CoreWebView2.Profile.ClearBrowsingDataAsync();
                ctl.CoreWebView2.Reload();
                tray.ShowBalloonTip(3000, "Tac Map Overlay", "Browsing data cleared (history, cookies, cache, site logins).", ToolTipIcon.Info);
            }
            catch { }
        }
    }
}
