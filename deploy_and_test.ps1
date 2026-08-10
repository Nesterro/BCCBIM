# =====================================================================
# BIMBCC PlugIn | Unified Automated Testing & Deployment Tool
# Средство автоматизации деплоя и тестирования плагина BIMBCC
# =====================================================================

param (
    [string]$Version = "",
    [switch]$AutoIncrement = $false,
    [switch]$TestOnly = $false,
    [switch]$DeployLocalOnly = $false,
    [string]$Notes = "Автоматический деплой и публикация релиза BIMBCC PlugIn",
    [string]$Repo = "Nesterro/BCCBIM"
)

$projectDir = $PSScriptRoot
Set-Location $projectDir

Write-Host "==================================================" -ForegroundColor Red
Write-Host "  BIMBCC PlugIn | AUTOMATED DEPLOYMENT & TESTING  " -ForegroundColor Red
Write-Host "==================================================" -ForegroundColor Red

# ---------------------------------------------------------------------
# STEP 1: RUN AUTOMATED TEST SUITE
# ---------------------------------------------------------------------
Write-Host "`n[STEP 1/4] Running Automated Test Suite..." -ForegroundColor Yellow
$testScript = "$projectDir\run_tests.ps1"
if (Test-Path $testScript) {
    powershell -ExecutionPolicy Bypass -File $testScript
    if ($LASTEXITCODE -ne 0) {
        Write-Host "TEST SUITE FAILED! Aborting deployment." -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "WARNING: run_tests.ps1 not found, skipping unit tests." -ForegroundColor Yellow
}

if ($TestOnly) {
    Write-Host "`nTest run completed successfully (-TestOnly mode). Exiting without deploy." -ForegroundColor Green
    exit 0
}

# ---------------------------------------------------------------------
# STEP 2: DETERMINE VERSION NUMBER
# ---------------------------------------------------------------------
if ([string]::IsNullOrEmpty($Version)) {
    # Detect current version from version.txt or default
    $versionFile = "$env:APPDATA\BIMBCC\PlugIn\version.txt"
    $currentVer = "2.2.0"
    if (Test-Path $versionFile) {
        $currentVer = (Get-Content $versionFile -Encoding UTF8).Trim().TrimStart('v')
    }

    if ($AutoIncrement) {
        $parts = $currentVer.Split('.')
        if ($parts.Length -eq 3) {
            $major = [int]$parts[0]
            $minor = [int]$parts[1]
            $patch = [int]$parts[2] + 1
            $Version = "$major.$minor.$patch"
        } else {
            $Version = "2.3.0"
        }
    } else {
        $Version = $currentVer
    }
}

Write-Host "`n[STEP 2/4] Target Version: v$Version" -ForegroundColor Cyan

# ---------------------------------------------------------------------
# STEP 3: BUILD BINARIES & PACKAGE DEPLOYMENT ZIP
# ---------------------------------------------------------------------
Write-Host "`n[STEP 3/4] Building binaries and packaging installation zip..." -ForegroundColor Yellow

# Clean MSBuild caches
if (Test-Path "$projectDir\bin") { Remove-Item "$projectDir\bin" -Recurse -Force }
if (Test-Path "$projectDir\obj") { Remove-Item "$projectDir\obj" -Recurse -Force }
if (Test-Path "$projectDir\BCCInstaller\bin") { Remove-Item "$projectDir\BCCInstaller\bin" -Recurse -Force }
if (Test-Path "$projectDir\BCCInstaller\obj") { Remove-Item "$projectDir\BCCInstaller\obj" -Recurse -Force }

# Build BCCPlugIn.dll
dotnet build "$projectDir\BCCPlugIn.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed for BCCPlugIn.csproj" }

# Build BIMBCC_Installer.exe
dotnet build "$projectDir\BCCInstaller\BCCInstaller.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed for BCCInstaller.csproj" }

# Package ZIP
$releaseDir = "$projectDir\release_build"
if (Test-Path $releaseDir) { Remove-Item $releaseDir -Recurse -Force }
New-Item $releaseDir -ItemType Directory | Out-Null

$pkgDir = "$releaseDir\BCCPlugIn"
New-Item $pkgDir -ItemType Directory | Out-Null

# Copy main binaries
Copy-Item "$projectDir\bin\Release\BCCPlugIn.dll" -Destination $pkgDir -Force
if (Test-Path "$projectDir\bin\Release\Newtonsoft.Json.dll") {
    Copy-Item "$projectDir\bin\Release\Newtonsoft.Json.dll" -Destination $pkgDir -Force
}
Copy-Item "$projectDir\BCCPlugIn.addin" -Destination $pkgDir -Force

# Copy full native LTools binaries directory
$ltoolsSourceDir = "$projectDir\Ltools\LTools\2024\LTools"
if (Test-Path $ltoolsSourceDir) {
    Copy-Item $ltoolsSourceDir -Destination "$pkgDir\LTools" -Recurse -Force
}

# Create version.txt in package
Set-Content -Path "$pkgDir\version.txt" -Value "v$Version" -Encoding UTF8

# Sync to local AppData plugin install location
$localAppDataDir = "$env:APPDATA\BIMBCC\PlugIn"
if (-not (Test-Path $localAppDataDir)) { New-Item $localAppDataDir -ItemType Directory | Out-Null }
Copy-Item "$pkgDir\*" -Destination $localAppDataDir -Recurse -Force

Write-Host "Local AppData deployment synchronized to: $localAppDataDir" -ForegroundColor Green

# Create release ZIP package
$zipPath = "$releaseDir\BCCPlugIn_v$Version.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($pkgDir, $zipPath)

# Copy Portable Installer to release_build
$installerPath = "$releaseDir\BIMBCC_Installer.exe"
Copy-Item "$projectDir\BCCInstaller\bin\Release\net48\BIMBCC_Installer.exe" -Destination $installerPath -Force

Write-Host "Release ZIP created: $zipPath" -ForegroundColor Green
Write-Host "Installer created:   $installerPath" -ForegroundColor Green

if ($DeployLocalOnly) {
    Write-Host "`nLocal Deployment Completed (-DeployLocalOnly mode)." -ForegroundColor Green
    exit 0
}

# ---------------------------------------------------------------------
# STEP 4: PUBLISH TO GITHUB RELEASES
# ---------------------------------------------------------------------
Write-Host "`n[STEP 4/4] Publishing release v$Version to GitHub ($Repo)..." -ForegroundColor Yellow

$ghExe = Get-Command gh -ErrorAction SilentlyContinue
if (-not $ghExe) {
    if (Test-Path "C:\Program Files\GitHub CLI\gh.exe") {
        $ghExe = "C:\Program Files\GitHub CLI\gh.exe"
    }
}

if ($ghExe) {
    $isExist = $false
    try {
        $check = & $ghExe release view "v$Version" --repo "$Repo" 2>&1
        if ($LASTEXITCODE -eq 0) { $isExist = $true }
    } catch { }

    if (-not $isExist) {
        Write-Host "Creating new GitHub Release v$Version..." -ForegroundColor Yellow
        & $ghExe release create "v$Version" "$zipPath" "$installerPath" --repo "$Repo" --title "Release v$Version" --notes "$Notes"
    } else {
        Write-Host "Release v$Version exists. Updating assets..." -ForegroundColor Yellow
        & $ghExe release upload "v$Version" "$zipPath" "$installerPath" --repo "$Repo" --clobber
    }

    Write-Host "`n==================================================" -ForegroundColor Green
    Write-Host "SUCCESS! Release v$Version & BIMBCC_Installer.exe deployed to GitHub repo $Repo!" -ForegroundColor Green
    Write-Host "URL: https://github.com/$Repo/releases/tag/v$Version" -ForegroundColor Green
    Write-Host "==================================================" -ForegroundColor Green
} else {
    Write-Host "WARNING: GitHub CLI (gh.exe) not found. Build packages are available at $releaseDir" -ForegroundColor Yellow
}
