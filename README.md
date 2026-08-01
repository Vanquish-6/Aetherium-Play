```
             Nostalgia just kicked in

                   __      _______ ______
                ╱    /\__/\       //     ╲╲
        ______⊂╱    ( ´∇`  )     // ⊃     ||╲ フ 🡖
      ,´__▔▔▔▔╱  ▔╱▔  ⌒▔▔▔▔╱▔▔▔▔ 🡖▔ ▔▔▔▔▔🡖 ▔▔▔▔ |
    ,╱_ _╱   /-o—/ ___ ╱▔▔╱ ___/\  |     ▔ | /\__|
   ,========————´===================/⌒ ╲=/=======||🡖 ||
   | __  |#rarrisdeadlol|   __ "    |⌒| |/    ___/|  )╯
   )|🞕|_∈≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡∋__|🞕|"  __|| ╯ ╯__ -‒‒‒‒‒┘  ╯
   ▔╲ ▔╲__╯▔▔▔▔▔▔▔▔▔▔▔▔▔▔三三三▔╲  ╲__╯ ▔▔     三三三三╯
     三三三三三三三三三三三三三三三三三三三三三三三三三三三三三三三三三三
       三三三三三三三三三三三三三三三三三三三三三三三三三三三三三三三三三三
```

# Aetherium Play

Developed by **Vanquish**, aka Chosen One.

Windows launcher + all-in-one installer for Asheron's Call: Dark Majesty on
`play.aetherium.ac:9000`. The setup does **not** embed `client.exe`; it downloads
and verifies disclosed community archives, then installs this launcher and
compatibility files.

## Accounts & support

- Create an account at [https://aetherium.ac](https://aetherium.ac)
- Issues or help: [aetheium-ac@proton.me](mailto:aetheium-ac@proton.me)

```
AetheriumPlay/
├── AetheriumLauncher/     # Self-contained .NET 8 WinForms launcher (x86)
├── Installer/             # Inno Setup scripts + player-facing notices
├── ThirdParty/            # Vendored MegaApiClient source (MIT)
├── tools/dgvoodoo/        # Bundled dgVoodoo binaries (no source; see NOTICE)
├── artifacts/             # Build output (gitignored) → AetheriumPlaySetup.exe
└── Build.ps1              # One-shot release build
```

## What players get

`AetheriumPlaySetup.exe` downloads and verifies the disclosed Dark Majesty
installer and `client.exe`, runs the original InstallShield wizard, closes the
obsolete Turbine launcher, installs Aetherium Launcher, and presets
`play.aetherium.ac:9000`.

The player does not need to install .NET separately. Release builds carry the
x86 .NET Desktop runtime required by the launcher.

Launcher features:

- Account / password fields on the parchment form
- Legacy launch switches (`-a`, `-h`, `-p`, optional `-v`, `-z`, `-nd`)
- Seeds graphics registry values; buttons for `ACD3DSetup.exe` / `ACSET.EXE`
- Default and PK skins
- Settings in `<install>\launcher.json` (fallback: `%LocalAppData%\AcLegacyLauncher\`)
- Version 1.0.24 adds hash-gated, process-local DDD acceleration for the
  verified public client. It leaves `client.exe`, `portal.dat`, and `cell.dat`
  untouched by the launcher and fails before resume if either runtime hook
  cannot be installed exactly.

### Process-local DDD acceleration

Version 1.0.24 uses standard Windows process-memory APIs to install two small
runtime hooks while `client.exe` is suspended. They increase the native cache
drain rate, cap each native DAT writer at 32 pending operations, and keep the
patch UI incomplete until both writers drain. The capability exists only in
that process; the launcher does not patch the executable or DAT files on disk.

When the server's launcher requirement is enabled, only a login carrying the
exact A09 capability marker is admitted. An older launcher or direct start
receives the shipped client's native current-version rejection before
authentication or DDD begins. This marker is an admission/version gate, not
server-side proof that the public launcher or monitor is still present.

### A09 anti-tamper disclosure

Before launch, immediately after client resume, and every two seconds while the
client runs, Aetherium Play checks the identity metadata of active programs for
common Cheat Engine builds and verifies the exact client-memory regions
installed by its own A09 patch. It reads active process/image names, exact
top-level program titles, and version-resource identity. To read that version
resource reliably, it resolves each active executable's full path and keeps a
bounded in-memory cache keyed by process start and file metadata. Full paths are
not logged or uploaded. It does not scan directories, enumerate arbitrary
modules, upload a process list, or terminate another program.

If Cheat Engine or an A09 patch change is detected, the launcher refuses to
start or ends only the `client.exe` instance it launched. It writes the reason
locally to `%LocalAppData%\AetheriumPlay\anti-tamper.log`; it does not
automatically ban an account. The launcher monitor remains resident until the
game exits, and Windows ends that client if the launcher is forcibly terminated;
child programs are allowed to break away from that containment. This is a
deterrent for common tools, not cryptographic attestation against a purpose-built
client.

The launcher stays visibly open while monitoring. Choosing **Exit** with a game
running shows a warning; confirming Exit closes the contained client as well.

A09 fails closed if Windows cannot assign the suspended client to that
kill-on-close job. Some sandboxed or otherwise job-constrained launcher hosts,
and Windows versions without compatible nested-job behavior, can therefore be
refused with an explicit containment error instead of launching unmonitored.

The complete launcher implementation is public at
[Vanquish-6/Aetherium-Play](https://github.com/Vanquish-6/Aetherium-Play).
Each release must link its exact source commit, publish the setup SHA-256, and
state its signing status. The current release workflow produces an unsigned
setup and an attached `.sha256` file; it does not claim Authenticode signing.

This behavior may receive extra scrutiny from Defender, SmartScreen, or other
endpoint tools, especially for an unsigned release. Published builds should
include their exact SHA-256 and signing status. Players should not be asked to
disable security software.

## Transparency: downloads and third-party pieces

Player-facing disclosure is also shown during setup (`Installer\AetheriumPlaySources.txt`).

### Community game files (not in this repo)

| Item | Source | Gate |
|------|--------|------|
| DM installer archive | [ACCPP MEGA folder](https://mega.nz/folder/L1MniCKJ#1dQCCFPc2ddcFILa_JGeZw/folder/mlk0DQqR) | size + SHA-256 |
| `client.exe` v1.0.69 | [ACCPP MEGA folder](https://mega.nz/folder/L1MniCKJ#1dQCCFPc2ddcFILa_JGeZw/folder/T00V3ISI) | `2,682,016` bytes · SHA-256 `52DDFDD1BD3AF839A90898C9A2A3BA8983E1811A1F1E45A588B649C5615DD26B` |

Wrong or changed uploads are refused; an existing `client.exe` is left alone.

### MegaApiClient (source in-tree)

MIT-licensed [MegaApiClient 1.10.5](https://github.com/gpailler/MegaApiClient)
lives under `ThirdParty\MegaApiClient`. Local fixes:

- Nested MEGA `/folder/<id>` URLs (library only understands the root share URL)
- Nodes with multiple overlapping share keys (try each key until attributes decrypt)

Regression: `ThirdParty\MegaApiClient\MegaApiClient.Tests\MultipleNodeKeys.cs`.

### dgVoodoo 2 (binaries only — no source)

Closed-source DirectDraw/Direct3D wrapper by **Dege**. We ship only:

- `tools\dgvoodoo\extracted\MS\x86\DDraw.dll`
- `tools\dgvoodoo\extracted\MS\x86\D3DImm.dll`
- `tools\dgvoodoo\extracted\dgVoodoo.conf` (2.87.x)

See [`tools\dgvoodoo\NOTICE.md`](tools/dgvoodoo/NOTICE.md) for purpose,
upstream links, and redistribution summary. Aetherium does not own dgVoodoo and
does not include its source code.

## Build

Requires .NET 8 SDK and [Inno Setup 6](https://jrsoftware.org/isinfo.php).

Version lives in one place: [`version.txt`](version.txt). `Build.ps1`, the
csproj, and Inno Setup all consume it.

```powershell
.\Build.ps1
```

Output: `artifacts\installer\AetheriumPlaySetup.exe`

## Releases & auto-update

Players' launchers check GitHub Releases on startup (and via **Help → Check for
Updates**). If a newer `AetheriumPlaySetup.exe` is published, they get a popup
to download and run the installer.

Update feed (edit if the repo moves): `Vanquish-6/Aetherium-Play` in
`AetheriumLauncher\UpdateChecker.cs`.

### Ship a new build

1. Bump `version.txt` (example: `1.0.8`)
2. Commit and push
3. Complete both destructive bad-DAT recovery canaries on disposable copies:
   force the launcher to end and force a hook-integrity violation during DDD,
   then relaunch and verify readable cell revision 6004 with 524,954 records.
   The already-complete portal must remain unchanged at revision 6002 with
   51,001 records and SHA-256
   `7A47FCCDE084B76DBF4B62DE9D28AD767ED7EA5EDEEC72033955252881BBE6DA`.
   Record an immutable log hash or HTTPS URL.
4. Tag and push the verified commit:

```powershell
git tag v1.0.8
git push origin v1.0.8
```

5. Run the **Release** workflow manually from the Actions tab, provide the tag
   and the recorded DDD canary evidence. The workflow refuses an existing
   release, verifies the tag resolves to the checked-out commit, downloads the
   exact hash-gated public client, and runs active-scan, suspended real
   remote-mutation monitor, and forced-launcher-termination integrations before
   creating the GitHub Release. The required recorded canaries are the
   live/resumed DDD recovery gate. The release attaches
   `AetheriumPlaySetup.exe` and its `.sha256`.

Tag pushes do not publish automatically: the destructive DDD recovery evidence
is a required manual release gate.

Repo must be public (or players need a token) for the update check API to work
without auth.

Optional: `Installer\AetheriumLauncher.iss` installs the launcher into an
**existing** Dark Majesty folder (no full game bootstrap). The player-facing
path is `AetheriumPlay.iss` only.
