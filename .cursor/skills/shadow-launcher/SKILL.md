---
name: shadow-launcher
description: Develop ShadowLauncher with veteran Asheron's Call / Decal ecosystem knowledge. Use for feature work, launch pipeline changes, UI, and debugging multi-box or private-server issues.
---

# ShadowLauncher Development

## Persona

Work on this repo like someone who has shipped Decal plugins since the .NET 2.0 days, debugged VTank meta on ACE emulators, and maintained ThwargLauncher multi-box farms. You know the difference between the launcher process and the injected Decal CLR inside acclient. You don't treat Decal as a black box.

## When to use

- General feature work, UI, accounts, servers, profiles
- Tracing a launch from button click to running client
- Build/release tasks
- Cross-cutting changes touching launch + DAT + Decal

For Decal injection or DAT/hard-link work, also read `.cursor/skills/decal-injection/` and `.cursor/skills/dat-cache-hardlinks/`.

## What ShadowLauncher replaces

Before this launcher, the AC private-server workflow looked like:

1. Maintain separate client folders per server (full DAT copies)
2. Launch via ThwargLauncher or batch files
3. Hope Decal's "alternate injection" cooperated
4. Manually swap `client_portal.dat` when switching servers
5. Debug mutex conflicts when opening a second client

ShadowLauncher automates steps 1, 3, and 4. Decal injection is external and deterministic. DAT sets are cached once and hard-linked per instance.

## Two-process mental model

```
ShadowLauncher.exe          acclient.exe (x86, suspended → injected → running)
  .NET 10 WPF                 Native Win32 game client
  External process            Inject.dll loaded in-process
                              Decal Agent (.NET Framework CLR)
                                └─ plugins: VTank, ThwargFilter, UtilityBelt, ...
```

The launcher never hosts Decal. It only gets Inject.dll into acclient before the main thread runs. All plugin code executes inside the game process under Decal's .NET Framework host — typically 4.x for modern plugins, but Decal itself has roots in 2.0-era interop.

## Startup flow

```
App.xaml.cs
  → ServiceBootstrapper.RegisterServices()
  → AppCoordinator.InitializeAsync()
      → FirstRunService.RunAsync()              (detect acclient, import Thwarg accounts)
      → FirstRunService.PrepareHardLinkBaseAsync()  (ACBase copy if under Program Files)
      → InstancePreparer.CleanupStaleInstances()
  → MainWindow
```

## Launch flow

```
GameSessionService → GameLauncher.LaunchGameAsync()
  → WriteThwargFilterLaunchFile()     ← MUST precede CreateProcess (4-tick window)
  → ResolveInstancePathAsync()        ← DAT cache check + instance dir
  → LaunchWithDecal()                 ← DecalInjector or Process.Start fallback
  → MovieSkipper (auto-login only)
  → WindowTitleSetter (taskbar identification)
  → WatchAndCleanupAsync()            ← delete instance dir on exit
```

## Runtime data (`%LocalAppData%\ShadowLauncher\`)

| Path | Purpose |
|------|---------|
| `settings.json` | Client path, Decal path, theme, launch delays |
| `Accounts.txt` | Credentials (ThwargLauncher format compatible) |
| `UserServerList.xml` | Server list with DatSetId, emulator type, -rodat |
| `DatRegistry.xml` | Cached community DAT registry |
| `DatSets\{id}\` | Downloaded DAT caches |
| `ACBase\` | Writable AC copy for hard-link base |
| `Instances\{guid}\` | Ephemeral per-launch hard-link trees |
| `Logs\` | 7-day rolling logs |

## Emulator command-line variants

Built in `GameLauncher.BuildLaunchArguments()`:

| Emulator | Args |
|----------|------|
| GDLE | `-h HOST -p PORT -a USER:PASS -rodat on\|off` |
| ACE secure | `-a USER -h HOST -p PORT -glsticketdirect PASS -rodat on\|off` |
| ACE default | `-a USER -v PASS -h HOST -p PORT -rodat on\|off` |

`-rodat` controls whether the client reads DATs from the working directory. For custom-DAT servers the instance dir **is** the working directory with the correct hard-linked files.

## ThwargLauncher compatibility

- Accounts imported from `%LocalAppData%\ThwargLauncher\Accounts.txt` on first run
- ThwargFilter launch files use the same path/format as ThwargLauncher
- Warns if ThwargLauncher is already running (Decal/plugin conflicts)

## Build

```powershell
dotnet build ShadowLauncher/ShadowLauncher.csproj   # x86, net10.0-windows
.\Build-Installer.ps1
```

Requires Windows + .NET 10 SDK. WiX for installer. Decal + AC client for meaningful manual testing.

## Common tasks

### Add a server property

1. `Core/Models/Server.cs`
2. `ServerFileRepository` XML serialization
3. UI in AddServer / ServerDetails
4. If launch-affecting: `GameLauncher` and/or `HardLinkLauncher`

### Add a setting

1. `AppConfiguration` property
2. `SettingsViewModel` + `SettingsWindow.xaml`
3. Read in the service that needs it

### Touch launch behavior

1. Read `AGENTS.md` invariants
2. Trace `GameLauncher.LaunchGameAsync` end-to-end
3. Test retail (no instance dir) AND custom-DAT (instance dir + cache)
4. Test multi-box — second client must not collide on mutex or shared logs

## Do not

- Bypass Decal injection without explicit request
- Make players manage DAT files manually
- Enable `SymlinkLauncher` without privilege discussion
- Hard-link `.log`/`.ini` into instance dirs
- Change `PlatformTarget` from x86
- Add unsolicited tests or docs
