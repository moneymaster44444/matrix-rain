#requires -RunAsAdministrator
# Build, publish, and copy to System32 in one step.
# Run from an elevated PowerShell.

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$proj = Join-Path $root 'MatrixRain'
$out  = Join-Path $proj 'bin\Release\net9.0-windows\win-x64\publish\MatrixRain.scr'
$dest = 'C:\Windows\System32\MatrixRain.scr'

Write-Host '[1/2] Publishing self-contained single-file build...' -ForegroundColor Cyan
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

if (-not (Test-Path $out)) { throw "Publish output not found: $out" }

Write-Host "[2/2] Copying to $dest..." -ForegroundColor Cyan
Copy-Item -Path $out -Destination $dest -Force

$installed = Get-Item $dest
Write-Host ''
Write-Host 'Installed:' -ForegroundColor Green
Write-Host "  $($installed.FullName)"
Write-Host "  $([math]::Round($installed.Length / 1MB, 1)) MB at $($installed.LastWriteTime)"
Write-Host ''
Write-Host 'Open Screen Saver Settings to verify selection:' -ForegroundColor Yellow
Write-Host '  control desk.cpl,,@screensaver'
