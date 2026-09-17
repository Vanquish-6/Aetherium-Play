# Aetherium Play for Linux (experimental)

This is the same Windows Dark Majesty client and Aetherium launcher, started
through a private Wine runtime. You do not install Wine, Proton, or the 2004
InstallShield wizard. There is no native Linux `client.exe`.

Use a real Linux desktop. WSL works for a smoke test and feels slow because
graphics go through another VM.

## Requirements

- 64-bit Linux (`x86_64`) with an X11 or Wayland desktop
- `curl`, `tar`, `sha256sum`
- About 1 GB free
- Network on first run (Wine ~64 MB, 7-Zip ~2 MB, Dark Majesty ~230 MB, client)

Debian / Ubuntu host libraries the bundled Wine needs:

```bash
sudo apt install curl tar ca-certificates \
  libx11-6 libxext6 libxcomposite1 libxcursor1 libxfixes3 libxi6 \
  libxrandr2 libxrender1 libxinerama1 libfreetype6 libfontconfig1 \
  libpng16-16t64 libgnutls30t64 libgl1 libglib2.0-0t64 libpulse0 \
  libxkbcommon0 libwayland-client0 libasound2t64 mesa-vulkan-drivers
```

Older distros may still package `libpng16-16`, `libgnutls30`, `libglib2.0-0`,
and `libasound2` without the `t64` suffix. Optional: `unshield` if cabinet
extract fails.

Create an account at [https://aetherium.ac](https://aetherium.ac) first.

## Install from a GitHub Release

1. Download `AetheriumPlay-linux.tar.gz` (and the `.sha256` if you want to
   check it) from
   [GitHub Releases](https://github.com/Vanquish-6/Aetherium-Play/releases).
2. Extract and start:

```bash
tar -xzf AetheriumPlay-linux.tar.gz
cd AetheriumPlay-linux
chmod +x AetheriumPlay.sh
./AetheriumPlay.sh --install-desktop   # optional app-menu shortcut
./AetheriumPlay.sh
```

First run downloads Wine into `~/.local/share/aetherium-play`, extracts
`portal.dat` / `cell.dat` from the verified cabinets (no 2004 wizard), overlays
the hashed 1.0.69 `client.exe`, then opens the parchment launcher. Sign in as
usual. The first download can take several minutes.

Later launches are the same command, or **Aetherium Play** from the app menu.

### Already have a Windows Dark Majesty folder

```bash
./AetheriumPlay.sh --import "/path/to/Asheron's Call"
```

That folder must contain `portal.dat` and `cell.dat`. The wrapper still verifies
and overlays the supported 1.0.69 client.

## Updates

Linux is not updated by **Help → Check for Updates** (that path installs
`AetheriumPlaySetup.exe` on Windows). Download the newer
`AetheriumPlay-linux.tar.gz`, extract it over the previous folder, and run
`AetheriumPlay.sh` again. Game data stays in `~/.local/share/aetherium-play`.

## What it does not do

- Native Linux play. The process is still Windows `client.exe` under Wine.
- dgVoodoo. Wine's DirectDraw is used instead.
- Vintage Decal is unchanged and likely will not work.
- Killing the wrapper kills wineserver, which ends the launcher and client.

Logs: `~/.local/share/aetherium-play/logs/linux-setup.log`

## Overrides

| Variable | Purpose |
|---|---|
| `AETHERIUM_DATA_DIR` | Data / prefix location |
| `AETHERIUM_WINE` | Use this `wine` binary instead of the pinned Kron4ek build |
| `AETHERIUM_WINEDEBUG` | Wine debug channels (default `-all`) |
| `AETHERIUM_SETUP_ONLY` | `1` finishes install/import without opening the launcher |

Pinned runtime: Kron4ek Wine 10.0 staging amd64 wow64. Do not set `WINEARCH=win32`.

## Build the Linux bundle (maintainers)

On Windows, `.\Build.ps1` (or `.\Build.ps1 -LauncherOnly`) writes
`artifacts\linux\AetheriumPlay-linux.tar.gz`. The GitHub **Release** workflow
attaches that tarball next to `AetheriumPlaySetup.exe`. See the Linux notes in
the root `README.md` ship list.
