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

Write-Host "Packaging Linux wrapper..."
$publishDir = Join-Path $projectRoot "AetheriumLauncher\bin\$Configuration\net8.0-windows\win-x86\publish"
$linuxRoot = Join-Path $projectRoot "artifacts\linux"
$linuxOut = Join-Path $linuxRoot "AetheriumPlay-linux"
if (-not (Test-Path -LiteralPath $publishDir)) {
    throw "Launcher publish output was not found: $publishDir"
}
if (Test-Path -LiteralPath $linuxRoot) {
    Remove-Item -LiteralPath $linuxRoot -Recurse -Force
}
New-Item -ItemType Directory -Path (Join-Path $linuxOut "launcher") -Force | Out-Null
Copy-Item -Path (Join-Path $publishDir "*") -Destination (Join-Path $linuxOut "launcher") -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot "linux\AetheriumPlay.sh") -Destination $linuxOut
Copy-Item -LiteralPath (Join-Path $projectRoot "linux\aetherium-play.desktop") -Destination $linuxOut
Copy-Item -LiteralPath (Join-Path $projectRoot "linux\README.md") -Destination $linuxOut
$linuxScript = Join-Path $linuxOut "AetheriumPlay.sh"
$linuxScriptText = [System.IO.File]::ReadAllText($linuxScript) -replace "`r`n", "`n" -replace "`r", "`n"
$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($linuxScript, $linuxScriptText, $utf8)
$linuxTar = Join-Path $linuxRoot "AetheriumPlay-linux.tar.gz"
if (Test-Path -LiteralPath $linuxTar) {
    Remove-Item -LiteralPath $linuxTar -Force
}
Push-Location $linuxRoot
try {
    & tar -czf "AetheriumPlay-linux.tar.gz" "AetheriumPlay-linux"
    if ($LASTEXITCODE -ne 0) {
        throw "Linux tarball failed with exit code $LASTEXITCODE."
    }
} finally {
    Pop-Location
}
if (-not (Test-Path -LiteralPath $linuxTar)) {
    throw "Linux packaging completed without producing: $linuxTar"
}
$linuxHash = Get-FileHash -Algorithm SHA256 -LiteralPath $linuxTar
"$($linuxHash.Hash) *AetheriumPlay-linux.tar.gz" | Set-Content `
    -LiteralPath "$linuxTar.sha256" -Encoding ascii -NoNewline
Write-Host "Linux bundle: $linuxTar"
Write-Host "Linux SHA-256: $($linuxHash.Hash)"

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
Write-Host "Linux: $linuxTar"
Write-Host "Linux SHA-256: $($linuxHash.Hash)"
