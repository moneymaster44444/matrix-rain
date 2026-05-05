#requires -Version 5.1
# Build a release-ready zip of the self-contained MatrixRain.scr.
# Usage: .\release.ps1                  # defaults to v1.0.0
#        .\release.ps1 -Version v1.1.0
#
# Output: MatrixRain-<version>-win-x64.zip in the repo root, containing
#         MatrixRain.scr + README.txt + LICENSE.txt. Upload that zip as a
#         GitHub Release asset.

param(
    [string]$Version = 'v1.0.0'
)

$ErrorActionPreference = 'Stop'
$root       = $PSScriptRoot
$proj       = Join-Path $root 'MatrixRain'
$publishOut = Join-Path $proj 'bin\Release\net9.0-windows\win-x64\publish\MatrixRain.scr'
$staging    = Join-Path $root "release-staging-$Version"
$zipPath    = Join-Path $root "MatrixRain-$Version-win-x64.zip"
$licenseSrc = Join-Path $root 'LICENSE'

Write-Host "[1/3] Publishing self-contained single-file build..." -ForegroundColor Cyan
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

Write-Host "[2/3] Staging zip contents..." -ForegroundColor Cyan
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

opens the settings dialog (character set, density, speed, color).
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

Write-Host "[3/3] Creating zip..." -ForegroundColor Cyan
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zipPath -CompressionLevel Optimal

# Staging dir served its purpose -- remove so the repo root stays clean.
Remove-Item $staging -Recurse -Force

$zipInfo = Get-Item $zipPath
Write-Host ''
Write-Host 'Release zip ready:' -ForegroundColor Green
Write-Host "  $($zipInfo.FullName)"
Write-Host "  $([math]::Round($zipInfo.Length / 1MB, 1)) MB"
Write-Host ''
Write-Host 'Next steps:' -ForegroundColor Yellow
Write-Host "  git tag -a $Version -m `"$Version`""
Write-Host "  git push origin $Version"
Write-Host '  GitHub -> Releases -> Draft new release -> select the tag, attach the zip'
