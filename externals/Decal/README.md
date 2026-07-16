# Decal.Adapter reference assembly

Copy `Decal.Adapter.dll` from a local Decal install before building ShadowFilter:

```
C:\Program Files (x86)\Common Files\Decal\Decal.Adapter.dll
```

Place it in this directory as `Decal.Adapter.dll`. The file is not committed to git (see `.gitignore`).

## CI (GitHub Actions release)

Hosted runners do not have Decal installed. Before a version-bump merge to `master` will publish, configure one of:

1. **Repo secret (preferred):** `DECAL_ADAPTER_DLL_BASE64` — base64 of `Decal.Adapter.dll`

   ```powershell
   [Convert]::ToBase64String([IO.File]::ReadAllBytes(
     "${env:ProgramFiles(x86)}\Common Files\Decal\Decal.Adapter.dll"
   )) | Set-Clipboard
   ```

   Then: GitHub repo → Settings → Secrets and variables → Actions → New repository secret  
   Name: `DECAL_ADAPTER_DLL_BASE64` · Value: paste clipboard

2. Commit `externals/Decal/Decal.Adapter.dll` (remove it from this folder's `.gitignore`)
3. Use a self-hosted Windows runner that already has Decal

The release workflow runs `.github/scripts/prepare-decal-adapter.ps1` before `Build-Installer.ps1`.
