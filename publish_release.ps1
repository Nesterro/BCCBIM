# Script to build, package, and upload BIMBCC PlugIn releases to GitHub

param (
    [string]$Version = "2.0.0",
    [string]$Notes = "Полная интеграция оригинальной библиотеки LTools.dll в BIMBCC PlugIn. Запуск 100% родного редактора правил LTools (SAV.ParamRules.FrmRuler)",
    [string]$Repo = "Nesterro/BCCBIM"
)

$ErrorActionPreference = "Stop"

$projectDir = "C:\Users\user\Yandex.Disk\BCC\BCC PlugIn"
Set-Location $projectDir

Write-Host "==================================================" -ForegroundColor Red
Write-Host "BIMBCC PlugIn | Building Release v$Version for $Repo" -ForegroundColor Red
Write-Host "==================================================" -ForegroundColor Red

# 1. Build Revit Plugin
Write-Host "Step 1: Building BCCPlugIn.dll (Release)..." -ForegroundColor Yellow
dotnet build "$projectDir\BCCPlugIn.csproj" -c Release

# 2. Build Installer App with clean cache
Write-Host "Step 2: Building BIMBCC_Installer.exe (Clean Cache)..." -ForegroundColor Yellow
if (Test-Path "$projectDir\BCCInstaller\bin") { Remove-Item "$projectDir\BCCInstaller\bin" -Recurse -Force }
if (Test-Path "$projectDir\BCCInstaller\obj") { Remove-Item "$projectDir\BCCInstaller\obj" -Recurse -Force }
dotnet build "$projectDir\BCCInstaller\BCCInstaller.csproj" -c Release

# 3. Create Package ZIP
Write-Host "Step 3: Packaging plugin files into ZIP..." -ForegroundColor Yellow
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
    Write-Host "Packaging native LTools binaries from $ltoolsSourceDir..." -ForegroundColor Green
    Copy-Item $ltoolsSourceDir -Destination "$pkgDir\LTools" -Recurse -Force
}

# Create version.txt in package
Set-Content -Path "$pkgDir\version.txt" -Value "v$Version" -Encoding UTF8

# Also sync to local AppData if plugin directory exists
$localAppDataDir = "$env:APPDATA\BIMBCC\PlugIn"
if (Test-Path $localAppDataDir) {
    Set-Content -Path "$localAppDataDir\version.txt" -Value "v$Version" -Encoding UTF8
    if (Test-Path $ltoolsSourceDir) {
        Copy-Item $ltoolsSourceDir -Destination "$localAppDataDir\LTools" -Recurse -Force
    }
}

$zipPath = "$releaseDir\BCCPlugIn_v$Version.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($pkgDir, $zipPath)

Write-Host "Release package created: $zipPath" -ForegroundColor Green

# Copy Installer to release_build
$installerPath = "$releaseDir\BIMBCC_Installer.exe"
Copy-Item "$projectDir\BCCInstaller\bin\Release\net48\BIMBCC_Installer.exe" -Destination $installerPath -Force

# 4. Check GitHub CLI (gh) and publish
Write-Host "Step 4: Publishing release and installer via GitHub CLI..." -ForegroundColor Yellow

$ghExe = Get-Command gh -ErrorAction SilentlyContinue
if (-not $ghExe) {
    if (Test-Path "C:\Program Files\GitHub CLI\gh.exe") {
        $ghExe = "C:\Program Files\GitHub CLI\gh.exe"
    }
}

if ($ghExe) {
    Write-Host "Publishing release v$Version with BIMBCC_Installer.exe to $Repo..." -ForegroundColor Green
    
    $isExist = $false
    try {
        $check = & $ghExe release view "v$Version" --repo "$Repo" 2>&1
        if ($LASTEXITCODE -eq 0) { $isExist = $true }
    } catch { }

    if (-not $isExist) {
        Write-Host "Creating new release v$Version..." -ForegroundColor Yellow
        & $ghExe release create "v$Version" "$zipPath" "$installerPath" --repo "$Repo" --title "Release v$Version" --notes "$Notes"
    } else {
        Write-Host "Release v$Version exists. Uploading assets..." -ForegroundColor Yellow
        & $ghExe release upload "v$Version" "$zipPath" "$installerPath" --repo "$Repo" --clobber
    }

    Write-Host "SUCCESS! Release v$Version and BIMBCC_Installer.exe published to GitHub repository $Repo!" -ForegroundColor Green
} else {
    Write-Host "WARNING: GitHub CLI (gh) not found. Build is ready at $releaseDir" -ForegroundColor Yellow
}
