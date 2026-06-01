<#
.SYNOPSIS
    Uploads the OFFLINE Agent-X installer (model-bundled, ~2 GB) to Cloudflare R2.

.DESCRIPTION
    The OFFLINE installer exceeds GitHub Releases' 2 GiB per-asset limit, so it is hosted on
    Cloudflare R2 and linked from the release notes (the SLIM installer is the GitHub asset).
    This script uploads the file with `wrangler r2 object put` and prints the public URL to put
    in the release notes.

    Credentials are read from the environment — nothing secret is stored in the repo:
      CLOUDFLARE_API_TOKEN    R2-scoped API token (Object Read & Write).
      CLOUDFLARE_ACCOUNT_ID   Cloudflare account id.

    Requires the Wrangler CLI (`npm i -g wrangler` or `npx wrangler`).

.PARAMETER Bucket
    R2 bucket name. Defaults to env AGENTX_R2_BUCKET or "agentx-releases".

.PARAMETER PublicBaseUrl
    Public base URL for the bucket (r2.dev domain or a custom domain), used only to print the
    final link. Defaults to env AGENTX_R2_PUBLIC_BASE_URL.

.PARAMETER InstallerPath
    Path to the offline installer. Defaults to the standard build output for this version.

.PARAMETER Version
    Version string used in the object key. Defaults to 2.1.1.

.PARAMETER DryRun
    Print the planned action without uploading.

.EXAMPLE
    $env:CLOUDFLARE_API_TOKEN  = '...'
    $env:CLOUDFLARE_ACCOUNT_ID = '...'
    ./publish-offline-installer.ps1 -Version 2.1.1 -PublicBaseUrl 'https://downloads.strategia-x.com'
#>

[CmdletBinding()]
param(
    [string]$Version = "2.1.1",
    [string]$Bucket = $(if ($env:AGENTX_R2_BUCKET) { $env:AGENTX_R2_BUCKET } else { "agentx-releases" }),
    [string]$PublicBaseUrl = $env:AGENTX_R2_PUBLIC_BASE_URL,
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

$fileSizeGB = [math]::Round((Get-Item $InstallerPath).Length / 1GB, 2)
$objectKey = "v$Version/AgentX-Setup-$Version-x64-offline.exe"

Write-Host ""
Write-Host "Agent-X OFFLINE installer -> Cloudflare R2"
Write-Host "==========================================="
Write-Host "File:        $InstallerPath ($fileSizeGB GB)"
Write-Host "Bucket:      $Bucket"
Write-Host "Object key:  $objectKey"
Write-Host ""

if (-not $env:CLOUDFLARE_API_TOKEN -or -not $env:CLOUDFLARE_ACCOUNT_ID) {
    Write-Error "Set CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID before running (R2-scoped token)."
    exit 1
}

if ($DryRun) {
    Write-Host "[DryRun] Would run: wrangler r2 object put $Bucket/$objectKey --file `"$InstallerPath`" --content-type application/vnd.microsoft.portable-executable"
} else {
    Write-Host "Uploading (this transfers ~$fileSizeGB GB)..."
    & wrangler r2 object put "$Bucket/$objectKey" `
        --file "$InstallerPath" `
        --content-type "application/vnd.microsoft.portable-executable"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "wrangler upload failed (exit $LASTEXITCODE)."
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
