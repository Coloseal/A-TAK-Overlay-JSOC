# Makes everything for a release in one go:
#   powershell -ExecutionPolicy Bypass -File tools\release.ps1
# - builds the program, makes TacMapOverlay-v<version>.zip (program + WebView2 + LICENSE + privacy note)
# - prints its SHA-256 and writes release-notes.md (to paste into the GitHub release) and version.txt.new (to paste into version.txt)
# Order when publishing:  1) publish the GitHub release with the zip   2) THEN edit version.txt on GitHub.
param([string]$Notes = 'What changed in this version (one line)', [string]$Minimum = '1.0.0')
$ErrorActionPreference = 'Stop'
# version.txt is read by every installed copy, so refuse anything that would break it. (Double quotes in -Notes can split
# the command line and push words into -Minimum.)
if ($Minimum -notmatch '^\d+\.\d+\.\d+$') { throw "-Minimum must look like 1.0.0 but is '$Minimum'. If your notes contain double quotes, remove them." }
$Notes = ($Notes -replace '[\r\n]+', ' ').Replace('"', "'")
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $root
Add-Type -AssemblyName System.IO.Compression.FileSystem

powershell -ExecutionPolicy Bypass -File "$root\build.ps1"
if ($LASTEXITCODE -ne 0) { throw 'build failed' }

$version = (Get-Item "$root\dist\TacOverlay.exe").VersionInfo.FileVersion -replace '\.0$', ''
$zipName = "TacMapOverlay-v$version.zip"
$zip = Join-Path $root $zipName
Remove-Item $zip -ErrorAction SilentlyContinue
$stage = Join-Path $env:TEMP 'tac_release_stage'
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $stage | Out-Null
foreach ($f in 'TacOverlay.exe', 'Microsoft.Web.WebView2.Core.dll', 'WebView2Loader.dll', 'LICENSE.txt', 'THIRD-PARTY-NOTICES.txt', 'PRIVACY-AND-SECURITY.txt') {
    if (Test-Path "$root\dist\$f") { Copy-Item "$root\dist\$f" $stage }
}
# optional sound effects for the game panel (assets\sounds\*.mp3 -> sounds\ next to the program)
if (Test-Path "$root\assets\sounds") { Copy-Item "$root\assets\sounds" "$stage\sounds" -Recurse }
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)
Remove-Item $stage -Recurse -Force

# sanity: unpack and compare the program inside with the build
$chk = Join-Path $env:TEMP 'tac_release_check'
Remove-Item $chk -Recurse -Force -ErrorAction SilentlyContinue
[IO.Compression.ZipFile]::ExtractToDirectory($zip, $chk)
if ((Get-FileHash "$chk\TacOverlay.exe").Hash -ne (Get-FileHash "$root\dist\TacOverlay.exe").Hash) { throw 'zip does not match the build' }
Remove-Item $chk -Recurse -Force

$sha = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
$exeSha = (Get-FileHash "$root\dist\TacOverlay.exe" -Algorithm SHA256).Hash.ToLower()
$repo = 'Coloseal/A-TAK-Overlay-JSOC'

@"
latest=$version
minimum=$Minimum
url=https://github.com/$repo/releases/latest
notes=$Notes
sha256=$sha
"@ | Set-Content "$root\version.txt.new" -Encoding ascii

@"
## Tac Map Overlay v$version

$Notes

### Verify your download
| File | SHA-256 |
|---|---|
| ``$zipName`` | ``$sha`` |
| ``TacOverlay.exe`` (inside) | ``$exeSha`` |

PowerShell:  ``Get-FileHash .\$zipName``   (it must match the line above)

- VirusTotal scan of this exact file: **<paste the VirusTotal link here>**
- The program is open source (MIT). It needs no admin rights, installs nothing, has no keyboard hooks and sends no data about you.
  Read ``PRIVACY-AND-SECURITY.txt`` (inside the zip) for exactly what it does.
- Windows may warn about a new, unsigned program. That is about reputation, not behaviour. Right-click the zip > Properties >
  Unblock, then extract. If SmartScreen shows: More info > Run anyway. Never turn off your antivirus for this.
- Already installed? Version 1.1.0+ updates itself: press **Update now** when it asks.
"@ | Set-Content "$root\release-notes.md" -Encoding utf8

Write-Host ""
Write-Host "Version : $version"
Write-Host "Zip     : $zip"
Write-Host "SHA-256 : $sha"
Write-Host "Wrote   : release-notes.md (GitHub release text)  and  version.txt.new (paste into version.txt AFTER the release is published)"
