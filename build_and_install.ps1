
[CmdletBinding()]
param(
    [string]$CosmoteerBin  = $env:COSMOTEER_BIN,
    [string]$YamlModDir    = $env:YAML_MOD_DIR,
    [string]$ModInstallDir = ''
)

if (-not $CosmoteerBin) { $CosmoteerBin = 'D:\SteamLibrary\steamapps\common\Cosmoteer\Bin' }
if (-not $YamlModDir)   { $YamlModDir   = 'D:\SteamLibrary\steamapps\workshop\content\799600\3577650065' }

$ErrorActionPreference = 'Stop'
$script:here = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $script:here

if (-not $ModInstallDir) {
    $savedGamesRoot = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Saved Games\Cosmoteer'
    $pickedUserDir = $null
    if (Test-Path $savedGamesRoot) {
        $pickedUserDir = Get-ChildItem -Path $savedGamesRoot -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^\d+$' } |
            Select-Object -First 1
    }
    if ($pickedUserDir) {
        $ModInstallDir = Join-Path $pickedUserDir.FullName 'Mods\ztx.hinges_pistons_docking'
    } else {
        $cosmoRoot = Split-Path -Parent $CosmoteerBin
        $ModInstallDir = Join-Path $cosmoRoot 'Mods\ztx.hinges_pistons_docking'
        Write-Warning "No Saved Games profile found; falling back to game-install Mods dir."
    }
}

Write-Host "=== ZTX Hinges, Pistons and Docking build + install ===" -ForegroundColor Cyan
Write-Host "CosmoteerBin:   $CosmoteerBin"
Write-Host "YamlModDir:     $YamlModDir"
Write-Host "ModInstallDir:  $ModInstallDir"
Write-Host ''

foreach ($p in @(
    (Join-Path $CosmoteerBin 'Cosmoteer.dll'),
    (Join-Path $CosmoteerBin 'HalflingCore.dll'),
    (Join-Path $YamlModDir   '0Harmony.dll')
)) {
    if (-not (Test-Path $p)) {
        throw "Required file not found: $p (adjust -CosmoteerBin / -YamlModDir)"
    }
}

$logVer = (Select-String -Path (Join-Path $script:here 'Core\Log.cs') `
    -Pattern 'VERSION\s*=\s*"v([0-9.]+)"').Matches[0].Groups[1].Value
$modVer = (Select-String -Path (Join-Path $script:here 'mod.rules.template') `
    -Pattern '^\s*Version\s*=\s*([0-9.]+)').Matches[0].Groups[1].Value
if ($logVer -ne $modVer) {
    throw "Version mismatch: Log.cs says v$logVer, mod.rules.template says $modVer. Sync them."
}
Write-Host "Version check OK: $modVer" -ForegroundColor DarkGray

Write-Host 'Building (dotnet build -c Release)...' -ForegroundColor Yellow
& dotnet build `
    -c Release `
    -p:CosmoteerBin="$CosmoteerBin" `
    -p:YamlModDir="$YamlModDir"
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

$dll = Join-Path $env:LOCALAPPDATA 'ZTX.HingesPistonsDocking.Build\bin\Release\net10.0\ZTX.HingesPistonsDocking.dll'
if (-not (Test-Path $dll)) {
    throw "Build succeeded but output DLL not found at: $dll"
}

if (-not (Test-Path $ModInstallDir)) {
    New-Item -ItemType Directory -Path $ModInstallDir -Force | Out-Null
}

Write-Host 'Copying install files...' -ForegroundColor Yellow
Copy-Item -Force (Join-Path $script:here 'mod.rules.template') (Join-Path $ModInstallDir 'mod.rules')

Write-Host 'Copying DLL...' -ForegroundColor Yellow
$dllDest = Join-Path $ModInstallDir 'ZTX.HingesPistonsDocking.dll'
try {
    Copy-Item -Force $dll $dllDest
}
catch {
    Write-Warning "DLL copy failed (is Cosmoteer running? Close it and rerun)."
    Write-Warning "  $_"
    Write-Warning "mod.rules DID update -- only the DLL was skipped."
}

Write-Host ''
Write-Host "Done." -ForegroundColor Green
Write-Host "Next:" -ForegroundColor Cyan
Write-Host "  1. Launch Cosmoteer."
Write-Host "  2. Mods menu -> enable 'Hinges, Pistons and Docking'. Trust the DLL when YAML asks."
Write-Host "  3. Restart the game. Look for '[ZTX.HingesDock]' lines in"
Write-Host "     <Saved Games>\Cosmoteer\<id>\Logs\log *.txt"
