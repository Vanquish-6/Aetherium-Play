# Changelog

## 1.0.30 - 2026-08-27

### Fixed

- 1.0.29 crashed at startup with `{app} constant before it was initialized`
  because the Aetherium Play folder check ran during `InitializeWizard`. That
  check now uses `{autopf}` / `{pf32}` and only reads `{app}` after it exists.

## 1.0.29 - 2026-08-27

### Fixed

- First-time setup treated InstallShield's `setup.exe` stub exit as "the game
  is installed," then opened a folder picker that defaulted to Aetherium Play
  under Program Files. Players who accepted that default got
  `Runtime error (at 35:808)`. Setup now waits for the original wizard, tells
  the player not to pick the Aetherium Play folder, and will launch the 2004
  installer again if `client.exe`, `portal.dat`, and `cell.dat` never appear.
- The Dark Majesty archive is about 230 MB. Status text now says a slow
  connection can take several minutes so the download is not mistaken for a
  hang or a finished install.

## 1.0.28 - 2026-08-18

### Fixed

- Open panels that rewrite unchanged numbers hitch because those writers
  still call `ClearAllText`. 1.0.28 skips that rebuild when the integer is
  already on the widget: `TextRegion::SetInt` (signed and unsigned) compares
  glyphs to `%d`/`%u` without calling `GetText`, `StatRegion::SetInt` skips
  unchanged total-XP comma labels, `InfoBox::SetAvailable` skips the
  unchanged unassigned-XP number and its static label while still running
  `SetParent`, and `AllegPanel::SetXPChange` skips unchanged allegiance
  numbers. Global `TextRegion::SetText` stays stock so launch matches 1.0.27.
  Buff-duration skipping and DDD drain hooks are unchanged.

## 1.0.27 - 2026-08-18

### Fixed

- 1.0.26 still prevented the client from opening for updater installs. The
  global `TextRegion::SetText` and `AllegPanel::SetXPChange` hitch skips are
  no longer installed. Buff-duration skipping through `SpellRegion::Update`
  remains. DDD drain hooks are unchanged.

## 1.0.26 - 2026-08-18

### Fixed

- The client failed to open after the XP-label hitch skip. `TextRegion::SetText`
  is thiscall, and the skip path called `GetText` (which clobbers `ecx`) before
  jumping into a trampoline that still did `mov esi, ecx` / `call ClearAllText`.
  The first real label update during UI bring-up therefore ran `ClearAllText` on
  a garbage object. 1.0.26 restores the original TextRegion in `ecx` before that
  fallthrough. The hitch skip is unchanged.

### Added

- Process-local skip of unchanged buff/debuff `m:ss` duration labels
  (`SpellRegion::Update`).
- Process-local skip of unchanged XP and allegiance number labels
  (`TextRegion::SetText` when the glyphs already match, and
  `AllegPanel::SetXPChange` visuals for sworn characters).

These hooks stay in the launched process only. The launcher still does not
patch `client.exe` or DAT files on disk. DDD writer drain hooks are unchanged
from 1.0.25.

## 1.0.25 - 2026-08-02

- Extends the launcher patch for faster DDD updates.
- Adds optional `--game-install` pinning without changing the normal player
  install path.
