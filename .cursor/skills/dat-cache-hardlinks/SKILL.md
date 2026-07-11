---
name: dat-cache-hardlinks
description: Modify DAT download, caching, registry, and hard-link instance prep in ShadowLauncher. Veteran AC knowledge of client_*.dat files, per-server content, and multi-box directory strategies. Use when touching DAT or instance-prep code.
---

# DAT Cache and Hard Links

## Background — the DAT problem

Asheron's Call reads world data from fixed filenames in the client directory:

| File | Contents |
|------|----------|
| `client_portal.dat` | Portal/landblock data — which world you load |
| `client_cell_1.dat` | Cell geometry |
| `client_local_English.dat` | UI strings, localized text |
| `client_highres.dat` | High-res textures (optional, pre-2003 patch) |
| `acclient.exe` | Sometimes replaced by emulator-specific builds |

Retail AC ended at a specific patch level. Private servers — Dark Majesty, Seedsow, GDLE custom content, ACE forks — ship different DAT sets. Connect with the wrong portal DAT and you get wrong terrain, missing content, or immediate disconnect.

**The old workflow:** maintain `C:\AC-Retail\`, `C:\AC-MyServer\`, copy 1–2 GB between them, or keep a "current server" symlink and swap manually. Every server switch was error-prone. ThwargLauncher copied/symlinked per instance. ShadowLauncher caches each known set once and hard-links into per-launch instance directories — same isolation, fraction of the disk.

Players must never think about this. The launcher resolves the server's DAT set, ensures the cache is populated, and builds the instance dir before Decal injection.

## When to use this skill

- DAT download, extraction, cache validation
- Hard-link instance preparation
- `DatRegistry.xml` integration
- New server DAT source types
- Wrong-world / missing-content bugs
- ACBase copy for Program Files installs

## Data flow

```
Server (UserServerList.xml)
  ├─ DatSetId              → community DatRegistry.xml
  ├─ CustomDatZipUrl       → direct or GitHub release zip
  └─ CustomDatRegistryPath → local folder (Dat Developer Mode)

DatSetService
  ├─ EnsureCustomDatSourceReadyAsync()
  ├─ DownloadMissingFilesAsync()         (DAT Manager UI)
  ├─ IsDatSetReadyAsync()
  └─ CompleteDatCacheFromRetailAsync()   (fill gaps from retail)

HardLinkLauncher.PrepareInstanceAsync()
  ├─ ResolveDataSourceDir()
  ├─ CreateInstanceDirectory()           → Instances\{guid}\
  ├─ LinkRuntimeFiles()                  → DLLs from ACBase
  ├─ Hard link client_*.dat from cache
  └─ Hard link acclient.exe (custom if in cache, else ACBase)
```

## Cache layout

```
%LocalAppData%\ShadowLauncher\
  DatSets\
    dark-majesty\                ← registry DatSetId
      client_portal.dat
      client_cell_1.dat
      client_local_English.dat
      client_highres.dat         (optional)
      acclient.exe               (optional emulator build)
      .version                   (GitHub release tag for re-download detection)
    my-server\                   ← CustomDatZipUrl (sanitized server name)
  ACBase\                        ← writable copy of AC install (Program Files case)
  Instances\
    {guid}\                      ← ephemeral, one per launch
      acclient.exe               → hard link
      client_*.dat               → hard links from cache
      *.dll, controls\           → hard links from ACBase
```

Hard links share inodes — zero extra disk for the DAT files themselves. A 500 MB portal DAT exists once in `DatSets\` and can back dozens of concurrent instance dirs.

## Source resolution

`HardLinkLauncher.ResolveDataSourceDir()` / `GameLauncher.ResolveInstancePathAsync()`:

| Condition | Source |
|-----------|--------|
| No DatSetId / `retail`, no custom source | ACBase (retail DATs) |
| `CustomDatRegistryPath` | Local path (highest priority) |
| `CustomDatZipUrl` | `%DatSets%/{sanitized name}/` |
| `DatSetId` (non-retail) | `%DatSets%/{datSetId}/` |

Live lookup: server with no `DatSetId` but matching `<Server name="..."/>` in `DatRegistry.xml` gets resolved at launch — covers servers added before the registry mapped them.

## Hard link rules

### Same volume

`CreateHardLink` requires source and target on the same NTFS volume. AC under `Program Files` is a different permission context and often a different volume layout than `%LocalAppData%`. `FirstRunService.PrepareHardLinkBaseAsync()` copies once to `ACBase\` — same trick every launcher author eventually discovers.

Win32 error **1142** (`ERROR_NOT_SAME_DEVICE`) = volume mismatch. Check ACBase copy completed.

### Link order in HardLinkLauncher

1. **Runtime files from ACBase** — `.dll`, `.exe`, `.dat`, `.xsd` recursively (including `controls\Controls.dll`). Skip known DAT names, skip `acclient.exe`, skip `backup\` and `plugins\`.
2. **DAT overrides** — hard link each `KnownDatFiles` entry from cache dir.
3. **acclient.exe** — custom from cache if present, else ACBase.

### Never hard-link these

`.log`, `.ini`, `.pdb`, `.bin`, `.avi`, `.txt`, `.rtf`, `.msi`

Hard links share inodes. Two acclient instances writing the same `acclient.log` = file lock contention. The client surfaces this as a **DirectX initialization error** — classic multi-box red herring that wastes hours if you don't know the inode sharing issue.

Each instance dir should have its own `.ini`/`.log` (not linked), or none at all (client creates fresh ones in CWD).

### Instance cleanup

`WatchAndCleanupAsync()` → wait for exit + 2s → delete instance dir file-by-file. Some links may resist deletion if another running instance shares the inode (same DLL hard-linked from ACBase). `CleanupStaleInstances()` on startup catches orphans.

Only delete `Instances\{guid}\` — **never** delete `DatSets\` cache during cleanup.

## DatRegistry.xml

Community-maintained mapping of server names → DAT sets. Fetched by `DatRegistryDownloader`, cached atomically to AppData.

```xml
<DatRegistry>
  <DatSet id="dark-majesty" name="Dark Majesty" version="1.0">
    <Description>Dark Majesty expansion content</Description>
    <Zip url="https://github.com/owner/repo/releases/latest"/>
    <Servers>
      <Server name="Dark Majesty"/>
    </Servers>
  </DatSet>
</DatRegistry>
```

Default URL: `https://raw.githubusercontent.com/jakerange52/ac-dat-registry/main/DatRegistry.xml`

## Download behavior

- **GitHub release URLs** — `GitHubReleaseResolver` resolves latest asset; `.version` sidecar tracks tag
- **Direct zip** — extract only known AC filenames (ignore junk in archive)
- **Partial sets** — `CompleteDatCacheFromRetailAsync()` copies missing DATs from retail ACBase (common for servers that only override portal/cell)
- Server zips that include a custom `acclient.exe` get it cached too

## Launch integration

| Server type | Instance dir? | CWD |
|-------------|--------------|-----|
| Retail (no DatSetId) | No | Client install dir |
| Registry/custom DAT | Yes | `Instances\{guid}\` |

Decal injection runs in both cases. Custom DAT servers **require** instance prep — acclient must see the right portal DAT in CWD or `-rodat on` loads the wrong world.

## Dat Developer Mode

`AppConfiguration.DatDeveloperMode` enables:

- `CustomDatRegistryPath` — point at a local dev DAT folder
- `CustomDatZipUrl` — shared zip for the dev team

Local path beats zip URL. This mirrors how server devs passed DAT folders around before registries existed.

## SymlinkLauncher (dormant)

`SymlinkLauncher` is the ThwargLauncher-style approach — symlinks/junctions instead of hard links. Commented out in `ServiceBootstrapper.cs`. Symlinks need Developer Mode or `SeCreateSymbolicLinkPrivilege`. Hard links need nothing. Don't switch without explicit request.

## Debugging — veteran checklist

1. Wrong world / missing dungeons → wrong `client_portal.dat` linked. Check `ResolveDataSourceDir` output in logs.
2. Server added manually with no DatSetId → live registry lookup should catch it; verify `DatRegistry.xml` has the server name
3. `Creating hard-link instance at {Dir} (base=..., datSource=...)` — datSource should be cache dir, not ACBase, for custom servers
4. `HardLinkBasePath` in settings — ACBase copy done?
5. Error 1142 → volume mismatch
6. DirectX error on multi-box → shared `.log`/`.ini` hard link (check skip list)
7. Partial zip missing cell DAT → `CompleteDatCacheFromRetailAsync` should fill; check warnings in log

## Safe changes

- Add DAT filename to both `InstanceLauncherBase.KnownDatFiles` and `DatSetService.KnownAcFileNames`
- New download source type on `Server` model
- Better cache validation / UI feedback in DAT Manager
- Logging around link creation

## Do not

- Make players copy/swap DATs manually
- Hard-link logs or inis
- Delete `DatSets\` during instance cleanup
- Enable symlinks without privilege handling
- Skip retail backfill for partial custom sets
- Symlink or copy full 2 GB when a hard link suffices

## Key files

| File | Role |
|------|------|
| `Services/Dats/DatSetService.cs` | Cache, download, validation |
| `Infrastructure/Native/HardLinkLauncher.cs` | Instance hard-link tree |
| `Infrastructure/Native/InstanceLauncherBase.cs` | KnownDatFiles, cleanup |
| `Application/FirstRunService.cs` | ACBase copy |
| `Infrastructure/WebServices/DatRegistryDownloader.cs` | Registry fetch |
| `Core/Models/Server.cs` | DatSetId, CustomDat* properties |
| `Services/Launching/GameLauncher.cs` | Launch-time DAT resolution |

## Verification

1. **Retail** — no `Instances\`, correct world, `-rodat` respected
2. **Registry DAT set** — cache via DAT Manager, instance dir, correct world
3. **Multi-box same custom server** — two `Instances\{guid}\`, same world in both, no DirectX log errors
4. **Program Files install** — ACBase copy once, links succeed
5. **Partial zip** — missing cell DAT backfilled from retail
