<#
.SYNOPSIS
    Publishes Agent-X and compiles the SLIM and/or OFFLINE Inno Setup installers.

.DESCRIPTION
    One script, two installer profiles (selected by Inno's AgentXOffline flag):
      SLIM     -> installer-output\AgentX-Setup-<ver>-x64.exe          (~180 MB, GitHub asset)
      OFFLINE  -> installer-output\AgentX-Setup-<ver>-x64-offline.exe   (~2 GB, hosted on R2)

    The SLIM installer omits the model (the app downloads it on first run). The OFFLINE installer
    bundles models\llama-3.2-3b-instruct-q4_k_m.gguf; run scripts/download-model.ps1 first if it
    is missing.

.PARAMETER Profiles
    Which installers to build: 'slim', 'offline', or 'both' (default).

.PARAMETER SkipPublish
    Reuse an existing publish\win-x64 instead of re-publishing.

.EXAMPLE
    ./build-installers.ps1                       # publish + build both
    ./build-installers.ps1 -Profiles slim        # GitHub asset only
    ./build-installers.ps1 -Profiles offline     # R2 asset only (needs the model in models\)
#>

[CmdletBinding()]
param(
    [ValidateSet('slim', 'offline', 'both')]
    [string]$Profiles = 'both',
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$issPath = Join-Path $projectRoot "installer\AgentX-Setup.iss"
$publishDir = Join-Path $projectRoot "publish\win-x64"
$appProject = Join-Path $projectRoot "src\AgentX.App\AgentX.App.csproj"
$modelFile = Join-Path $projectRoot "models\llama-3.2-3b-instruct-q4_k_m.gguf"

function Find-Iscc {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno\ISCC.exe"),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw "ISCC.exe (Inno Setup 6) not found. Install Inno Setup 6 or add ISCC.exe to PATH."
}

# 1. Publish the self-contained, unpackaged WinUI app.
if (-not $SkipPublish) {
    Write-Host "Publishing Agent-X (Release, win-x64, self-contained)..."
    & dotnet publish $appProject -c Release -r win-x64 --self-contained true `
        -p:Platform=x64 -p:WindowsPackageType=None -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }
} elseif (-not (Test-Path $publishDir)) {
    throw "-SkipPublish set but $publishDir does not exist."
}

$iscc = Find-Iscc
Write-Host "Using ISCC: $iscc"

$buildSlim = $Profiles -in @('slim', 'both')
$buildOffline = $Profiles -in @('offline', 'both')

# 2a. SLIM installer (no model bundled).
if ($buildSlim) {
    Write-Host "`nBuilding SLIM installer..."
    & $iscc $issPath
    if ($LASTEXITCODE -ne 0) { throw "SLIM installer build failed (exit $LASTEXITCODE)." }
}

# 2b. OFFLINE installer (model bundled). Requires the GGUF to be present.
if ($buildOffline) {
    if (-not (Test-Path $modelFile)) {
        throw "OFFLINE build needs the model at $modelFile. Run scripts/download-model.ps1 first."
    }
    Write-Host "`nBuilding OFFLINE installer (bundling the model)..."
    & $iscc "/DAgentXOffline=1" $issPath
    if ($LASTEXITCODE -ne 0) { throw "OFFLINE installer build failed (exit $LASTEXITCODE)." }
}

Write-Host "`nDone. Output in installer-output\:"
Get-ChildItem (Join-Path $projectRoot "installer-output") -Filter "AgentX-Setup-*.exe" -ErrorAction SilentlyContinue |
    ForEach-Object { Write-Host ("  {0}  ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB)) }
