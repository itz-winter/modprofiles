# ============================================================
# Build the standalone .NET installer (no Inno Setup required)
# ============================================================
# This script:
#   1. Publishes the main app as a self-contained single-file exe
#   2. Packages it into a ZIP (payload.zip)
#   3. Embeds the ZIP into the installer project as a resource
#   4. Publishes the installer as a single-file exe
#
# Usage:  .\build-dotnet-installer.ps1
# Output: installer\output\ModProfileSwitcher_Setup_1.0.0.exe
# ============================================================

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent   # workspace root
$srcDir = Join-Path $root 'src'
$installerDir = Join-Path (Join-Path $root 'installer') 'dotnet-installer'
$payloadDir = Join-Path $installerDir 'payload'
$outputDir = Join-Path (Join-Path $root 'installer') 'output'
$publishDir = Join-Path $root 'publish'

Write-Host '=== Step 1: Publish main application ===' -ForegroundColor Cyan
Push-Location $srcDir
dotnet publish -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -o $publishDir
Pop-Location

if (-not (Test-Path (Join-Path $publishDir 'ModProfileSwitcher.exe'))) {
    Write-Error 'Main app publish failed — ModProfileSwitcher.exe not found.'
    exit 1
}

Write-Host '=== Step 2: Create payload.zip ===' -ForegroundColor Cyan
# Clean previous payload
if (Test-Path $payloadDir) { Remove-Item $payloadDir -Recurse -Force }
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null

$payloadZip = Join-Path $payloadDir 'payload.zip'

# Stage files into a temp dir then zip
$stageDir = Join-Path $env:TEMP 'mps_installer_stage'
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

Copy-Item (Join-Path $publishDir 'ModProfileSwitcher.exe') $stageDir
Copy-Item (Join-Path $root 'LICENSE') $stageDir -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root 'README.md') $stageDir -ErrorAction SilentlyContinue

Compress-Archive -Path (Join-Path $stageDir '*') -DestinationPath $payloadZip -Force
Remove-Item $stageDir -Recurse -Force

Write-Host "   payload.zip: $([math]::Round((Get-Item $payloadZip).Length / 1MB, 1)) MB"

Write-Host '=== Step 3: Build installer exe ===' -ForegroundColor Cyan
Push-Location $installerDir
dotnet build -c Release
Pop-Location

# The net48 build output is in the bin\Release\net48 folder
$builtExe = Join-Path (Join-Path (Join-Path (Join-Path $installerDir 'bin') 'Release') 'net48') 'ModProfileSwitcher_Setup.exe'
$destExe = Join-Path $outputDir 'ModProfileSwitcher_Setup_1.0.0_Standalone.exe'

if (-not (Test-Path $builtExe)) {
    Write-Error "Installer build failed - exe not found at $builtExe"
    exit 1
}

if (-not (Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir -Force | Out-Null }
Copy-Item $builtExe $destExe -Force

# Cleanup payload dir (it was only needed at build time)
Remove-Item $payloadDir -Recurse -Force -ErrorAction SilentlyContinue

$size = [math]::Round((Get-Item $destExe).Length / 1MB, 1)
Write-Host ''
Write-Host "=== Done! ===" -ForegroundColor Green
Write-Host "Installer: $destExe"
Write-Host "Size:      $size MB"
Write-Host ''
Write-Host 'This installer requires NO external tools — just run the exe.'
