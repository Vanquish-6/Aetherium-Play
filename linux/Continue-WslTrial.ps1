# Maintainer helper: continue a WSL smoke test after Windows enables WSL2.
# This is not the player Linux path. Players use AetheriumPlay-linux.tar.gz on
# a real Linux desktop (see linux/README.md). WSL will feel slow.
#
# Run from PowerShell after reboot if WSL needed Virtual Machine Platform:
#   powershell -ExecutionPolicy Bypass -File .\linux\Continue-WslTrial.ps1

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path -LiteralPath (Join-Path $repo "linux\AetheriumPlay.sh"))) {
    $repo = "C:\AetheriumPlay"
}

$bundle = Join-Path $repo "artifacts\linux\AetheriumPlay-linux.tar.gz"
$importDir = Join-Path $repo "artifacts\linux-import"
$log = Join-Path $env:TEMP "aetherium-wsl-trial.log"

function Write-Trial([string]$Message) {
    $line = "[{0}] {1}" -f (Get-Date -Format "s"), $Message
    Add-Content -LiteralPath $log -Value $line
    Write-Host $line
}

"=== Aetherium WSL trial $(Get-Date -Format o) ===" | Set-Content -LiteralPath $log
Write-Trial "Repo $repo"

$wslStatus = & wsl.exe --status 2>&1 | Out-String
Write-Trial $wslStatus.Trim()
if ($wslStatus -match "virtualization is not enabled" -or
    $wslStatus -match "unable to start") {
    throw "WSL2 still cannot start. Reboot Windows, then run this script again."
}

Write-Trial "Installing Ubuntu if needed..."
& wsl.exe --install --distribution Ubuntu --no-launch
$distros = & wsl.exe -l -q 2>&1 | Out-String
Write-Trial "Distros:`n$distros"
if ($distros -notmatch "Ubuntu") {
    throw "Ubuntu distro is not installed. Run: wsl --install -d Ubuntu"
}

Write-Trial "Bootstrapping Ubuntu (root, noninteractive)..."
& wsl.exe -d Ubuntu -u root -- bash -lc "id && uname -m && echo DISPLAY=`$DISPLAY WAYLAND_DISPLAY=`$WAYLAND_DISPLAY"
if ($LASTEXITCODE -ne 0) {
    throw "Ubuntu failed to start. Complete the Ubuntu first-run window if it appeared, then rerun this script."
}

$apt = @'
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive
apt-get update -y
apt-get install -y curl tar unzip ca-certificates \
  libx11-6 libxext6 libxcomposite1 libxcursor1 libxfixes3 libxi6 libxrandr2 libxrender1 libxinerama1 \
  libfreetype6 libfontconfig1 libpng16-16 libgnutls30 libgl1 libglib2.0-0 \
  libpulse0 libxkbcommon0 libwayland-client0 mesa-vulkan-drivers || true
apt-get install -y libasound2t64 || apt-get install -y libasound2 || true
apt-get install -y libpng16-16t64 || true
'@
Write-Trial "Installing Wine host libraries..."
$apt | & wsl.exe -d Ubuntu -u root -- bash -s
if ($LASTEXITCODE -ne 0) {
    throw "apt-get failed inside Ubuntu."
}

if (-not (Test-Path -LiteralPath $bundle)) {
    throw "Missing Linux bundle: $bundle. Rebuild with Build.ps1 -LauncherOnly."
}
if (-not (Test-Path -LiteralPath (Join-Path $importDir "portal.dat"))) {
    throw "Missing import folder: $importDir"
}

Write-Trial "Unpacking Linux bundle in Ubuntu..."
$setup = @'
set -euo pipefail
mkdir -p "$HOME/AetheriumPlay-linux"
cd "$HOME"
rm -rf "$HOME/AetheriumPlay-linux"
tar -xzf /mnt/c/AetheriumPlay/artifacts/linux/AetheriumPlay-linux.tar.gz -C "$HOME"
# tarball contains AetheriumPlay-linux/
cd "$HOME/AetheriumPlay-linux"
chmod +x AetheriumPlay.sh
export AETHERIUM_SETUP_ONLY=1
./AetheriumPlay.sh --import "/mnt/c/AetheriumPlay/artifacts/linux-import"
'@
$setup | & wsl.exe -d Ubuntu -- bash -s
if ($LASTEXITCODE -ne 0) {
    throw "AetheriumPlay.sh --import failed. See ~/.local/share/aetherium-play/logs/linux-setup.log inside Ubuntu."
}

Write-Trial "Import succeeded. Starting launcher (GUI via WSLg)..."
Start-Process -FilePath "wsl.exe" -ArgumentList @(
    "-d", "Ubuntu", "--", "bash", "-lc",
    'cd "$HOME/AetheriumPlay-linux" && ./AetheriumPlay.sh'
)
Write-Trial "Launcher process started. A parchment window should appear on the Windows desktop."
Write-Trial "Logs: wsl -d Ubuntu -- cat ~/.local/share/aetherium-play/logs/linux-setup.log"
Write-Trial "Done."
