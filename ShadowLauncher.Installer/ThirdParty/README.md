# Third-Party Installer Binaries

Place the following file in this directory before building the MSI (if bundling Decal):

| File | Source |
|---|---|
| `Decal_3_0_0_0.exe` | https://www.decaldev.com / community mirror |

`ShadowFilter.dll` and `Newtonsoft.Json.dll` are **built from source** by `Build-Installer.ps1` and staged under `ThirdParty/ShadowFilter/`. They are not committed.

See `externals/Decal/README.md` for the `Decal.Adapter.dll` reference required to compile ShadowFilter.

See `THIRD_PARTY.md` at the repo root for attribution and license details.
