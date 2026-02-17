# ============================================================
# Build & Package Script for Minecraft Mod Profile Switcher
# ============================================================
# This script:
#   1. Publishes the .NET app as a self-contained single-file exe
#   2. Compiles the Inno Setup installer (if Inno Setup is installed)
#
# Usage:
#   .\build-installer.ps1
#   .\build-installer.ps1 -SkipPublish    # skip dotnet publish, just rebuild installer
# ============================================================

param(
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path "$root\src\ModProfileSwitcher.csproj")) {
    $root = $PSScriptRoot
}

$srcDir     = Join-Path $root "src"
$publishDir = Join-Path $root "publish"
$issFile    = Join-Path $root "installer\setup.iss"
$outputDir  = Join-Path $root "installer\output"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Minecraft Mod Profile Switcher Builder" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# --- Step 1: Publish ---
if (-not $SkipPublish) {
    Write-Host "[1/2] Publishing self-contained exe..." -ForegroundColor Yellow

    Push-Location $srcDir
    dotnet publish -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -o $publishDir 2>&1

    if ($LASTEXITCODE -ne 0) {
        Pop-Location
        Write-Host "ERROR: dotnet publish failed!" -ForegroundColor Red
        exit 1
    }
    Pop-Location

    $exePath = Join-Path $publishDir "ModProfileSwitcher.exe"
    $sizeMB = [math]::Round((Get-Item $exePath).Length / 1MB, 1)
    Write-Host "  Published: $exePath ($sizeMB MB)" -ForegroundColor Green
}
else {
    Write-Host "[1/2] Skipping publish (using existing build)" -ForegroundColor DarkGray
}

# --- Step 2: Compile Installer ---
Write-Host "[2/2] Building installer..." -ForegroundColor Yellow

# Find Inno Setup compiler
$iscc = $null
$innoSearchPaths = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)

foreach ($p in $innoSearchPaths) {
    if (Test-Path $p) {
        $iscc = $p
        break
    }
}

if (-not $iscc) {
    Write-Host ""
    Write-Host "  Inno Setup 6 not found!" -ForegroundColor Red
    Write-Host ""
    Write-Host "  To build the installer, install Inno Setup 6 from:" -ForegroundColor White
    Write-Host "    https://jrsoftware.org/isdl.php" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Or open installer\setup.iss manually in the Inno Setup IDE." -ForegroundColor White
    Write-Host ""
    Write-Host "  The published exe is ready at:" -ForegroundColor White
    Write-Host "    $publishDir\ModProfileSwitcher.exe" -ForegroundColor Green
    Write-Host ""
    exit 0
}

Write-Host "  Found Inno Setup: $iscc" -ForegroundColor DarkGray

# Create output directory
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# Compile
& $iscc $issFile

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Inno Setup compilation failed!" -ForegroundColor Red
    exit 1
}

$installerFile = Get-ChildItem $outputDir -Filter "*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " Build Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Installer: $($installerFile.FullName)" -ForegroundColor White
Write-Host "  Size:      $([math]::Round($installerFile.Length / 1MB, 1)) MB" -ForegroundColor White
Write-Host ""
