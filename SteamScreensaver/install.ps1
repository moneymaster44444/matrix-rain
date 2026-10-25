# Copy the Steam screensaver into Steam's uioverrides folder (Windows).
# Usage:  .\install.ps1 [-SteamPath 'D:\Steam']
# An existing config.js is kept, so your settings survive a reinstall. The
# current template is always written next to it as config.example.js, and any
# settings your config.js doesn't set yet are listed at the end.

param(
    [string]$SteamPath
)

$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'matrix-rain'

function Find-SteamPath {
    $candidates = @()
    $reg = Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -Name SteamPath -ErrorAction SilentlyContinue
    if ($reg) { $candidates += $reg.SteamPath }
    $reg = Get-ItemProperty -Path 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam' -Name InstallPath -ErrorAction SilentlyContinue
    if ($reg) { $candidates += $reg.InstallPath }
    $candidates += "${env:ProgramFiles(x86)}\Steam"
    foreach ($c in $candidates) {
        if ($c -and (Test-Path (Join-Path $c 'steam.exe'))) { return (Resolve-Path $c).Path }
    }
    return $null
}

if (-not $SteamPath) { $SteamPath = Find-SteamPath }
if (-not $SteamPath -or -not (Test-Path $SteamPath)) {
    throw 'Steam not found. Pass its folder explicitly: .\install.ps1 -SteamPath "D:\Steam"'
}

$dest = Join-Path $SteamPath 'config\uioverrides\screensavers\matrix-rain'

# Steam usually makes its folder user-writable; only elevate if it isn't.
$canWrite = $true
try {
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    $probe = Join-Path $dest '.write-test'
    [IO.File]::WriteAllText($probe, '')
    Remove-Item $probe -Force
} catch {
    $canWrite = $false
}

if (-not $canWrite) {
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    if ($isAdmin) { throw "Cannot write to $dest" }
    Write-Host "No write access to $dest; relaunching elevated..." -ForegroundColor Yellow
    $argList = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -SteamPath `"$SteamPath`""
    Start-Process powershell -Verb RunAs -ArgumentList $argList -Wait
    exit
}

Write-Host "Installing to $dest" -ForegroundColor Cyan
foreach ($item in Get-ChildItem -Path $src -Recurse -File) {
    $rel = $item.FullName.Substring($src.Length + 1)
    $target = Join-Path $dest $rel
    if ($rel -eq 'config.js' -and (Test-Path $target)) {
        Write-Host '  keeping existing config.js'
        continue
    }
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    Copy-Item -Path $item.FullName -Destination $target -Force
    Write-Host "  $rel"
}
Copy-Item -Path (Join-Path $src 'config.js') -Destination (Join-Path $dest 'config.example.js') -Force
Write-Host '  config.example.js (reference copy of every setting)'

# Settings added since your config.js was written fall back to their defaults
# until you add them; list them so upgrades don't hide new options.
$keyPattern = '^\s*([A-Za-z]+)\s*:'
$have = @(Select-String -Path (Join-Path $dest 'config.js') -Pattern $keyPattern |
    ForEach-Object { $_.Matches[0].Groups[1].Value })
$missing = @(Select-String -Path (Join-Path $src 'config.js') -Pattern $keyPattern |
    ForEach-Object { $_.Matches[0].Groups[1].Value } |
    Where-Object { $have -notcontains $_ })

Write-Host ''
Write-Host 'Installed.' -ForegroundColor Green
Write-Host 'In Big Picture: Settings > Customization > Screensaver, pick "matrix-rain (User Created)".'
Write-Host "Edit settings in: $(Join-Path $dest 'config.js')"
if ($missing.Count -gt 0) {
    Write-Host ''
    Write-Host "Your config.js doesn't set: $($missing -join ' ')" -ForegroundColor Yellow
    Write-Host 'They use their defaults. Copy them from config.example.js to change them.'
}
