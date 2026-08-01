[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$LauncherOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = $PSScriptRoot
$versionFile = Join-Path $projectRoot "version.txt"
$launcherProject = Join-Path $projectRoot "AetheriumLauncher\AetheriumLauncher.csproj"
$patchTestsProject = Join-Path $projectRoot "AetheriumLauncher.PatchTests\AetheriumLauncher.PatchTests.csproj"
$installerScript = Join-Path $projectRoot "Installer\AetheriumPlay.iss"
$installerOutput = Join-Path $projectRoot "artifacts\installer"

if (-not (Test-Path -LiteralPath $versionFile)) {
    throw "Missing version file: $versionFile"
}

$version = (Get-Content -LiteralPath $versionFile -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "version.txt must contain a semver like 1.0.8 (got '$version')."
}

Write-Host "Version: $version"
Write-Host "Building Aetherium Launcher..."
dotnet build $launcherProject -c $Configuration -r win-x86 -p:AetheriumVersion=$version
if ($LASTEXITCODE -ne 0) {
    throw "Aetherium Launcher build failed with exit code $LASTEXITCODE."
}

Write-Host "Testing native client patch plans..."
dotnet run `
    --project $patchTestsProject `
    -c $Configuration `
    -r win-x86 `
    -p:AetheriumVersion=$version
if ($LASTEXITCODE -ne 0) {
    throw "Native client patch tests failed with exit code $LASTEXITCODE."
}

Write-Host "Publishing Aetherium Launcher..."
dotnet publish $launcherProject `
    -c $Configuration `
    -r win-x86 `
    --self-contained true `
    -p:AetheriumVersion=$version
if ($LASTEXITCODE -ne 0) {
    throw "Aetherium Launcher publish failed with exit code $LASTEXITCODE."
}

if ($LauncherOnly) {
    exit 0
}

$innoCompilerCandidates = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
$innoCompiler = $innoCompilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $innoCompiler) {
    throw "Inno Setup 6 compiler (ISCC.exe) was not found."
}

New-Item -ItemType Directory -Path $installerOutput -Force | Out-Null

Write-Host "Compiling Aetherium Play Setup..."
& $innoCompiler "/DMyAppVersion=$version" "/O$installerOutput" $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Aetherium Play Setup compilation failed with exit code $LASTEXITCODE."
}

$setupPath = Join-Path $installerOutput "AetheriumPlaySetup.exe"
if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "Setup compilation completed without producing: $setupPath"
}

$setupHash = Get-FileHash -Algorithm SHA256 -LiteralPath $setupPath
Write-Host ""
Write-Host "Release: $setupPath"
Write-Host "Version: $version"
Write-Host "SHA-256: $($setupHash.Hash)"
