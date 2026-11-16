# Security

## What this program does and doesn't do
See [PRIVACY-AND-SECURITY.txt](PRIVACY-AND-SECURITY.txt): no admin rights, no installer, no keyboard hooks, no data collection,
and exactly which servers it talks to.

## Verifying a download
Every release lists the SHA-256 of the zip and a VirusTotal link in its notes.
In PowerShell: `Get-FileHash .\TacMapOverlay-v<version>.zip` must match.
You can also build it yourself from this source (`build.ps1`).

## Reporting a security problem
Please open a **private** report: this repository's **Security** tab > **Report a vulnerability**
(or open an issue that says "security" without details, and we will contact you).
Include the version (Settings window title) and, if you can, the file `tacoverlay.log` (Settings > Open log folder).
