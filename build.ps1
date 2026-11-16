# Builds dist\TacOverlay.exe with the C# compiler that ships with Windows (no SDK needed).
#   powershell -ExecutionPolicy Bypass -File build.ps1
# The only outside files are Microsoft's two WebView2 DLLs (Microsoft-signed). If lib\ does not have them they are taken
# from an Office install if there is one, otherwise downloaded from the official NuGet package (the same version).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root 'dist'
$lib  = Join-Path $root 'lib'
$csc  = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wvVersion = '1.0.2365.46'

New-Item -ItemType Directory -Force $dist, $lib | Out-Null

function Get-WebView2Dlls {
    $need = 'Microsoft.Web.WebView2.Core.dll', 'WebView2Loader.dll'
    if (-not ($need | Where-Object { -not (Test-Path (Join-Path $lib $_)) })) { return }
    $office = 'C:\Program Files\Microsoft Office\root\Office16\ADDINS\Microsoft Power Query for Excel Integrated\bin'
    if (-not $env:TACOVERLAY_NO_OFFICE -and (Test-Path (Join-Path $office $need[0]))) {
        foreach ($f in $need) { Copy-Item (Join-Path $office $f) $lib -Force }
        return
    }
    Write-Host "Downloading Microsoft.Web.WebView2 $wvVersion from NuGet ..."
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $pk = Join-Path $env:TEMP "webview2.$wvVersion.nupkg"
    Invoke-WebRequest -UseBasicParsing "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$wvVersion/microsoft.web.webview2.$wvVersion.nupkg" -OutFile $pk
    $z = [IO.Compression.ZipFile]::OpenRead($pk)
    try {
        $map = @{ 'lib/net45/Microsoft.Web.WebView2.Core.dll' = 'Microsoft.Web.WebView2.Core.dll'; 'runtimes/win-x64/native/WebView2Loader.dll' = 'WebView2Loader.dll' }
        foreach ($k in $map.Keys) { [IO.Compression.ZipFileExtensions]::ExtractToFile($z.GetEntry($k), (Join-Path $lib $map[$k]), $true) }
    } finally { $z.Dispose() }
}
Get-WebView2Dlls

# The official releases use photos that are not in the public source. If any picture is missing, make simple stand-ins.
foreach ($a in 'device.png', 'mfd.png', 'emblem.png', 'icon.ico') {
    if (-not (Test-Path (Join-Path $root "assets\$a"))) { & "$root\tools\make-placeholder-art.ps1" -Root $root; break }
}

& $csc /nologo /target:winexe /platform:x64 /optimize+ /unsafe `
    /out:"$dist\TacOverlay.exe" `
    /win32icon:"$root\assets\icon.ico" `
    /win32manifest:"$root\assets\app.manifest" `
    /resource:"$root\assets\device.png,TacOverlay.device.png" `
    /resource:"$root\assets\emblem.png,TacOverlay.emblem.png" `
    /resource:"$root\assets\mfd.png,TacOverlay.mfd.png" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:"$lib\Microsoft.Web.WebView2.Core.dll" `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Xaml.dll /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll" /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll" `
    "$root\src\Program.cs" "$root\src\Themes.cs" "$root\src\SettingsForm.cs" "$root\src\Updater.cs" "$root\src\Browser.cs" "$root\src\Panels.cs" "$root\src\Tools.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

Copy-Item "$lib\*.dll" $dist -Force
foreach ($f in 'LICENSE.txt', 'THIRD-PARTY-NOTICES.txt', 'PRIVACY-AND-SECURITY.txt') { if (Test-Path (Join-Path $root $f)) { Copy-Item (Join-Path $root $f) $dist -Force } }
Write-Host "Built: $dist\TacOverlay.exe"
