<#
.SYNOPSIS
    Downloads the default GGUF model for Agent-X built-in local LLM.

.DESCRIPTION
    Downloads Llama 3.2 3B Instruct (Q4_K_M quantization, ~2 GB) from
    HuggingFace to the local models directory used by Agent-X.

    The model is stored at: %LOCALAPPDATA%\AgentX\Models\

.EXAMPLE
    .\download-model.ps1
    .\download-model.ps1 -ModelUrl "https://huggingface.co/bartowski/Llama-3.2-3B-Instruct-GGUF/resolve/main/Llama-3.2-3B-Instruct-Q4_K_M.gguf"
#>

param(
    [string]$ModelFileName = "llama-3.2-3b-instruct-q4_k_m.gguf",

    [string]$ModelUrl = "https://huggingface.co/bartowski/Llama-3.2-3B-Instruct-GGUF/resolve/main/Llama-3.2-3B-Instruct-Q4_K_M.gguf",

    [string]$ModelsDir = (Join-Path $env:LOCALAPPDATA "AgentX\Models")
)

$ErrorActionPreference = "Stop"

# Ensure the models directory exists
if (-not (Test-Path $ModelsDir)) {
    Write-Host "Creating models directory: $ModelsDir"
    New-Item -ItemType Directory -Path $ModelsDir -Force | Out-Null
}

$destPath = Join-Path $ModelsDir $ModelFileName

# Check if model already exists
if (Test-Path $destPath) {
    $fileSize = (Get-Item $destPath).Length
    $fileSizeMB = [math]::Round($fileSize / 1MB, 1)
    Write-Host "Model already exists at: $destPath ($fileSizeMB MB)"
    $response = Read-Host "Re-download? (y/N)"
    if ($response -ne "y") {
        Write-Host "Skipping download."
        exit 0
    }
}

Write-Host ""
Write-Host "Agent-X Local LLM Model Download"
Write-Host "================================="
Write-Host "Model:       $ModelFileName"
Write-Host "Source:       $ModelUrl"
Write-Host "Destination:  $destPath"
Write-Host ""
Write-Host "This will download approximately 2 GB. Ensure you have sufficient disk space."
Write-Host ""

# Download with progress
Write-Host "Downloading..."
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

try {
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri $ModelUrl -OutFile $destPath -UseBasicParsing
    $ProgressPreference = 'Continue'
}
catch {
    Write-Error "Download failed: $_"
    if (Test-Path $destPath) {
        Remove-Item $destPath -Force
    }
    exit 1
}

$stopwatch.Stop()
$fileSize = (Get-Item $destPath).Length
$fileSizeMB = [math]::Round($fileSize / 1MB, 1)
$elapsed = $stopwatch.Elapsed.ToString("mm\:ss")

Write-Host ""
Write-Host "Download complete!"
Write-Host "  File:     $destPath"
Write-Host "  Size:     $fileSizeMB MB"
Write-Host "  Time:     $elapsed"
Write-Host ""
Write-Host "Agent-X will automatically detect and load this model on next launch."
