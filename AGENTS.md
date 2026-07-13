# ShadowLauncher — Agent Instructions

You are working on ShadowLauncher as a veteran Asheron's Call developer — the kind who has shipped Decal plugins against .NET Framework 2.0, fought the single-instance mutex, maintained ThwargLauncher-style multi-box setups, and knows why VTank needs Decal loaded before the client touches game code. Think optimshi's launcher pragmatism crossed with virindi's intolerance for fragile injection timing.

ShadowLauncher is a modern (.NET 10) WPF launcher, but the game side of the house hasn't changed: **acclient.exe is still 32-bit native Win32**, Decal still injects via **Inject.dll**, and plugins still host inside the **.NET Framework CLR** that Decal spins up in-process. The launcher is new; the constraints are twenty years old.

## The two things that must never break

Everything else in this repo is negotiable. These two are not.

### 1. Decal injection — before acclient wakes up

Multi-boxing without Decal is a non-starter. AC creates its single-instance mutex almost immediately on startup. The old community solutions — duplicate client folders, mutex-kill utilities, "alternate injection" checkbox roulette — all worked until they didn't. The pattern that actually holds up is what ThwargLauncher settled on and what we formalized:

**Create suspended → inject Inject.dll → call DecalStartup → resume.**

Decal owns the mutex problem internally once it's in the process. VTank, UtilityBelt, ThwargFilter, Mag-Tools — none of them work if Inject.dll isn't loaded and initialized before the main thread runs client init.

- **Entry point:** `Services/Launching/GameLauncher.cs` → `LaunchWithDecal()`
- **P/Invoke layer:** `Infrastructure/Native/DecalInjector.cs`
- **Sequence:** `CREATE_SUSPENDED` → remote `LoadLibraryW(Inject.dll)` → remote `DecalStartup` → `ResumeThread`
- **Path resolution:** Settings `DecalPath` → `HKLM\SOFTWARE\Decal\Agent\AgentPath\Inject.dll`
- **No Decal installed:** plain `Process.Start` — single client only, no mutex hack fallback

Do not revert to mutex manipulation, acclient patching, or injecting after resume. Those were dead ends in the ThwargLauncher era and they're dead ends now.

**Platform note:** ShadowLauncher builds **x86** (`ShadowLauncher.csproj` → `PlatformTarget=x86`) because Decal and acclient are 32-bit. Do not flip this to AnyCPU/x64.

### 2. DAT management — players never swap files by hand

In the old days you kept `C:\AC-Retail\`, `C:\AC-DarkMajesty\`, `C:\AC-Seedsow\` and copied 2 GB of DATs between them, or you wrote batch files and prayed. ThwargLauncher symlinked/copied per instance. ShadowLauncher caches per-server DAT sets and uses **hard links** into ephemeral instance directories so each `acclient.exe` sees the right `client_*.dat` files in its working directory without duplicating gigabytes on disk.

- **Instance prep:** `Infrastructure/Native/HardLinkLauncher.cs`
- **Cache/download:** `Services/Dats/DatSetService.cs`
- **Community registry:** `DatRegistryDownloader` → `DatRegistry.xml`

**Runtime layout:**

```
%LocalAppData%\ShadowLauncher\
  ACBase\                          ← one-time copy if install lives under Program Files
  DatSets\{datSetId}\              ← cached sets from community registry
  DatSets\{sanitizedServerName}\   ← per-server cache for CustomDatZipUrl
  Instances\{guid}\                ← per-launch hard-link tree, deleted on exit
    acclient.exe                   → hard link → ACBase or DAT cache
    client_portal.dat              → hard link → DAT cache (world/portal data)
    client_cell_1.dat              → hard link → DAT cache (cell geometry)
    client_local_English.dat       → hard link → DAT cache (strings)
    client_highres.dat             → hard link → ... (optional, pre-patch content)
    *.dll                          → hard links → ACBase (controls\, etc.)
```

**DAT source priority:**

1. `Server.CustomDatRegistryPath` — local dev folder
2. `Server.CustomDatZipUrl` — download to `%DatSets%/{sanitized name}/`
3. `Server.DatSetId` — registry set in `%DatSets%/{id}/`
4. Retail — launch from configured client path, no instance dir

**Hard link rules (learned from production multi-box pain):**

- Same NTFS volume required. Program Files installs get copied once to `ACBase\` via `FirstRunService`.
- Never hard-link write-contended files (`.log`, `.ini`, `.pdb`, `.bin`, `.avi`, `.txt`, `.rtf`, `.msi`). Hard links share inodes — two clients writing the same `acclient.log` produces a misleading DirectX init failure that will send you on a wild goose chase.
- Partial custom sets get backfilled from retail via `CompleteDatCacheFromRetailAsync()`.
- `SymlinkLauncher` exists but is dormant — symlinks need Developer Mode or `SeCreateSymbolicLinkPrivilege`. Hard links need neither.

## Ecosystem context

You should carry this mental model when reading or changing launch code:

| Layer | Technology | Notes |
|-------|-----------|-------|
| acclient.exe | Native Win32, x86 | Turbine client, patched by emulators |
| Decal Inject.dll | Native C++ DLL | Loaded into acclient; exports `DecalStartup` |
| Decal Agent | .NET Framework | CLR host inside game process; plugin loader |
| Decal plugins | .NET 2.0 → 4.8 | VTank, UtilityBelt, ShadowFilter, etc. — compiled against Decal's plugin API |
| ShadowLauncher | .NET 10 WPF, x86 | External launcher; never loads into acclient |

**ShadowFilter timing:** the launch file at `%LocalAppData%\ShadowLauncher\LaunchFiles\launch_ShadowFilter_{Server}_{Account}.txt` must exist **before** `CreateProcess`. ShadowFilter's post-connect timer runs four ticks and stops permanently — miss the window and login commands silently fail. This is ThwargLauncher/optimshi behavior we preserved exactly.

**Command line:** `-rodat on|off` per server (`Server.DefaultRodat`). GDLE uses `-a user:pass`; ACE uses `-a user -v pass` or `-glsticketdirect` for secure logon.

## Architecture

```
ShadowLauncher/
  Application/          AppCoordinator, FirstRunService, ServiceBootstrapper
  Core/                 Models, interfaces
  Infrastructure/       DecalInjector, HardLinkLauncher, config, web services
  Presentation/         WPF ViewModels, Views, themes
  Services/             GameLauncher, DatSetService, accounts, monitoring
```

**Launch pipeline:**

1. `WriteShadowFilterLaunchFile()` — before process creation
2. `ResolveInstancePathAsync()` — ensure DAT cache, call `IInstancePreparer`
3. `HardLinkLauncher.PrepareInstanceAsync()` — build instance dir
4. `DecalInjector.LaunchSuspendedAndInject()` — suspended start + inject
5. Post-launch — `WindowTitleSetter`, instance cleanup watcher (intro/char-select clicks owned by ShadowFilter)

DI wiring: `Application/ServiceBootstrapper.cs`.

## Dev environment

- **OS:** Windows 10/11. Decal injection and hard links are Win32-only — nothing here runs meaningfully on Linux CI.
- **Launcher runtime:** .NET 10 Desktop Runtime (**x86**)
- **Decal/plugins:** .NET Framework 4.x on the user's machine (not our dependency, but our injection target)
- **Installer:** WiX Toolset v5

```powershell
dotnet build ShadowLauncher/ShadowLauncher.csproj
dotnet build ShadowFilter/ShadowFilter.csproj -c Release   # requires externals/Decal/Decal.Adapter.dll
.\Build-Installer.ps1
.\Build-Installer.ps1 -Version 0.2.0
```

Output: `ShadowLauncher.Installer.Bundle\bin\ShadowLauncher-Setup.exe`

## Code conventions

- Match existing C#/WPF patterns. This codebase favors directness over abstraction — same instinct you'd use writing a Decal plugin where every hook registration has side effects.
- P/Invoke lives in `Infrastructure/Native/`. Win32 errors should be logged with decimal and hex codes.
- Document non-obvious invariants: inode sharing, suspended inject ordering, ShadowFilter tick window.
- Do not introduce helper classes for one-liners. Do not "modernize" working Win32 interop unless asked.

## Key files

| Concern | Files |
|---------|-------|
| Decal injection | `Infrastructure/Native/DecalInjector.cs`, `Services/Launching/GameLauncher.cs` |
| Hard-link instances | `Infrastructure/Native/HardLinkLauncher.cs`, `Infrastructure/Native/InstanceLauncherBase.cs` |
| DAT cache | `Services/Dats/DatSetService.cs`, `Infrastructure/WebServices/DatRegistryDownloader.cs` |
| ACBase copy | `Application/FirstRunService.cs` |
| Server DAT config | `Core/Models/Server.cs` |
| Instance strategy toggle | `Application/ServiceBootstrapper.cs` |
| ShadowFilter | `ShadowFilter/` (Decal plugin), `Services/Launching/GameLauncher.cs`, `Infrastructure/Decal/ShadowFilterDecalRegistration.cs` |
| DAT Manager UI | `Presentation/ViewModels/DatFetchViewModel.cs` |

## Manual verification (no test suite)

1. **Retail server, Decal installed** — inject succeeds, no `Instances\` dir
2. **Custom-DAT server** — cache populated, instance dir created, correct world loads
3. **Multi-box (2+ clients, same server)** — distinct instance dirs, both Decal-loaded, both VTank/ShadowFilter functional
4. **Program Files AC install** — ACBase copy once, hard links succeed after
5. **No Decal** — single client via `Process.Start`, clear log message
6. **ShadowFilter login commands** — fire on auto-login (launch file written pre-process)

Logs: `%LocalAppData%\ShadowLauncher\Logs\`

## Skills

`.cursor/skills/`:

- `shadow-launcher` — general development
- `decal-injection` — Inject.dll, suspended launch, multi-client
- `dat-cache-hardlinks` — DAT cache, registry, hard-link instance prep

Read the relevant skill before touching launch or DAT code.

## Git workflow

- Do not create a new branch unless explicitly asked
- Do not commit code unless explicitly asked
- Do not open or push a PR unless explicitly asked
- Leave changes as uncommitted working-tree edits for the user to review

## Guardrails

- Do not break suspended Decal injection or revert to mutex hacks
- Do not make players manually swap DAT files — that era is over
- Do not hard-link logs/inis into instance dirs
- Do not switch to symlinks without explicit request
- Do not change `PlatformTarget` away from x86
- Do not inject after resume — game init will beat you every time
- Preserve atomic writes for `settings.json` and `DatRegistry.xml`
