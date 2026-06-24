---
name: dat-cache-hardlinks
description: Modify DAT download, caching, registry integration, and hard-link instance preparation in ShadowLauncher. Use when changing how server-specific DAT files are fetched, stored, or linked into per-client directories.
---

# DAT Cache and Hard Links

## Why this matters

Transparent DAT management is a **core invariant**. Players must never manually swap `client_*.dat` files when switching servers or multi-boxing. The launcher:

1. Knows which DAT set each server needs (registry, zip URL, or local path)
2. Downloads and caches files under `%LocalAppData%\ShadowLauncher\DatSets\`
3. Creates a unique instance directory per launch using **hard links**
4. Cleans up instance dirs when the client exits

## When to use this skill

- Changing DAT download, extraction, or cache validation
- Modifying hard-link instance preparation
- Adding server DAT source types
- Updating `DatRegistry.xml` integration
- Debugging wrong/missing DATs at launch
- Changing ACBase copy behavior for protected installs

## Data flow

```
Server definition
  ├─ DatSetId              → community registry (DatRegistry.xml)
  ├─ CustomDatZipUrl       → direct/GitHub zip download
  └─ CustomDatRegistryPath → local folder (Dat Developer Mode)

DatSetService
  ├─ EnsureCustomDatSourceReadyAsync()   (before launch, custom sources)
  ├─ DownloadMissingFilesAsync()         (DAT Manager UI)
  ├─ IsDatSetReadyAsync()                (validate cache)
  └─ CompleteDatCacheFromRetailAsync()   (fill missing DATs from retail)

HardLinkLauncher.PrepareInstanceAsync()
  ├─ ResolveDataSourceDir()              (pick cache dir vs ACBase)
  ├─ CreateInstanceDirectory()           (Instances\{guid}\)
  ├─ LinkRuntimeFiles()                  (DLLs from ACBase, skip DATs/exe)
  ├─ Hard link DAT files from cache
  └─ Hard link acclient.exe (custom from cache if present, else ACBase)
```

## Cache directory layout

```
%LocalAppData%\ShadowLauncher\
  DatSets\
    dark-majesty\              ← registry DatSetId
      client_portal.dat
      client_cell_1.dat
      client_local_English.dat
      client_highres.dat       (optional)
      acclient.exe             (optional custom client)
      .version                 (GitHub release tag sidecar)
    my-custom-server\          ← CustomDatZipUrl (sanitized server name)
      ...
  ACBase\                      ← copy of AC install (Program Files only)
  Instances\
    a1b2c3...\                 ← ephemeral, deleted after client exit
      acclient.exe             → hard link
      client_*.dat             → hard links
      *.dll                    → hard links (from ACBase, recursive)
```

## DAT source resolution

In `HardLinkLauncher.ResolveDataSourceDir()` and `GameLauncher.ResolveInstancePathAsync()`:

| Condition | Source directory |
|-----------|-----------------|
| `DatSetId` empty/null/`retail`, no custom source | ACBase (retail DATs) |
| `CustomDatRegistryPath` set | That local path (highest priority) |
| `CustomDatZipUrl` set | `%DatSets%/{sanitized server name}/` |
| `DatSetId` set (non-retail) | `%DatSets%/{datSetId}/` |

Live registry lookup: if a server has no `DatSetId` but matches a `<Server name="..."/>` entry in `DatRegistry.xml`, the ID is resolved at launch time.

## Known DAT files

Defined in `InstanceLauncherBase.KnownDatFiles`:

- `client_portal.dat`
- `client_cell_1.dat`
- `client_local_English.dat`
- `client_highres.dat` (optional)

Also tracked: `acclient.exe` (may be custom per DAT set).

## Hard link rules

File: `Infrastructure/Native/HardLinkLauncher.cs`

### Same-volume requirement

Hard links only work on the same NTFS volume. If AC is installed under Program Files:

1. `FirstRunService.PrepareHardLinkBaseAsync()` copies to `%LocalAppData%\ShadowLauncher\ACBase\`
2. `HardLinkBasePath` stored in settings
3. All hard links originate from ACBase + DatSets (both on LocalAppData volume)

### Files to hard link

| Step | What | Source |
|------|------|--------|
| 1 | Runtime DLLs/EXEs (recursive subdirs) | ACBase |
| 2 | DAT files | DAT cache (overrides step 1 skips) |
| 3 | acclient.exe | DAT cache if present, else ACBase |

### Files to skip in step 1

- Known DAT filenames and `acclient.exe` (handled in steps 2–3)
- Extensions not in runtime set (only `.dll`, `.exe`, `.dat`, `.xsd`)
- Subdirectories: `backup`, `plugins`
- Write-contended extensions: `.log`, `.ini`, `.pdb`, `.bin`, `.avi`, `.txt`, `.rtf`, `.msi`

**Why skip write-contended files:** hard links share inodes. Two instances linking the same `acclient.log` causes file lock conflicts and misleading DirectX errors.

### Instance cleanup

- `WatchAndCleanupAsync()` waits for process exit + 2s delay, then deletes instance dir
- File-by-file delete (some links may be locked if another instance shares inodes)
- `CleanupStaleInstances()` on startup removes orphaned dirs with no running acclient

## DatRegistry.xml

Fetched by `DatRegistryDownloader` from configurable URL (default: community registry on GitHub).

Schema:

```xml
<DatRegistry>
  <DatSet id="dark-majesty" name="Dark Majesty" version="1.0">
    <Description>...</Description>
    <Zip url="https://github.com/owner/repo/releases/latest"/>
    <File name="client_portal.dat"/>
    <Servers>
      <Server name="Dark Majesty"/>
    </Servers>
  </DatSet>
</DatRegistry>
```

Cached atomically to `%LocalAppData%\ShadowLauncher\DatRegistry.xml`.

## Download behavior

`DatSetService` handles:

- **GitHub release URLs** — resolved via `GitHubReleaseResolver`; `.version` sidecar tracks release tag for re-download
- **Direct zip URLs** — downloaded to temp, extracted (known filenames only)
- **Partial sets** — `CompleteDatCacheFromRetailAsync()` copies missing DATs from retail ACBase
- **Progress reporting** — `DatDownloadProgress` for UI

## Launch integration

In `GameLauncher.ResolveInstancePathAsync()`:

1. Custom source → `EnsureCustomDatSourceReadyAsync()` then instance prep
2. Registry `DatSetId` → validate set exists and `IsDatSetReadyAsync()`, then instance prep
3. Neither → launch directly from configured client path (no instance dir)

Retail servers skip instance prep entirely — Decal injection still applies.

## Dat Developer Mode

`AppConfiguration.DatDeveloperMode` enables per-server overrides:

- `CustomDatRegistryPath` — point at a local DAT folder
- `CustomDatZipUrl` — shared zip for remote developers

Local path takes priority over zip URL.

## Active vs symlink strategy

`ServiceBootstrapper.cs` registers `HardLinkLauncher` as `IInstancePreparer`. `SymlinkLauncher` is dormant (commented out). Do not switch strategies without explicit request — symlinks require Developer Mode or elevated symlink privilege.

## Debugging checklist

1. Check server's `DatSetId`, `CustomDatRegistryPath`, `CustomDatZipUrl` in `UserServerList.xml`
2. Verify cache dir exists and contains expected DAT files
3. Check log for `"Creating hard-link instance at"` with base/datSource paths
4. Confirm `HardLinkBasePath` in settings (ACBase copy completed)
5. Win32 error 1142 (ERROR_NOT_SAME_DEVICE) → volume mismatch; ACBase copy may have failed
6. Wrong world content → wrong cache dir linked; check `ResolveDataSourceDir` logic

## Safe change patterns

- Add new DAT filename to `KnownDatFiles` and `KnownAcFileNames` together
- Add new download source type in `DatSetService` with corresponding `Server` property
- Improve cache validation in `IsDatSetReadyAsync` / `IsFullyDownloaded`
- Add logging around hard link creation (already at Debug level)

## Unsafe change patterns (avoid)

- Requiring manual DAT file copying by the player
- Hard-linking `.log`/`.ini` files into instance dirs
- Deleting shared DAT cache files during instance cleanup (only delete instance dir)
- Using symlinks without privilege handling
- Skipping `CompleteDatCacheFromRetailAsync` for partial custom sets (causes missing DAT warnings)

## Related files

| File | Role |
|------|------|
| `Services/Dats/DatSetService.cs` | Download, cache, validation |
| `Services/Dats/IDatSetService.cs` | Service interface |
| `Infrastructure/Native/HardLinkLauncher.cs` | Hard-link instance creation |
| `Infrastructure/Native/InstanceLauncherBase.cs` | Shared instance logic, KnownDatFiles |
| `Infrastructure/Native/SymlinkLauncher.cs` | Dormant symlink alternative |
| `Application/FirstRunService.cs` | ACBase copy for protected installs |
| `Infrastructure/WebServices/DatRegistryDownloader.cs` | Registry fetch/parse |
| `Core/Models/Server.cs` | DAT-related server properties |
| `Core/Models/DatSet.cs` | Registry dat set model |
| `Services/Launching/GameLauncher.cs` | Launch-time DAT resolution |
| `Presentation/ViewModels/DatFetchViewModel.cs` | DAT Manager UI |

## Verification

On Windows with AC client installed:

1. **Retail server** — no `Instances\` dir; launches from client path
2. **Registry DAT set** — cache populated via DAT Manager; instance dir created; correct world loads
3. **Multi-box same server** — two distinct `Instances\{guid}\` dirs; both clients run
4. **Program Files install** — ACBase copy runs once; subsequent launches hard link successfully
5. **Partial custom zip** — missing DATs filled from retail; launch succeeds
