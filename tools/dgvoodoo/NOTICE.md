# dgVoodoo 2 (third-party, closed source)

Aetherium Play ships a **small subset** of [dgVoodoo 2](https://dege.freeweb.hu/dgVoodoo2/)
by **Dege** so the legacy Dark Majesty `client.exe` can use DirectDraw / Direct3D
on modern Windows (windowed play, dual-client mouse capture tweaks, etc.).

## What we ship

| File | Role |
|------|------|
| `extracted/MS/x86/DDraw.dll` | DirectDraw wrapper (loaded next to `client.exe`) |
| `extracted/MS/x86/D3DImm.dll` | Companion Direct3D Immediate Mode DLL |
| `extracted/dgVoodoo.conf` | Text config (`Version = 0x287` → dgVoodoo **2.87.x**) |

The x86 binaries are dgVoodoo **2.87.5** (product version 2.8.7.5, built 14 September 2026).
That release includes Dege’s fix for the Windows 11 26H1 (build 28000+) crash.
`DDraw.dll` reports file version 4.7.1.3000. 2.87.5 is the build Dege republished
after Microsoft Defender flagged 2.87.4.

There is **no dgVoodoo source code** in this repository. Only redistributable
binary components and a config file are included.

## Why it is here

Dark Majesty expects old DirectDraw APIs that modern Windows does not provide
reliably. On Play (and when launching dual clients), `GraphicsBootstrap` copies
these DLLs into the game folder and adjusts `dgVoodoo.conf` (for example
`CaptureMouse` / watermark) so keyboard and windowed focus work correctly.

## Redistribution (Dege’s terms, summarized)

Per Dege’s published redistribution rights:

- You **may** ship individual dgVoodoo files **with your game or game mod**.
- You **must not** bundle dgVoodoo inside a general-purpose launcher/framework
  meant for arbitrary third-party apps.
- If you host dgVoodoo as a **standalone** download, provide the **full
  unmodified original zip** from Dege — not a partial extract.

Aetherium Play uses dgVoodoo only for this Dark Majesty install path, not as a
generic graphics wrapper for other software.

Upstream:

- Home: https://dege.freeweb.hu/dgVoodoo2/
- Readme / redistribution: https://dege.freeweb.hu/dgVoodoo2/ReadmeGeneral/
- Forum: VOGONS (dgVoodoo)

Copyright (c) 2013–2026 Dege. All rights in dgVoodoo remain with its author.
Aetherium Play authors claim no ownership of dgVoodoo.
