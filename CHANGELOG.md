# Changelog

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
