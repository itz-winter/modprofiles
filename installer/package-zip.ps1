# ============================================================
# Quick Package Script — Creates a portable .zip distribution
# No Inno Setup required!
# ============================================================
# Usage:  .\package-zip.ps1
# Output: installer\output\ModProfileSwitcher_v1.0.0_Portable.zip
# ============================================================

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path "$root\src\ModProfileSwitcher.csproj")) {
    $root = $PSScriptRoot
}

$srcDir     = Join-Path $root "src"
$publishDir = Join-Path $root "publish"
$outputDir  = Join-Path $root "installer\output"
$version    = "1.0.0"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Packaging Mod Profile Switcher v$version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# --- Publish ---
Write-Host "[1/2] Publishing..." -ForegroundColor Yellow
Push-Location $srcDir
dotnet publish -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    Pop-Location
    Write-Host "ERROR: dotnet publish failed!" -ForegroundColor Red
    exit 1
}
Pop-Location

# --- Package ---
Write-Host "[2/2] Creating ZIP..." -ForegroundColor Yellow

if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# Create a staging folder with clean contents
$stageDir = Join-Path $env:TEMP "ModProfileSwitcher_stage"
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
New-Item -ItemType Directory -Path $stageDir | Out-Null

Copy-Item (Join-Path $publishDir "ModProfileSwitcher.exe") $stageDir
Copy-Item (Join-Path $root "INSTRUCTIONS.md") (Join-Path $stageDir "README.md") -ErrorAction SilentlyContinue

$zipName = "ModProfileSwitcher_v${version}_Portable.zip"
$zipPath = Join-Path $outputDir $zipName

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$stageDir\*" -DestinationPath $zipPath -CompressionLevel Optimal

Remove-Item $stageDir -Recurse -Force

$sizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " Package Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  ZIP: $zipPath" -ForegroundColor White
Write-Host "  Size: $sizeMB MB" -ForegroundColor White
Write-Host ""
Write-Host "  Users can extract and run ModProfileSwitcher.exe directly." -ForegroundColor DarkGray
Write-Host ""
