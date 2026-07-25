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

- Account / password fields and multi-account profiles (Room menu)
- Dual-client for DM-era `client.exe` (mutex gate + DAT isolation under `multiclient\`)
- Legacy launch switches (`-a`, `-h`, `-p`, optional `-v`, `-z`, `-nd`)
- Seeds graphics registry values; buttons for `ACD3DSetup.exe` / `ACSET.EXE`
- Default and PK skins
- Settings in `<install>\launcher.json` (fallback: `%LocalAppData%\AcLegacyLauncher\`)

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
3. Tag and push the tag:

```powershell
git tag v1.0.8
git push origin v1.0.8
```

4. GitHub Actions (`.github/workflows/release.yml`) builds the installer and
   creates a GitHub Release with `AetheriumPlaySetup.exe` attached.

You can also run the **Release** workflow manually from the Actions tab.

Repo must be public (or players need a token) for the update check API to work
without auth.

Optional: `Installer\AetheriumLauncher.iss` installs the launcher into an
**existing** Dark Majesty folder (no full game bootstrap). The player-facing
path is `AetheriumPlay.iss` only.
