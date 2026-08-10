# =====================================================================
# BIMBCC PlugIn | Automated Test Suite (Автоматическое тестирование)
# =====================================================================

param (
    [switch]$VerboseOutput = $false
)

$projectDir = $PSScriptRoot
Set-Location $projectDir

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  BIMBCC PlugIn | Automated Test Suite" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

$passed = 0
$failed = 0

function Assert-Test([string]$testName, [scriptblock]$action) {
    Write-Host "[TEST] $testName ... " -NoNewline -ForegroundColor Yellow
    try {
        & $action
        Write-Host "PASSED!" -ForegroundColor Green
        $global:passed++
    }
    catch {
        Write-Host "FAILED!" -ForegroundColor Red
        Write-Host "       Error: $_" -ForegroundColor DarkRed
        $global:failed++
    }
}

# ---------------------------------------------------------------------
# Test 1: Validate Required Files & Directories
# ---------------------------------------------------------------------
Assert-Test "1. Check Project Core Files Existence" {
    $requiredFiles = @(
        "$projectDir\BCCPlugIn.csproj",
        "$projectDir\BCCPlugIn.addin",
        "$projectDir\BCCInstaller\BCCInstaller.csproj",
        "$projectDir\BCCInstaller\installer_icon.ico",
        "$projectDir\Ltools\LTools\2024\LTools\LTools.dll"
    )

    foreach ($file in $requiredFiles) {
        if (-not (Test-Path $file)) {
            throw "Missing required file: $file"
        }
    }
}

# ---------------------------------------------------------------------
# Test 2: Compile Plugin Assembly (BCCPlugIn.dll)
# ---------------------------------------------------------------------
Assert-Test "2. Build BCCPlugIn.dll (Release)" {
    $buildOutput = dotnet build "$projectDir\BCCPlugIn.csproj" -c Release 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "BCCPlugIn compilation failed:`n$buildOutput"
    }

    $dllPath = "$projectDir\bin\Release\BCCPlugIn.dll"
    if (-not (Test-Path $dllPath)) {
        throw "BCCPlugIn.dll output file not found at $dllPath"
    }
}

# ---------------------------------------------------------------------
# Test 3: Validate Plugin Assembly Types & Entry Commands
# ---------------------------------------------------------------------
Assert-Test "3. Inspect BCCPlugIn.dll Assembly Types" {
    $dllPath = "$projectDir\bin\Release\BCCPlugIn.dll"

    $binDir = "$projectDir\bin\Release"
    $nugetDir = "$env:USERPROFILE\.nuget\packages"
    $resolveHandler = [System.ResolveEventHandler]{
        param($s, $e)
        $shortName = $e.Name.Split(',')[0]
        $candidate = Get-ChildItem -Path $binDir, $nugetDir -Filter "$shortName.dll" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($candidate) { return [System.Reflection.Assembly]::LoadFrom($candidate.FullName) }
        return $null
    }
    [System.AppDomain]::CurrentDomain.add_AssemblyResolve($resolveHandler)

    $bytes = [System.IO.File]::ReadAllBytes($dllPath)
    $asm = [System.Reflection.Assembly]::Load($bytes)
    
    $loadedTypes = @()
    try {
        $loadedTypes = $asm.GetTypes()
    } catch [System.Reflection.ReflectionTypeLoadException] {
        $loadedTypes = $_.Exception.Types | Where-Object { $_ -ne $null }
    }

    $typeNames = $loadedTypes | ForEach-Object { $_.FullName }

    $requiredTypes = @(
        "BCCPlugIn.BatchParamsWindow",
        "BCCPlugIn.BatchParamsEngine",
        "BCCPlugIn.SharedParamParser",
        "BCCPlugIn.HeatLossEngine",
        "BCCPlugIn.HeatLossWindow"
    )

    foreach ($tName in $requiredTypes) {
        if ($typeNames -notcontains $tName) {
            throw "Type $tName is missing from BCCPlugIn.dll"
        }
    }
}

# ---------------------------------------------------------------------
# Test 4: Compile Installer App (BIMBCC_Installer.exe)
# ---------------------------------------------------------------------
Assert-Test "4. Build BIMBCC_Installer.exe (Release)" {
    $buildOutput = dotnet build "$projectDir\BCCInstaller\BCCInstaller.csproj" -c Release 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "BIMBCC_Installer compilation failed:`n$buildOutput"
    }

    $exePath = "$projectDir\BCCInstaller\bin\Release\net48\BIMBCC_Installer.exe"
    if (-not (Test-Path $exePath)) {
        throw "BIMBCC_Installer.exe output file not found at $exePath"
    }
}

# ---------------------------------------------------------------------
# Test 5: Verify Native LTools DLL & Assembly Integrity
# ---------------------------------------------------------------------
Assert-Test "5. Verify Native LTools.dll Assembly Integrity" {
    $ltoolsDllPath = "$projectDir\Ltools\LTools\2024\LTools\LTools.dll"
    
    $ltoolsDir = [System.IO.Path]::GetDirectoryName($ltoolsDllPath)
    Get-ChildItem $ltoolsDir -Filter "*.dll" | ForEach-Object {
        try { [System.Reflection.Assembly]::LoadFrom($_.FullName) | Out-Null } catch { }
    }

    $asm = [System.Reflection.Assembly]::LoadFrom($ltoolsDllPath)
    $loadedTypes = @()
    try {
        $loadedTypes = $asm.GetTypes()
    } catch [System.Reflection.ReflectionTypeLoadException] {
        $loadedTypes = $_.Exception.Types | Where-Object { $_ -ne $null }
    }

    $typeNames = $loadedTypes | ForEach-Object { $_.FullName }

    if ($typeNames -notcontains "SAV.ParamRules.FrmRuler") {
        throw "SAV.ParamRules.FrmRuler type missing in LTools.dll"
    }
}

# ---------------------------------------------------------------------
# Test 6: Verify Addin Manifest XML Format
# ---------------------------------------------------------------------
Assert-Test "6. Verify BCCPlugIn.addin XML Manifest Syntax" {
    $addinFile = "$projectDir\BCCPlugIn.addin"
    [xml]$xml = Get-Content $addinFile
    
    if ($xml.RevitAddIns.AddIn.Name -notlike "*BCC*") {
        throw "Addin name mismatch in manifest. Expected name to contain 'BCC'"
    }
}

# ---------------------------------------------------------------------
# Test Results Summary
# ---------------------------------------------------------------------
Write-Host "--------------------------------------------------" -ForegroundColor Cyan
Write-Host "  TEST RESULTS: Passed: $passed, Failed: $failed" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
Write-Host "--------------------------------------------------" -ForegroundColor Cyan

if ($failed -gt 0) {
    exit 1
} else {
    exit 0
}
