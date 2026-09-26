<#
.SYNOPSIS
    Downloads the default GGUF model for Agent-X built-in local LLM.

.DESCRIPTION
    Downloads Llama 3.2 3B Instruct (Q4_K_M quantization, ~2 GB) from
    HuggingFace. Downloads to BOTH:
      1. The project's models/ directory (for bundling in the installer)
      2. %LOCALAPPDATA%\AgentX\Models\ (for local development/testing)

    The file name and URL are read from the app's own constants
    (DefaultModelFileName / DefaultDownloadUrl in src/AgentX.Core/AI/BuiltInModelBootstrap.cs),
    so the OFFLINE installer bundles the same build the app downloads on first run. The script
    used to fetch a different (bartowski) quantization under the same file name.

    Known gap: neither this script nor the app pins the download to a repository revision or
    verifies a SHA-256 (the URL follows the mutable "main" branch). The hash of the downloaded
    file is printed so a release can record it.

.EXAMPLE
    .\download-model.ps1
    .\download-model.ps1 -SkipLocal
#>

param(
    # Empty = use BuiltInModelBootstrap.DefaultModelFileName. Override only for testing.
    [string]$ModelFileName,

    # Empty = use BuiltInModelBootstrap.DefaultDownloadUrl. Override only for testing.
    [string]$ModelUrl,

    [switch]$SkipLocal
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir

# Single source of truth: the constants the app itself downloads with.
$bootstrapSource = Join-Path $projectRoot "src\AgentX.Core\AI\BuiltInModelBootstrap.cs"
function Get-BootstrapConstant([string]$Name) {
    $text = [IO.File]::ReadAllText($bootstrapSource)
    $match = [regex]::Match($text, "const\s+string\s+$Name\s*=\s*""([^""]+)""")
    if (-not $match.Success) { throw "Could not read $Name from $bootstrapSource." }
    return $match.Groups[1].Value
}
if ([string]::IsNullOrWhiteSpace($ModelFileName)) { $ModelFileName = Get-BootstrapConstant 'DefaultModelFileName' }
if ([string]::IsNullOrWhiteSpace($ModelUrl)) { $ModelUrl = Get-BootstrapConstant 'DefaultDownloadUrl' }

# Primary destination: project models/ directory (for installer bundling)
$buildModelsDir = Join-Path $projectRoot "models"
$buildDestPath = Join-Path $buildModelsDir $ModelFileName

# Secondary destination: local app data (for dev/testing)
$localModelsDir = Join-Path $env:LOCALAPPDATA "AgentX\Models"
$localDestPath = Join-Path $localModelsDir $ModelFileName

# Check if model already exists in build directory
if (Test-Path $buildDestPath) {
    $fileSize = (Get-Item $buildDestPath).Length
    $fileSizeMB = [math]::Round($fileSize / 1MB, 1)
    Write-Host "Model already exists at: $buildDestPath ($fileSizeMB MB)"
    $response = Read-Host "Re-download? (y/N)"
    if ($response -ne "y") {
        # Still copy to local if needed
        if (-not $SkipLocal -and -not (Test-Path $localDestPath)) {
            New-Item -ItemType Directory -Path $localModelsDir -Force | Out-Null
            Copy-Item $buildDestPath $localDestPath
            Write-Host "Copied to local: $localDestPath"
        }
        exit 0
    }
}

# Ensure build models directory exists
if (-not (Test-Path $buildModelsDir)) {
    New-Item -ItemType Directory -Path $buildModelsDir -Force | Out-Null
}

Write-Host ""
Write-Host "Agent-X Local LLM Model Download"
Write-Host "================================="
Write-Host "Model:        $ModelFileName"
Write-Host "Source:        $ModelUrl"
Write-Host "Build dest:   $buildDestPath"
if (-not $SkipLocal) {
    Write-Host "Local dest:   $localDestPath"
}
Write-Host ""
Write-Host "This will download approximately 2 GB. Ensure you have sufficient disk space."
Write-Host ""

# Download
Write-Host "Downloading..."
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

try {
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri $ModelUrl -OutFile $buildDestPath -UseBasicParsing
    $ProgressPreference = 'Continue'
}
catch {
    Write-Error "Download failed: $_"
    if (Test-Path $buildDestPath) {
        Remove-Item $buildDestPath -Force
    }
    exit 1
}

$stopwatch.Stop()
$fileSize = (Get-Item $buildDestPath).Length
$fileSizeMB = [math]::Round($fileSize / 1MB, 1)
$elapsed = $stopwatch.Elapsed.ToString("mm\:ss")

$sha256 = (Get-FileHash $buildDestPath -Algorithm SHA256).Hash.ToLower()

Write-Host ""
Write-Host "Download complete!"
Write-Host "  File:     $buildDestPath"
Write-Host "  Size:     $fileSizeMB MB"
Write-Host "  SHA-256:  $sha256"
Write-Host "  Time:     $elapsed"

# Copy to local app data for development
if (-not $SkipLocal) {
    if (-not (Test-Path $localModelsDir)) {
        New-Item -ItemType Directory -Path $localModelsDir -Force | Out-Null
    }
    Copy-Item $buildDestPath $localDestPath -Force
    Write-Host "  Copied to: $localDestPath"
}

Write-Host ""
Write-Host "The model is ready for installer bundling and local development."
