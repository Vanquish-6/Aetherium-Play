#!/usr/bin/env bash
# Aetherium Play for Linux.
# Players click this script. It downloads a private Wine runtime, extracts the
# Dark Majesty files without the 2004 wizard, then starts the Windows launcher
# (A09 included) inside that prefix.
set -euo pipefail

usage() {
  cat <<'EOF'
Aetherium Play for Linux

Usage:
  AetheriumPlay.sh                 First-run setup if needed, then open the launcher
  AetheriumPlay.sh --import DIR    Copy an existing Dark Majesty folder into the prefix
  AetheriumPlay.sh --install-desktop
  AetheriumPlay.sh --help

Data lives in $XDG_DATA_HOME/aetherium-play (default: ~/.local/share/aetherium-play).
Wine is downloaded into that folder. You do not install Wine yourself.

Environment:
  AETHERIUM_DATA_DIR    Override the data directory
  AETHERIUM_WINE        Use this wine binary instead of the bundled runtime
  AETHERIUM_WINEDEBUG   Wine debug channels (default: -all)
  AETHERIUM_SETUP_ONLY  If 1, finish install/import without opening the launcher
EOF
}

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
DATA_DIR="${AETHERIUM_DATA_DIR:-${XDG_DATA_HOME:-$HOME/.local/share}/aetherium-play}"
WINE_VERSION="10.0"
WINE_DIST="wine-${WINE_VERSION}-staging-amd64-wow64"
WINE_URL="https://github.com/Kron4ek/Wine-Builds/releases/download/${WINE_VERSION}/${WINE_DIST}.tar.xz"
WINE_SHA256="03488c1b4e2aebdc102d73c9360da8cf0a753eda927667a9cfd84ec32ebb7e2b"
SEVENZ_URL="https://www.7-zip.org/a/7z2501-linux-x64.tar.xz"
SEVENZ_SHA256="4ca3b7c6f2f67866b92622818b58233dc70367be2f36b498eb0bdeaaa44b53f4"
LAUNCHER_WIN='C:\AetheriumPlay\AetheriumLauncher.exe'
GAME_WIN='C:\Turbine\Asheron'"'"'s Call'
BOOTSTRAP_WIN='C:\ProgramData\AetheriumPlay\Bootstrap'
LOG_DIR="$DATA_DIR/logs"
LOG_FILE="$LOG_DIR/linux-setup.log"

log() {
  mkdir -p "$LOG_DIR"
  local line="[$(date -Iseconds)] $*"
  printf '%s\n' "$line" | tee -a "$LOG_FILE"
}

die() {
  log "ERROR: $*"
  exit 1
}

need_cmd() {
  command -v "$1" >/dev/null 2>&1 || die "Missing required command: $1"
}

sha256_file() {
  sha256sum -b "$1" | awk '{print tolower($1)}'
}

download_verified() {
  local url=$1 dest=$2 expected=$3
  if [[ -f "$dest" ]]; then
    local actual
    actual=$(sha256_file "$dest")
    if [[ "$actual" == "$(echo "$expected" | tr 'A-Z' 'a-z')" ]]; then
      return 0
    fi
    rm -f "$dest"
  fi
  mkdir -p "$(dirname "$dest")"
  log "Downloading $(basename "$dest")..."
  curl -fL --retry 3 --retry-delay 2 -o "$dest" "$url"
  local actual
  actual=$(sha256_file "$dest")
  [[ "$actual" == "$(echo "$expected" | tr 'A-Z' 'a-z')" ]] || die "Checksum mismatch for $(basename "$dest") (got $actual)"
}

find_launcher_payload() {
  local candidates=(
    "$SCRIPT_DIR/launcher"
    "$SCRIPT_DIR/../AetheriumLauncher/bin/Release/net8.0-windows/win-x86/publish"
    "$SCRIPT_DIR/../AetheriumLauncher/bin/Release/net8.0-windows/win-x86"
  )
  local dir
  for dir in "${candidates[@]}"; do
    if [[ -f "$dir/AetheriumLauncher.exe" ]]; then
      printf '%s\n' "$dir"
      return 0
    fi
  done
  return 1
}

ensure_host() {
  [[ "$(uname -s)" == Linux ]] || die "This wrapper is for Linux."
  [[ "$(uname -m)" == x86_64 ]] || die "This wrapper needs x86_64 Linux (Wine wow64)."
  need_cmd curl
  need_cmd tar
  need_cmd sha256sum
  need_cmd find
  if [[ -z "${DISPLAY:-}" && -z "${WAYLAND_DISPLAY:-}" ]]; then
    die "A display is required. The first-run download windows and the launcher are graphical."
  fi
}

ensure_runtime() {
  mkdir -p "$DATA_DIR/cache" "$DATA_DIR/runtime" "$DATA_DIR/tools"
  if [[ -n "${AETHERIUM_WINE:-}" ]]; then
    [[ -x "$AETHERIUM_WINE" ]] || die "AETHERIUM_WINE is not executable: $AETHERIUM_WINE"
    WINE="$AETHERIUM_WINE"
    WINESERVER="${AETHERIUM_WINESERVER:-$(dirname "$WINE")/wineserver}"
    log "Using override wine: $WINE"
    return 0
  fi

  local wine_root="$DATA_DIR/runtime/$WINE_DIST"
  if [[ ! -x "$wine_root/bin/wine" ]]; then
    local archive="$DATA_DIR/cache/${WINE_DIST}.tar.xz"
    download_verified "$WINE_URL" "$archive" "$WINE_SHA256"
    log "Extracting bundled Wine $WINE_VERSION staging wow64..."
    rm -rf "$wine_root"
    mkdir -p "$DATA_DIR/runtime"
    tar -xJf "$archive" -C "$DATA_DIR/runtime"
    [[ -x "$wine_root/bin/wine" ]] || die "Wine runtime extracted without bin/wine."
  fi
  WINE="$wine_root/bin/wine"
  WINESERVER="$wine_root/bin/wineserver"
  export PATH="$wine_root/bin:$PATH"

  local sevenz_root="$DATA_DIR/tools/7zip"
  SEVENZ="$sevenz_root/7zz"
  if [[ ! -x "$SEVENZ" ]]; then
    local archive="$DATA_DIR/cache/7z2501-linux-x64.tar.xz"
    download_verified "$SEVENZ_URL" "$archive" "$SEVENZ_SHA256"
    mkdir -p "$sevenz_root"
    tar -xJf "$archive" -C "$sevenz_root"
    [[ -x "$SEVENZ" ]] || die "7-Zip extracted without 7zz."
  fi
}

configure_wine_env() {
  export WINEPREFIX="$DATA_DIR/prefix"
  export WINEDEBUG="${AETHERIUM_WINEDEBUG:--all}"
  # Keep Wine's mscoree stub. Disabling it makes .NET 8 self-contained
  # fail with "mscoree.dll not found, IL-only binary System.Runtime.dll".
  export WINEDLLOVERRIDES="mshtml=;winemenubuilder.exe=d;ddraw=builtin,b;d3dimm=builtin,b"
  unset WINEARCH
  mkdir -p "$WINEPREFIX"
}

init_prefix() {
  if [[ ! -f "$WINEPREFIX/system.reg" ]]; then
    log "Creating the private Wine prefix (first time only)..."
    "$WINE" wineboot --init >/dev/null 2>>"$LOG_FILE" || true
    "$WINESERVER" -w || true
  fi
}

unix_game_dir() {
  printf '%s\n' "$WINEPREFIX/drive_c/Turbine/Asheron's Call"
}

unix_launcher_dir() {
  printf '%s\n' "$WINEPREFIX/drive_c/AetheriumPlay"
}

unix_disk1_dir() {
  printf '%s\n' "$WINEPREFIX/drive_c/ProgramData/AetheriumPlay/Bootstrap/legacy/Disk1"
}

game_payload_present() {
  local dir
  dir=$(unix_game_dir)
  [[ -s "$dir/portal.dat" && -s "$dir/cell.dat" ]]
}

game_ready() {
  local dest launcher
  dest=$(unix_game_dir)
  launcher=$(unix_launcher_dir)
  game_payload_present \
    && [[ -s "$dest/client.exe" ]] \
    && [[ -f "$launcher/game.install.path" ]]
}

sync_launcher() {
  local source
  source=$(find_launcher_payload) || die "AetheriumLauncher.exe not found. Build the win-x86 launcher or use the Linux bundle that includes launcher/."
  local dest
  dest=$(unix_launcher_dir)
  mkdir -p "$dest"
  log "Installing the Windows launcher into the private prefix..."
  cp -a "$source"/. "$dest/"
  [[ -f "$dest/AetheriumLauncher.exe" ]] || die "Launcher copy failed."
}

run_wine() {
  "$WINE" "$@"
}

run_launcher() {
  run_wine "$LAUNCHER_WIN" "$@"
}

extract_installshield_cabs() {
  local disk1
  disk1=$(unix_disk1_dir)
  local dest
  dest=$(unix_game_dir)
  local tmp="$DATA_DIR/cab-extract"
  [[ -f "$disk1/data1.cab" ]] || die "Disk1 payload is missing data1.cab at $disk1"
  rm -rf "$tmp"
  mkdir -p "$tmp" "$dest"

  log "Extracting Dark Majesty files from the original installer cabinets..."
  local extracted=0
  if [[ -x "${SEVENZ:-}" ]]; then
    if (cd "$disk1" && "$SEVENZ" x data1.cab "-o$tmp" -y >>"$LOG_FILE" 2>&1); then
      extracted=1
    fi
    if [[ -f "$disk1/data2.cab" ]]; then
      (cd "$disk1" && "$SEVENZ" x data2.cab "-o$tmp" -y >>"$LOG_FILE" 2>&1) && extracted=1 || true
    fi
  fi
  if [[ "$extracted" -eq 0 ]] && command -v unshield >/dev/null 2>&1; then
    if (cd "$disk1" && unshield -d "$tmp" x data1.cab >>"$LOG_FILE" 2>&1); then
      extracted=1
    fi
  fi
  [[ "$extracted" -eq 1 ]] || die "Could not extract the InstallShield cabinets. Install 'unshield' and retry, or use --import with a Windows Dark Majesty folder."

  local portal
  portal=$(find "$tmp" -iname 'portal.dat' -print -quit || true)
  [[ -n "$portal" ]] || die "Cabinet extract finished but portal.dat was not in the payload."
  local src
  src=$(dirname "$portal")
  cp -a "$src"/. "$dest/"
  [[ -s "$dest/portal.dat" && -s "$dest/cell.dat" ]] || die "Game folder is still incomplete after cabinet extract."
  log "Game files laid out at $dest"
}

import_game_dir() {
  local source=$1
  [[ -d "$source" ]] || die "Import directory does not exist: $source"
  [[ -s "$source/portal.dat" && -s "$source/cell.dat" ]] || die "That folder is not a complete Dark Majesty install (need portal.dat and cell.dat)."
  local dest
  dest=$(unix_game_dir)
  mkdir -p "$dest"
  log "Copying existing Dark Majesty files into the private prefix..."
  cp -a "$source"/. "$dest/"
}

install_game() {
  if game_payload_present; then
    log "Existing Dark Majesty files found; skipping cabinet extract."
  else
    log "Downloading and unpacking the original Dark Majesty installer (about 230 MB)..."
    run_launcher --prepare-community-game-installer "$BOOTSTRAP_WIN"
    extract_installshield_cabs
  fi

  log "Installing and verifying the Dark Majesty 1.0.69 client..."
  run_launcher --install-community-client-with-progress "$GAME_WIN"
  log "Writing play.aetherium.ac:9000 and graphics registry values..."
  run_launcher --configure-aetherium-install "$GAME_WIN"
}

install_desktop_entry() {
  local bin_dir="$HOME/.local/bin"
  local app_dir="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
  mkdir -p "$bin_dir" "$app_dir"
  local target="$bin_dir/aetherium-play"
  cat >"$target" <<EOF
#!/usr/bin/env bash
exec "$SCRIPT_DIR/AetheriumPlay.sh" "\$@"
EOF
  chmod +x "$target" "$SCRIPT_DIR/AetheriumPlay.sh"
  cat >"$app_dir/aetherium-play.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Aetherium Play
Comment=Asheron's Call: Dark Majesty on play.aetherium.ac
Exec=$target
Terminal=true
Categories=Game;
StartupNotify=true
EOF
  log "Desktop entry installed. Start 'Aetherium Play' from your app menu, or run $target"
}

cleanup() {
  if [[ -n "${WINESERVER:-}" && -x "${WINESERVER:-}" ]]; then
    "$WINESERVER" -k >/dev/null 2>&1 || true
  fi
}

main() {
  local command=${1:-}
  case "$command" in
    -h|--help)
      usage
      exit 0
      ;;
    --install-desktop)
      [[ "$(uname -s)" == Linux ]] || die "This wrapper is for Linux."
      chmod +x "$SCRIPT_DIR/AetheriumPlay.sh" 2>/dev/null || true
      install_desktop_entry
      exit 0
      ;;
    --import)
      local import_dir=${2:-}
      [[ -n "$import_dir" ]] || die "--import requires a directory"
      shift 2 || true
      ensure_host
      ensure_runtime
      configure_wine_env
      trap cleanup EXIT INT TERM
      init_prefix
      sync_launcher
      import_game_dir "$import_dir"
      install_game
      if [[ "${AETHERIUM_SETUP_ONLY:-0}" == 1 ]]; then
        log "Import complete (setup-only; launcher not started)."
        trap - EXIT INT TERM
        exit 0
      fi
      log "Import complete. Starting Aetherium Play..."
      run_launcher
      ;;
    ""|--play)
      ensure_host
      ensure_runtime
      configure_wine_env
      trap cleanup EXIT INT TERM
      init_prefix
      sync_launcher
      if ! game_ready; then
        install_game
      fi
      if [[ "${AETHERIUM_SETUP_ONLY:-0}" == 1 ]]; then
        log "Setup complete (setup-only; launcher not started)."
        trap - EXIT INT TERM
        exit 0
      fi
      log "Starting Aetherium Play..."
      run_launcher
      ;;
    *)
      usage
      die "Unknown option: $command"
      ;;
  esac
}

main "$@"
