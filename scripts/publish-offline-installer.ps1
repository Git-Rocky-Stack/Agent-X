<#
.SYNOPSIS
    Uploads the OFFLINE Agent-X installer (model-bundled, ~2 GB) to Cloudflare R2 via rclone.

.DESCRIPTION
    The OFFLINE installer exceeds GitHub Releases' 2 GiB per-asset limit, so it is hosted on
    Cloudflare R2 and linked from the release notes (the SLIM installer is the GitHub asset).

    Wrangler's `r2 object put` is capped at 315 MB and is therefore NOT usable here — Cloudflare
    recommends an S3-compatible tool with multipart support. This script uses **rclone** (auto
    multipart, resumable) against R2's S3 endpoint.

    Credentials are read from the environment — nothing secret is stored in the repo. Create an
    R2 API token in the dashboard (R2 -> Manage R2 API Tokens -> Create, Object Read & Write),
    which yields the S3 Access Key ID + Secret, then set:
      R2_ACCESS_KEY_ID        S3 Access Key ID from the R2 API token.
      R2_SECRET_ACCESS_KEY    S3 Secret Access Key from the R2 API token.
      CLOUDFLARE_ACCOUNT_ID   Account id (forms the S3 endpoint https://<id>.r2.cloudflarestorage.com).

    Requires rclone (https://rclone.org). The bucket itself is created with wrangler:
      wrangler r2 bucket create agentx-releases
      wrangler r2 bucket dev-url enable agentx-releases   # public r2.dev URL

.PARAMETER Version
    Version string used in the object key. Defaults to 2.1.1.

.PARAMETER Bucket
    R2 bucket name. Defaults to env AGENTX_R2_BUCKET or "agentx-releases".

.PARAMETER PublicBaseUrl
    Public base URL of the bucket (a custom domain or the r2.dev URL), used to print the final
    link. Defaults to env AGENTX_R2_PUBLIC_BASE_URL, then to the production custom domain
    https://downloads.strategia-x.com (connected to the agentx-releases bucket).

.PARAMETER InstallerPath
    Path to the offline installer. Defaults to the standard build output for this version.

.PARAMETER DryRun
    Print the planned action without uploading.

.EXAMPLE
    $env:R2_ACCESS_KEY_ID      = '...'
    $env:R2_SECRET_ACCESS_KEY  = '...'
    $env:CLOUDFLARE_ACCOUNT_ID = '0d75974a4b80a0be4800c64715d4f1f5'
    ./publish-offline-installer.ps1 -Version 2.1.1   # prints https://downloads.strategia-x.com/v2.1.1/...
#>

[CmdletBinding()]
param(
    [string]$Version = "2.1.1",
    [string]$Bucket = $(if ($env:AGENTX_R2_BUCKET) { $env:AGENTX_R2_BUCKET } else { "agentx-releases" }),
    [string]$PublicBaseUrl = $(if ($env:AGENTX_R2_PUBLIC_BASE_URL) { $env:AGENTX_R2_PUBLIC_BASE_URL } else { "https://downloads.strategia-x.com" }),
    [string]$InstallerPath,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir

if (-not $InstallerPath) {
    $InstallerPath = Join-Path $projectRoot "installer-output\AgentX-Setup-$Version-x64-offline.exe"
}

if (-not (Test-Path $InstallerPath)) {
    Write-Error "Offline installer not found: $InstallerPath`nBuild it first: scripts/build-installers.ps1 -Profiles offline"
    exit 1
}

if (-not (Get-Command rclone -ErrorAction SilentlyContinue)) {
    Write-Error "rclone not found. Install it (https://rclone.org/downloads/ or 'winget install Rclone.Rclone')."
    exit 1
}

$missing = @()
foreach ($v in 'R2_ACCESS_KEY_ID', 'R2_SECRET_ACCESS_KEY', 'CLOUDFLARE_ACCOUNT_ID') {
    if (-not (Get-Item "env:$v" -ErrorAction SilentlyContinue)) { $missing += $v }
}
if ($missing.Count -gt 0) {
    Write-Error ("Set these env vars first (from an R2 API token): " + ($missing -join ', ') + @"

  Cloudflare dashboard -> R2 -> Manage R2 API Tokens -> Create API token (Object Read & Write).
  The token page shows the S3 Access Key ID + Secret Access Key and the endpoint.
"@)
    exit 1
}

$fileSizeGB = [math]::Round((Get-Item $InstallerPath).Length / 1GB, 2)
$objectKey = "v$Version/AgentX-Setup-$Version-x64-offline.exe"
$endpoint = "https://$($env:CLOUDFLARE_ACCOUNT_ID).r2.cloudflarestorage.com"

Write-Host ""
Write-Host "Agent-X OFFLINE installer -> Cloudflare R2 (rclone, multipart)"
Write-Host "==============================================================="
Write-Host "File:        $InstallerPath ($fileSizeGB GB)"
Write-Host "Bucket:      $Bucket"
Write-Host "Object key:  $objectKey"
Write-Host "Endpoint:    $endpoint"
Write-Host ""

# rclone on-the-fly :s3: remote — no config file, credentials passed as flags from env.
$rcloneArgs = @(
    'copyto', $InstallerPath, ":s3:$Bucket/$objectKey",
    '--s3-provider', 'Cloudflare',
    '--s3-access-key-id', $env:R2_ACCESS_KEY_ID,
    '--s3-secret-access-key', $env:R2_SECRET_ACCESS_KEY,
    '--s3-endpoint', $endpoint,
    '--s3-no-check-bucket',
    '--s3-upload-concurrency', '4',
    '--s3-chunk-size', '128M',
    '--progress'
)

if ($DryRun) {
    Write-Host "[DryRun] rclone copyto `"$InstallerPath`" :s3:$Bucket/$objectKey --s3-provider Cloudflare --s3-endpoint $endpoint ..."
} else {
    Write-Host "Uploading (~$fileSizeGB GB via multipart)..."
    & rclone @rcloneArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Error "rclone upload failed (exit $LASTEXITCODE)."
        exit $LASTEXITCODE
    }
    Write-Host "Upload complete."
}

if ($PublicBaseUrl) {
    $url = ($PublicBaseUrl.TrimEnd('/')) + "/" + $objectKey
    Write-Host ""
    Write-Host "Public download URL (add to the GitHub release notes):"
    Write-Host "  $url"
} else {
    Write-Host ""
    Write-Host "Set -PublicBaseUrl (or AGENTX_R2_PUBLIC_BASE_URL) to print the final public link."
}
