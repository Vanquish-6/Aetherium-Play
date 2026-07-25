# Vendored MegaApiClient

Upstream: https://github.com/gpailler/MegaApiClient  
Version: 1.10.5  
License: MIT (see `LICENSE`)

This tree is a **source-visible** dependency used only for downloading verified
community files from MEGA during install / client bootstrap.

Kept intentionally lean:

- Library project under `MegaApiClient\`
- `LICENSE`, root `README.md`, solution files needed to build
- `MegaApiClient.Tests\MultipleNodeKeys.cs` (and supporting test project) for
  the multi-key decryption regression used by Aetherium

Removed / narrowed vs upstream (not required to build the launcher): nested
`.git`, GitHub workflows/templates, DocFX `docs\`, multi-TFM packaging,
SourceLink, and strong-name signing. The library project targets
`netstandard2.0` only.
