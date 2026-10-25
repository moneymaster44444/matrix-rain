#requires -Version 5.1
# Build the release zips: the self-contained Windows MatrixRain.scr and the
# Steam Deck / Big Picture screensaver.
# Usage: .\release.ps1                  # defaults to v1.2.0
#        .\release.ps1 -Version v1.3.0
#
# Output, in the repo root (upload both as GitHub Release assets):
#   MatrixRain-<version>-win-x64.zip     MatrixRain.scr + README.txt + LICENSE.txt
#   MatrixRain-<version>-steamdeck.zip   SteamScreensaver/ (screensaver, install
#                                        scripts, README.txt, LICENSE.txt)

param(
    [string]$Version = 'v1.2.0'
)

$ErrorActionPreference = 'Stop'
$root       = $PSScriptRoot
$proj       = Join-Path $root 'MatrixRain'
$publishOut = Join-Path $proj 'bin\Release\net10.0-windows\win-x64\publish\MatrixRain.scr'
$staging    = Join-Path $root "release-staging-$Version"
$zipPath    = Join-Path $root "MatrixRain-$Version-win-x64.zip"
$steamZip   = Join-Path $root "MatrixRain-$Version-steamdeck.zip"
$steamSrc   = Join-Path $root 'SteamScreensaver'
$licenseSrc = Join-Path $root 'LICENSE'
# Windows' bundled tar writes zip entries with forward slashes; Compress-Archive
# in Windows PowerShell uses backslashes, which extract badly on SteamOS.
$tar        = Join-Path $env:SystemRoot 'System32\tar.exe'

Write-Host "[1/4] Publishing self-contained single-file build..." -ForegroundColor Cyan
Push-Location $proj
try {
    dotnet publish -c Release -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }
} finally {
    Pop-Location
}

if (-not (Test-Path $publishOut)) { throw "Publish output not found: $publishOut" }
if (-not (Test-Path $licenseSrc)) { throw "LICENSE not found at $licenseSrc -- pull origin/main first" }
if (-not (Test-Path $tar)) { throw "tar.exe not found at $tar (needs Windows 10 1803 or later)" }

Write-Host "[2/4] Creating Windows zip..." -ForegroundColor Cyan
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging | Out-Null

Copy-Item $publishOut (Join-Path $staging 'MatrixRain.scr')
Copy-Item $licenseSrc (Join-Path $staging 'LICENSE.txt')

# Bundled README that lives inside the zip alongside the .scr.
$header = "MatrixRain $Version"
$readme = @"
$header
$('=' * $header.Length)

A Matrix-style "digital rain" screensaver for Windows.
https://github.com/moneymaster44444/matrix-rain

INSTALL
-------
1. Right-click MatrixRain.scr and choose "Install".
   (You may see "Windows protected your PC" -- click "More info -> Run
    anyway" to proceed. The .scr is not code-signed.)
2. Screen Saver Settings opens with MatrixRain selected. Adjust the
   wait time and click OK.

For the screensaver to trigger on idle (and stay in the dropdown after
the dialog closes), the .scr must live in C:\Windows\System32\. From an
elevated PowerShell:

    Copy-Item .\MatrixRain.scr C:\Windows\System32\ -Force

Then re-select MatrixRain in Screen Saver Settings.

CONFIGURE
---------
After install, from any shell:

    MatrixRain.scr /c

opens the settings dialog (character set, density, speed, color, and
RGB mode, which gives every new drop its own random color).
Settings save to %APPDATA%\MatrixRain\config.json.

UNINSTALL
---------
Pick a different screensaver in Settings, then delete
C:\Windows\System32\MatrixRain.scr.

REQUIREMENTS
------------
- Windows 10 or 11 (64-bit)
- No .NET runtime install needed -- self-contained single-file build.

LICENSE
-------
MIT -- see LICENSE.txt.
"@

Set-Content -Path (Join-Path $staging 'README.txt') -Value $readme -Encoding ASCII

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zipPath -CompressionLevel Optimal

# Staging dir served its purpose -- remove so the repo root stays clean.
Remove-Item $staging -Recurse -Force

Write-Host "[3/4] Staging Steam Deck zip contents..." -ForegroundColor Cyan
$steamStaging = Join-Path $root "release-staging-$Version-steamdeck"
if (Test-Path $steamStaging) { Remove-Item $steamStaging -Recurse -Force }
New-Item -ItemType Directory -Path $steamStaging | Out-Null
$steamDir = Join-Path $steamStaging 'SteamScreensaver'
Copy-Item $steamSrc $steamDir -Recurse
Copy-Item $licenseSrc (Join-Path $steamDir 'LICENSE.txt')

$steamHeader = "MatrixRain $Version for Steam Deck / Big Picture"
$steamReadme = @"
$steamHeader
$('=' * $steamHeader.Length)

A Matrix-style "digital rain" screensaver for Steam Deck (Game Mode,
handheld or docked) and Steam Big Picture.
https://github.com/moneymaster44444/matrix-rain

INSTALL ON STEAM DECK
---------------------
1. Switch to Desktop Mode (Steam button -> Power -> Switch to Desktop).
2. Copy this zip to Downloads, right-click it, and choose
   Extract -> Extract archive here.
3. Open Konsole and run:

       bash ~/Downloads/SteamScreensaver/install.sh

4. Return to Gaming Mode, then Settings -> Customization -> Screensaver
   and pick "matrix-rain (User Created)". If it isn't listed, restart
   Steam.

INSTALL FOR WINDOWS BIG PICTURE
-------------------------------
From PowerShell in the extracted folder:

    powershell -ExecutionPolicy Bypass -File .\SteamScreensaver\install.ps1

SETTINGS
--------
Edit config.js in the installed folder:

    ~/.steam/root/config/uioverrides/screensavers/matrix-rain/config.js

Each setting is commented with its range. Changes apply the next time the
screensaver starts. Reinstalling keeps your config.js; the installer also
writes config.example.js with every current setting and lists any your
config.js doesn't set yet.

LICENSE
-------
MIT -- see LICENSE.txt.
"@
Set-Content -Path (Join-Path $steamDir 'README.txt') -Value $steamReadme -Encoding ASCII

Write-Host "[4/4] Creating Steam Deck zip..." -ForegroundColor Cyan
if (Test-Path $steamZip) { Remove-Item $steamZip -Force }
& $tar -a -c -f $steamZip -C $steamStaging SteamScreensaver
if ($LASTEXITCODE -ne 0) { throw "tar failed (exit $LASTEXITCODE)" }
Remove-Item $steamStaging -Recurse -Force

Write-Host ''
Write-Host 'Release zips ready:' -ForegroundColor Green
foreach ($zip in @($zipPath, $steamZip)) {
    $info = Get-Item $zip
    $size = if ($info.Length -ge 1MB) { "$([math]::Round($info.Length / 1MB, 1)) MB" } else { "$([math]::Round($info.Length / 1KB)) KB" }
    Write-Host "  $($info.FullName)  ($size)"
}
Write-Host ''
Write-Host 'Next steps:' -ForegroundColor Yellow
Write-Host "  git tag -a $Version -m `"$Version`""
Write-Host "  git push origin $Version"
Write-Host '  GitHub -> Releases -> Draft new release -> select the tag, attach both zips'
