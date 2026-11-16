# Tac Map Overlay

A tac-map overlay for BRM5 (Roblox): your map site on a rugged ATAK tablet or a helicopter display, over your game, with a
secure built-in browser, timers, notes and tools. Windows 10/11, no installer.

**Open source (MIT) - no admin rights, no installer, no keyboard hooks, no data collection.**
Read [PRIVACY-AND-SECURITY.txt](PRIVACY-AND-SECURITY.txt) for exactly what it does and what it connects to, and
[SECURITY.md](SECURITY.md) to report a problem.

## Download
Get the newest zip from **[Releases](../../releases/latest)**, extract it anywhere, run `TacOverlay.exe`.
Each release lists the **SHA-256** of the zip (check with `Get-FileHash .\TacMapOverlay-v<version>.zip`) and a VirusTotal link.
Windows may warn about a new, unsigned program: right-click the zip > Properties > **Unblock**, then extract; if SmartScreen
shows, *More info > Run anyway*. Never turn off your antivirus for this or any program. From version 1.1.0 on, the app updates
itself with one click.

## Build it yourself
`powershell -ExecutionPolicy Bypass -File build.ps1` - needs only Windows (it uses the C# compiler that ships with it). The two
Microsoft-signed WebView2 DLLs are fetched from the official NuGet package. The photos used in the official releases are not
in this repository (see [assets/README.txt](assets/README.txt)); the build draws simple stand-ins instead.
**Make a release:** `powershell -ExecutionPolicy Bypass -File tools\release.ps1` builds the zip, prints its SHA-256 and writes the
GitHub release text (`release-notes.md`) and the `version.txt` lines (`version.txt.new`).

A rugged ATAK-style tablet that expands over your game and shows your tac map.
When minimized, a small peek of the tablet's top stays in the bottom-right corner so you know it's running.

## Run
Double-click the **Tac Map Overlay** shortcut on your Desktop (or `TacOverlay.exe` in the folder it points to).
Keep the whole folder together (the .dll files are needed). The first launch expands the tablet; after that it waits minimized.

## How it works
**One key does it all: Ctrl+F1** (change it in settings; any keyboard key works).
- Press it -> the tablet **expands** (animated), takes your keyboard and mouse, and keeps the mouse inside the tablet
  (so you can pan / zoom / click the map without touching the game).
- Press it again -> the tablet **minimizes** to the corner, the mouse is freed and control goes back to the game.
No separate interact key. You can also click the minimized tablet to expand it.

| Key | Action |
|---|---|
| **Ctrl+F1** | Expand / minimize |
| Ctrl+F8 | Cycle size (45 / 62 / 78 / 90 % of the monitor width) |
| Ctrl+F9 | Cycle transparency |

## The minimized tablet
- It is the whole tablet pushed below the edge of the screen, so only its top shows (no crop line). In a fullscreen game it
  sits on the very bottom edge; on the normal desktop it rests on top of the taskbar. It follows that automatically.
- **Drag it** anywhere (click without dragging = expand). Drop it near the bottom and it snaps flush to the bottom edge.
  Dragging needs a free mouse - in a game that is capturing the mouse, use the settings below instead.
- **Settings > MINIMIZED TABLET:** size, how much of it shows, transparency, **raise from bottom** (lift it up to clear HUD
  / keybinds), and Bottom-right / Bottom-center / Bottom-left presets. It can also be turned off completely.

## Settings (gear icon, top right of the tablet)
- **Theme:** Coyote Tan, Black, Ranger Green, Arid, JG Camo, MultiCam (live preview), and **Custom Wrap**: pick any
  picture (PNG, JPG, WebP, BMP, GIF) and it is wrapped over the tablet's body. The metal shading, the rubber parts,
  the buttons and the screen stay, so it looks like a vinyl skin. The picture fills the tablet (centred, extra is cropped).
  Each person's picture is kept on their own PC (`%LocalAppData%\TacOverlay\wrap.png`); nothing is uploaded anywhere.
- **Monitor** it opens on, **position**, **size**, **transparency**, animation on/off, minimized peek on/off,
  **lock mouse while expanded** on/off
- **Keybinds:** click a box, press the keys you want - any keyboard key, alone or with Ctrl / Alt / Shift
- Refresh page, change the map URL, reset position, start with Windows, **Quit**
Drag the tablet by its frame (it remembers where, and which monitor); resize from the bottom-right corner.
Everything is saved in `TacOverlay.ini` next to the exe (`PageWidth`, `AnimMs`, `DockWidth` live there too).

## Layouts
Settings > **LAYOUT** switches the device picture:
- **ATAK tablet** - the wide rugged tablet (screen about 2.1 : 1).
- **Pilot display** - a multi-function display with a button row on every side (screen about 1.5 : 1, nearly 4:3).
Everything else (size, transparency, the minimized peek, themes, Custom Wrap, keys) works the same. Each layout remembers
its own theme. The page is told which one is active (see below).

## Pilot display buttons (soft keys), timers, clock, notes
On the pilot display every button is live and has a label on the screen next to it (like a real MFD):
- **Top:** MAP (go to the tac map) | WEB (address bar) | NOTE (notes pad) | TIME (clock / timer panel) | SIZE | MENU (settings)
- **Left:** TMR1-TMR4 (start a countdown; press again to cancel, or to dismiss the alarm) | STOPWATCH (start / stop / reset) | CLR (clear timers)
- **Right:** BACK | FWD | REFRESH | ADS (block ads on/off) | XPAR (transparency) | LOCK (mouse lock on/off)
- **Bottom:** MSN CLK (mission clock start / pause) | MSN RST | LOCAL / ZULU | COPY URL | SCREENSHOT (to clipboard) | ZOOM (reset)
- **Rockers:** VID/BRT = transparency, BK LT/BRT = tablet size, VID/CON = page zoom. **OFF** switch = minimize.
Timer lengths are set in Settings > "Timer presets". When one ends the status light flashes, the labels flash, the minimized
tablet shows "TMR n DONE", and it beeps (switch off in settings). The little minimized tablet also shows the time / the next timer.
The **ATAK tablet** gets two round buttons beside the globe: a stopwatch (timer menu) and a notes page.
The **notes pad** has 4 pages, saved on your PC as you type (`%LocalAppData%\TacOverlay\notes`), with COPY and CLEAR.
"Keep the page clear of the labels" (settings) shrinks the page so the labels never cover it.

## More keys (all changeable in Settings > MORE KEYS; right-click a box to switch a key off)
| Key | Action |
|---|---|
| Ctrl+Alt+P | **Peek** - HOLD it: the tablet is open only while you hold the key |
| Ctrl+Alt+N | Notes pad on / off (opens the tablet if it is minimized) |
| Ctrl+Alt+L | Live corner view on / off |
| Ctrl+Alt+M | **Move** the live view (it becomes draggable for a few seconds, then click-through again) |
| Ctrl+Alt+1 / 2 | Start / cancel timer 1 / timer 2 |
| Ctrl+Alt+S | Stopwatch start / stop / reset |

## Pilot display: three pages of buttons
The **PAGE** button flips between three pages. Page 1 is the everyday set (map, web, notes, timers, back/forward, ads, ...).
Page 2 is tools: **bookmarks** (BM1-BM6, set in Settings), **LOG +** (adds a time-stamped line to the mission log = notes page "LOG"),
**BRG/DIST** (bearing + distance between two grids), **9-LINE** (CAS template you can copy), **CHECKLIST** (editable, ticks are saved),
zoom / size, **MOVE LIVE**, **COPY LOG**, **PRESET 1-3** (saved layouts). Page 3 is system (settings, update check, clear data).
Settings > "Assign what each pilot-display button does..." lets you put any action on any button of any page.
Layout presets: arrange the tablet how you like (size, place, transparency, zoom, panels) and press "Save as preset 1/2/3".

## Live corner view (no frame when minimized)
Press the **LIVE button** at the top of the tablet (ATAK: a little "screen with a picture in its corner" icon beside the
globe; pilot display: the **LIVE** key on the top row) - or tick it in Settings / the tray menu. From then on, when you
minimize, the frame disappears and the live screen stays in the corner instead, as big as the whole tablet was, with no
border. It is click-through, so the game keeps your mouse. It has its own size (default **30% of the monitor's width**, so a
video is clearly readable; transparency = the minimized transparency) - see Settings > LIVE VIEW for size, corner
and "Move it by hand". A timer that ends flashes a banner on it. Press the toggle key to expand again; press LIVE again
(or untick it) to go back to the little tablet.

## Other things in the settings
- **Only show while the game is in front** (hides the tablet / live view when you alt-tab away; the game is `RobloxPlayerBeta`).
- **Export / Import settings**, and **Open log folder** (`%LocalAppData%\TacOverlay\logs` - send `tacoverlay.log` if something breaks).
- **Safe mode:** Settings > "Restart in safe mode" (or the tray menu, or start with `--safe`) restarts with default settings; your old ones are kept as `TacOverlay.ini.bak`.
The page is told which mode it is in (`data-overlay-mode`, `tacoverlay-mode` event) - see `GEMINI-HANDOFF-overlay-contract.md`.

## Built-in browser
The **globe button** next to the gear (or **Ctrl+L** while the tablet is open) shows an address bar on the tablet's screen:
back, forward, reload / stop, home (your tac map), the address box, a shield with the number of ads and trackers blocked
(click it to switch blocking on/off), and a close button. Type a web address, or just words to search (DuckDuckGo;
change `SearchUrl=` in the ini). Video, audio and typing work like in a normal browser. The tac map is the home page
and opens first; "Home page URL..." in settings changes it.

Security (it is the same sandboxed Chromium engine Edge/Brave use, kept up to date by Windows, plus):
- http:// is upgraded to https:// (plain http only on your own network); sites with a bad certificate are refused
  with no "continue anyway"; Microsoft SmartScreen is on
- camera, microphone, location, notifications, clipboard and every other permission request is denied
- downloads, extra pop-up windows, file:// pages and links that launch other programs are blocked
- password saving and autofill are off; developer tools are off; pages can't talk to this program
- only YOUR tac map site is told it is inside the overlay (other sites never see it)
- ads and trackers: a built-in list of ad / tracker servers is blocked, and YouTube ads are skipped / hidden
  (best effort - YouTube changes things often). Put extra domains, one per line, in `blocklist.txt` next to the exe.
- Settings > "Clear browsing data" wipes history, cookies, cache and saved site logins.

## For the web page
The tablet screen is about **2.1 : 1 landscape** (ATAK) or **1.5 : 1** (pilot display). Design for a viewport of about
**1050 x 500 CSS px** (ATAK) / **1050 x 700** (pilot) (the default `PageWidth=1050`), keep it fluid, and make buttons at
least ~40 px tall. The overlay tells *your map site only* that it is inside the tablet:
```js
if (window.TAC_OVERLAY) { /* running inside the overlay; TAC_OVERLAY.layout is "tablet" or "mfd", .aspect the screen ratio */ }
// or in CSS:  html[data-overlay="tablet"] { ... }   html[data-overlay="mfd"] { ... }
```

## One-click updates (version 1.1.0 and newer)
When the app starts and a newer version exists, a window offers **Update now**: it downloads the new zip from your GitHub
release by itself, swaps the files in place (settings are kept) and restarts the app. "Later" carries on; with a
`minimum=` above their version the app won't run until they update. The tray menu and Settings > "Check for updates" do
the same. Only github.com addresses are trusted. To publish: attach a zip named `TacMapOverlay-v<version>.zip`
(the files directly inside it) to a GitHub Release tagged `v<version>`, THEN raise `latest=` in `version.txt`.
People still on 1.0.x have to download 1.1.0 by hand once; after that it is automatic.

## Updates (for whoever publishes the app)
On every start the app reads a small text file you host (see `version.txt.example`):
```
latest=1.2.0      newest version that exists  -> people get a "new version available" notice (tray + settings)
minimum=1.1.0     oldest version allowed       -> anything older is blocked with "Update required" until they update
url=https://github.com/OWNER/REPO/releases/latest   where the download is
notes=What's new (optional)
```
- To push an update: bump `Version` in `src\Updater.cs`, rebuild, upload the new files (e.g. a GitHub Release), then edit that
  text file. Set `minimum` = `latest` to force everyone to update; set it lower to only nag.
- If the file can't be reached (offline, hosting down) the app just starts normally - nobody gets locked out.
- Switch it on by putting the file's address in `DefaultInfoUrl` in `src\Updater.cs` and rebuilding.
  (`UpdateUrl=` in `TacOverlay.ini` overrides it, useful for testing.) Settings > "Check for updates" checks on demand.
- The update window and URL prompt open on the monitor the tablet uses.

## Notes
- The game must be in **Borderless Windowed** (or Windowed) mode. Windows can't draw over true exclusive fullscreen.
- A key bound without Ctrl/Alt/Shift (a plain letter, say) will not type in the game while the app runs. Ctrl+F1 and
  the like are safe. Mouse buttons can't be bound (that would need a system-wide input hook, which antivirus dislikes).
- If you alt-tab away (or anything else takes focus) while expanded, the tablet minimizes by itself, the mouse is released
  and your focus stays where you put it. Press the key again to bring it back.
- The tablet is drawn from a 774 px wide photo, so the frame is a little soft when large (the map itself is always sharp).
  The app works at 3x that size internally so themes and edges are smooth. For a sharper frame, replace `assets\device.png`
  with a bigger version of the same photo (same shape, no cropping - e.g. the original run through an AI upscaler such as
  Upscayl at 4x; up to ~3100 px wide is used) and rebuild. Nothing else needs changing.
- Rebuild with `powershell -ExecutionPolicy Bypass -File build.ps1`. Source is in `src\`.

## If your antivirus removes TacOverlay.exe
The exe is unsigned, and it does the same things crosshair-overlay apps do (global hotkeys, an always-on-top
window), so security software can flag it by heuristics. If it gets flagged, restore it from the antivirus quarantine
and allow it yourself, or build it locally with `build.ps1`. For wider sharing the proper fixes are a code-signing
certificate and submitting the file to the vendor as a false positive.
