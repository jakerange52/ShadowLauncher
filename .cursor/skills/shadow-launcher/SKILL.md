---
name: shadow-launcher
description: Develop and modify ShadowLauncher, the Asheron's Call multi-boxing launcher. Use for general feature work, bug fixes, UI changes, and understanding the launch pipeline.
---

# ShadowLauncher Development

## When to use

- Adding or changing launcher features (accounts, servers, profiles, UI)
- Understanding how a launch flows from button click to running `acclient.exe`
- Building, releasing, or debugging the WPF app
- Any work that touches multiple subsystems

For Decal injection or DAT/hard-link changes specifically, also read the dedicated skills in `.cursor/skills/decal-injection/` and `.cursor/skills/dat-cache-hardlinks/`.

## Project overview

ShadowLauncher is a .NET 10 WPF app that:

1. Manages AC accounts and private-server definitions
2. Downloads/caches server-specific DAT files automatically
3. Creates per-client instance directories via hard links
4. Launches `acclient.exe` suspended, injects Decal, resumes
5. Monitors sessions, sends login commands via ThwargFilter

Read `AGENTS.md` at the repo root for architecture and invariants.

## Startup flow

```
App.xaml.cs
  → ServiceBootstrapper.RegisterServices()   (DI wiring)
  → AppCoordinator.InitializeAsync()
      → FirstRunService.RunAsync()           (detect client, import Thwarg accounts)
      → FirstRunService.PrepareHardLinkBaseAsync()  (ACBase copy if needed)
      → InstancePreparer.CleanupStaleInstances()
  → MainWindow shown
```

## Launch flow

```
MainWindowViewModel / GameSessionService
  → GameLauncher.LaunchGameAsync(account, server)
      → WriteThwargFilterLaunchFile()       (must happen BEFORE process start)
      → ResolveInstancePathAsync()          (DAT cache + instance dir)
      → LaunchWithDecal()                   (DecalInjector or Process.Start)
      → MovieSkipper, WindowTitleSetter
      → InstancePreparer.WatchAndCleanupAsync()
```

## Key directories (runtime)

All under `%LocalAppData%\ShadowLauncher\`:

| Path | Purpose |
|------|---------|
| `settings.json` | User config (client path, Decal path, theme, delays) |
| `Accounts.txt` | Stored account credentials |
| `UserServerList.xml` | User's server list |
| `DatRegistry.xml` | Cached community DAT registry |
| `DatSets\{id}\` | Downloaded DAT file caches |
| `ACBase\` | Copy of AC install for hard-link base (protected installs only) |
| `Instances\{guid}\` | Ephemeral per-launch hard-link trees |
| `Logs\` | Rolling file logs (7-day retention) |

## DI registration

All services are wired in `Application/ServiceBootstrapper.cs`. When adding a new service:

1. Define interface in `Core/Interfaces/` or `Services/{Area}/`
2. Implement in `Services/` or `Infrastructure/`
3. Register as singleton (most services) or transient (ViewModels, windows)
4. Inject via constructor

## UI patterns

- **MVVM:** ViewModels in `Presentation/ViewModels/`, Views in `Presentation/Views/`
- **Commands:** `RelayCommand` for button bindings
- **Themes:** `ThemeService` applies saved theme from `Presentation/Themes/`
- **Dialogs:** modal windows opened from ViewModels or MainWindow code-behind

## Build and verify

```powershell
dotnet build ShadowLauncher/ShadowLauncher.csproj
.\Build-Installer.ps1   # full release build (WiX required)
```

Manual testing requires Windows with an AC client and ideally Decal installed. Check logs at `%LocalAppData%\ShadowLauncher\Logs\`.

## Common tasks

### Add a server field

1. Add property to `Core/Models/Server.cs`
2. Update `ServerFileRepository` serialization (XML)
3. Update `AddServerViewModel` / `ServerDetailsWindow` UI
4. If it affects launch, update `GameLauncher` or `HardLinkLauncher`

### Add a settings option

1. Add property to `AppConfiguration` / `IConfigurationProvider`
2. Expose in `SettingsViewModel` and `SettingsWindow.xaml`
3. Read via `_config` in the relevant service

### Change launch behavior

1. Read `AGENTS.md` critical invariants first
2. Trace from `GameLauncher.LaunchGameAsync` through instance prep and Decal injection
3. Test retail server (no instance dir) and custom-DAT server (instance dir + cache)

## Do not

- Remove or bypass Decal injection without explicit request
- Make players manually manage DAT files
- Enable `SymlinkLauncher` without discussing privilege requirements
- Hard-link write-contended files (.log, .ini) into instance dirs
- Add tests or docs files unless requested
