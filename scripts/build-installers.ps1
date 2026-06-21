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
    [switch]$SkipPublish,

    # --- Code signing (AX-QA-001 / AX-QA-007) ---
    # Authenticode-sign the app binaries and installers. Supply EITHER a cert-store thumbprint
    # (preferred — no secret on the command line) OR a PFX path + password. With neither, the
    # build is UNSIGNED and prints a loud warning; pass -RequireSign to make that a hard error
    # (use this in the real release pipeline so an unsigned asset can never be produced).
    [string]$CertificateThumbprint,
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [switch]$RequireSign
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

# ── Code signing (AX-QA-001 / AX-QA-007) ───────────────────────────────────────────────────
$signingConfigured = -not [string]::IsNullOrWhiteSpace($CertificateThumbprint) -or
                     -not [string]::IsNullOrWhiteSpace($CertificatePath)

function Find-SignTool {
    $candidates = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } |
        Sort-Object FullName -Descending
    if ($candidates) { return $candidates[0].FullName }
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw "signtool.exe not found. Install the Windows SDK signing tools or add signtool.exe to PATH."
}

function Invoke-Sign {
    param([Parameter(Mandatory)][string[]]$Files)

    $signtool = Find-SignTool
    $signArgs = @('sign', '/fd', 'SHA256', '/tr', $TimestampUrl, '/td', 'SHA256', '/v')
    if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        $signArgs += @('/sha1', $CertificateThumbprint)
    } else {
        if (-not (Test-Path $CertificatePath)) { throw "Certificate file not found: $CertificatePath" }
        $signArgs += @('/f', $CertificatePath)
        if (-not [string]::IsNullOrWhiteSpace($CertificatePassword)) { $signArgs += @('/p', $CertificatePassword) }
    }

    foreach ($file in $Files) {
        & $signtool @signArgs $file
        if ($LASTEXITCODE -ne 0) { throw "signtool failed for $file (exit $LASTEXITCODE)." }
    }
}

function Confirm-Signature {
    param([Parameter(Mandatory)][string[]]$Files)
    foreach ($file in $Files) {
        $sig = Get-AuthenticodeSignature -FilePath $file
        Write-Host ("  {0}: {1}" -f (Split-Path $file -Leaf), $sig.Status)
        if ($sig.Status -ne 'Valid') { throw "Signature verification failed for $file (status: $($sig.Status))." }
    }
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

# 1b. Provenance gate (AX-QA-001): prove the PUBLISHED artifact actually contains the security
# remediation. The public v2.1.1 asset was built from stale source and shipped without it; this
# check fails the build if the security types are absent from the freshly published Core DLL.
$coreDll = Join-Path $publishDir "AgentX.Core.dll"
if (-not (Test-Path $coreDll)) { throw "Provenance check: $coreDll not found — publish incomplete." }
$dllText = [IO.File]::ReadAllText($coreDll, [Text.Encoding]::Latin1)
foreach ($type in @('LocalApiSecurity', 'ResolveContainedPath')) {
    if (-not $dllText.Contains($type)) {
        throw "Provenance check FAILED: '$type' absent from published AgentX.Core.dll. This build does not contain the security remediation (AX-QA-001) — do not ship it."
    }
}
$headCommit = (& git -C $projectRoot rev-parse HEAD 2>$null)
Write-Host "Provenance OK: security types present in published Core DLL (commit $headCommit)."

# 1c. Sign the application binaries before packaging (AX-QA-007).
$appExe = Join-Path $publishDir "AgentX.exe"
if ($signingConfigured) {
    Write-Host "`nSigning application binaries..."
    Invoke-Sign -Files @($appExe)
} elseif ($RequireSign) {
    throw "-RequireSign was set but no signing certificate was provided. Pass -CertificateThumbprint or -CertificatePath."
} else {
    Write-Warning "UNSIGNED BUILD: no signing certificate provided. Authenticode signatures are required for a public release (SmartScreen/trust). Pass -CertificateThumbprint (preferred) or -CertificatePath, or -RequireSign to enforce."
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

# 3. Sign installers, verify every signature, and record provenance hashes (AX-QA-001 / 007).
$outputDir = Join-Path $projectRoot "installer-output"
$installers = @(Get-ChildItem $outputDir -Filter "AgentX-Setup-*.exe" -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty FullName)

if ($signingConfigured -and $installers.Count -gt 0) {
    Write-Host "`nSigning installers..."
    Invoke-Sign -Files $installers
    Write-Host "`nVerifying Authenticode signatures..."
    Confirm-Signature -Files (@($appExe) + $installers)
}

# SHA-256 sums + source commit so the published asset can be proven to match HEAD.
if ($installers.Count -gt 0) {
    $sumsPath = Join-Path $outputDir "SHA256SUMS.txt"
    $lines = @("# Agent-X installers - source commit $headCommit",
               "# Generated by scripts/build-installers.ps1")
    $lines += $installers | ForEach-Object {
        "{0}  {1}" -f (Get-FileHash $_ -Algorithm SHA256).Hash.ToLower(), (Split-Path $_ -Leaf)
    }
    Set-Content -Path $sumsPath -Value $lines -Encoding ascii
    Write-Host "`nWrote $sumsPath"
}

Write-Host "`nDone. Output in installer-output\:"
Get-ChildItem $outputDir -Filter "AgentX-Setup-*.exe" -ErrorAction SilentlyContinue |
    ForEach-Object { Write-Host ("  {0}  ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB)) }
if (-not $signingConfigured) {
    Write-Warning "Reminder: this build is UNSIGNED and must not be published. See docs/RELEASE-SIGNING.md."
}
