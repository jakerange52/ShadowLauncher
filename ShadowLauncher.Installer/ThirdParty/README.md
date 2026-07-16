# Installer staging (ThirdParty)

`Build-Installer.ps1` builds ShadowFilter from source and stages these files here before the MSI bind:

| File | Source |
|---|---|
| `ShadowFilter/ShadowFilter.dll` | `ShadowFilter` project (Release net472) |
| `ShadowFilter/Newtonsoft.Json.dll` | ShadowFilter dependency |

They are not committed (see `.gitignore`).

`Decal.Adapter.dll` is required only to *compile* ShadowFilter — place it under `externals/Decal/` (see `externals/Decal/README.md`). Decal itself is not bundled in the installer; users register ShadowFilter in Decal Agent via **Settings → Help**.

See `THIRD_PARTY.md` at the repo root for attribution.
