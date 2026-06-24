# ShadowLauncher — Agent Instructions

ShadowLauncher is a WPF (.NET 10) multi-boxing launcher for Asheron's Call private servers. Players use it to launch multiple `acclient.exe` instances, manage accounts, browse servers, and run Decal plugins — without manually swapping DAT files.

## Critical invariants

These two behaviors are core product requirements. Do not regress them when making changes.

### 1. Decal injection

Every launch should inject Decal when it is installed. Decal enables multi-client (Decal handles the single-instance mutex) and is required for login automation via ThwargFilter.

- **Entry point:** `ShadowLauncher/Services/Launching/GameLauncher.cs` → `LaunchWithDecal()`
- **Implementation:** `ShadowLauncher/Infrastructure/Native/DecalInjector.cs`
- **Flow:** `CreateProcess` with `CREATE_SUSPENDED` → remote `LoadLibraryW(Inject.dll)` → remote `DecalStartup` → `ResumeThread`
- **Path resolution:** user-configured `DecalPath` in settings → fallback to `HKLM\SOFTWARE\Decal\Agent\AgentPath\Inject.dll`
- **Fallback:** if Decal is not found, launch via plain `Process.Start` (single client only)

When modifying launch code, always preserve suspended-launch + injection for Decal-enabled paths. Do not revert to mutex manipulation or pre-launch patching.

### 2. Transparent DAT management (hard links + cache)

Players must never manually swap DAT files. The launcher downloads and caches server-specific DAT sets, then creates per-instance directories using **hard links** so each client sees the correct files in its working directory.

- **Instance prep:** `ShadowLauncher/Infrastructure/Native/HardLinkLauncher.cs`
- **DAT cache:** `ShadowLauncher/Services/Dats/DatSetService.cs`
- **Registry:** community `DatRegistry.xml` fetched by `DatRegistryDownloader`

**Directory layout:**

```
%LocalAppData%\ShadowLauncher\
  ACBase\                          ← one-time copy if install is under Program Files
  DatSets\{datSetId}\              ← cached DAT sets from registry
  DatSets\{sanitizedServerName}\   ← per-server cache for CustomDatZipUrl
  Instances\{guid}\                ← ephemeral per-launch hard-link tree
    acclient.exe                   → hard link → ACBase or DAT cache
    client_portal.dat              → hard link → DAT cache (or ACBase for retail)
    client_cell_1.dat              → ...
    client_local_English.dat       → ...
    client_highres.dat             → ... (if present)
    *.dll                          → hard links → ACBase (recursive, skips backup/plugins)
```

**DAT source resolution (priority):**

1. `Server.CustomDatRegistryPath` — local folder, no download
2. `Server.CustomDatZipUrl` — download/extract to `%DatSets%/{sanitized name}/`
3. `Server.DatSetId` — registry-backed set in `%DatSets%/{id}/`
4. Retail — no instance dir; launch directly from configured client path

**Hard link constraints:**

- Source and link must be on the **same volume**. `FirstRunService.PrepareHardLinkBaseAsync()` copies protected installs (Program Files) to `%LocalAppData%\ShadowLauncher\ACBase\`.
- Skip `.log`, `.ini`, `.pdb`, `.bin`, `.avi`, `.txt`, `.rtf`, `.msi` when linking runtime files — hard links share inodes; two instances writing the same log causes DirectX errors.
- Partial DAT sets are completed from retail via `CompleteDatCacheFromRetailAsync()`.

**Active vs dormant strategy:** `HardLinkLauncher` is active. `SymlinkLauncher` exists but is commented out in `ServiceBootstrapper.cs` — symlinks require Developer Mode or `SeCreateSymbolicLinkPrivilege`.

## Architecture

```
ShadowLauncher/
  Application/          AppCoordinator, FirstRunService, ServiceBootstrapper (DI)
  Core/                 Models, interfaces, exceptions
  Infrastructure/       Native interop (Decal, hard links), config, persistence, web services
  Presentation/         WPF ViewModels, Views, themes
  Services/             Business logic — launch, monitor, DATs, accounts, servers
```

**Launch pipeline:**

1. `GameLauncher.LaunchGameAsync()` — build args, write ThwargFilter launch file
2. `ResolveInstancePathAsync()` — ensure DAT cache ready, call `IInstancePreparer`
3. `HardLinkLauncher.PrepareInstanceAsync()` — create instance dir with hard links
4. `DecalInjector.LaunchSuspendedAndInject()` — start suspended client with Decal
5. Post-launch — movie skip, window title, instance cleanup watcher

**Dependency injection:** all services registered in `Application/ServiceBootstrapper.cs`.

## Dev environment

- **OS:** Windows 10/11 (x86 or x64). Native interop (`DecalInjector`, `CreateHardLink`) is Windows-only and cannot be exercised on Linux CI.
- **Runtime:** [.NET 10 Desktop Runtime (x86)](https://dotnet.microsoft.com/download/dotnet/10.0)
- **SDK:** .NET 10 SDK for building
- **Installer:** WiX Toolset v5 (see README for extension install)

### Build commands

```powershell
# Build the app
dotnet build ShadowLauncher/ShadowLauncher.csproj

# Build the full installer (requires WiX)
.\Build-Installer.ps1

# Build with explicit version
.\Build-Installer.ps1 -Version 0.2.0
```

Output installer: `ShadowLauncher.Installer.Bundle\bin\ShadowLauncher-Setup.exe`

### Release checklist

1. Bump `<Version>` in `ShadowLauncher.csproj`, `ShadowLauncher.Installer.wixproj`, and `ShadowLauncher.Installer.Bundle.wixproj`
2. Run `.\Build-Installer.ps1 -Version x.y.z`
3. Create GitHub Release tagged `vx.y.z` with `ShadowLauncher-Setup.exe`

## Code conventions

- **Language:** C# / WPF, nullable reference types enabled
- **DI:** Microsoft.Extensions.DependencyInjection; register in `ServiceBootstrapper`
- **Logging:** `ILogger<T>` via Microsoft.Extensions.Logging; logs at `%LocalAppData%\ShadowLauncher\Logs\`
- **Config:** `AppConfiguration` / `IConfigurationProvider`; persisted to `settings.json`
- **Async:** prefer `async`/`await`; instance cleanup uses `WaitForExitAsync`
- **Native code:** P/Invoke in `Infrastructure/Native/`; keep Win32 error handling explicit
- **Comments:** document non-obvious invariants (hard link inode sharing, Decal suspended launch, ThwargFilter timing)

Match existing patterns. Do not introduce new abstractions for one-off logic.

## Key files by concern

| Concern | Files |
|---------|-------|
| Decal injection | `Infrastructure/Native/DecalInjector.cs`, `Services/Launching/GameLauncher.cs` |
| Hard-link instances | `Infrastructure/Native/HardLinkLauncher.cs`, `Infrastructure/Native/InstanceLauncherBase.cs` |
| DAT cache/download | `Services/Dats/DatSetService.cs`, `Infrastructure/WebServices/DatRegistryDownloader.cs` |
| First-run / ACBase | `Application/FirstRunService.cs`, `Presentation/Views/AcBaseCopyWindow.xaml.cs` |
| Server DAT config | `Core/Models/Server.cs` (`DatSetId`, `CustomDatRegistryPath`, `CustomDatZipUrl`) |
| Launch strategy toggle | `Application/ServiceBootstrapper.cs` (`IInstancePreparer` binding) |
| ThwargFilter integration | `Services/Launching/GameLauncher.cs` (launch file read/write) |
| UI — DAT manager | `Presentation/ViewModels/DatFetchViewModel.cs`, `Presentation/Views/DatFetchWindow.xaml` |

## Testing guidance

There is no automated test suite. Manual verification on Windows:

1. **Retail server** — single launch, Decal injects, no instance dir created
2. **Custom-DAT server** — DAT cache populated, instance dir created under `Instances\`, correct DATs linked
3. **Multi-box** — two clients same server launch simultaneously; both get unique instance dirs; Decal loads in both
4. **Program Files install** — ACBase copy runs once; hard links succeed afterward
5. **Decal missing** — falls back to single-client plain launch with clear log message

Check `%LocalAppData%\ShadowLauncher\Logs\` for launch diagnostics.

## Skills

Repo-specific Cursor skills live in `.cursor/skills/`:

- `shadow-launcher` — general development workflow
- `decal-injection` — modifying Decal launch/injection code
- `dat-cache-hardlinks` — modifying DAT download, cache, or hard-link instance prep

Read the relevant skill before touching those subsystems.

## Guardrails

- Do not break Decal suspended-launch injection or revert to mutex hacks
- Do not require players to manually copy/swap DAT files
- Do not switch to symlinks without explicit user request (privilege requirements)
- Do not hard-link files that acclient opens for exclusive write (logs, inis)
- Preserve atomic writes for `settings.json` and `DatRegistry.xml` cache
- Windows-only native code — guard or document when adding platform-specific behavior
