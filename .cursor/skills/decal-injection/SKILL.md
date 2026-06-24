---
name: decal-injection
description: Modify Decal injection and multi-client launch behavior in ShadowLauncher. Use when changing how acclient.exe is started, how Inject.dll is loaded, or Decal path detection.
---

# Decal Injection

## Why this matters

Decal injection is a **core invariant**. It enables:

- **Multi-boxing** — Decal handles the single-instance mutex internally; without it, only one client can run
- **Plugin support** — players rely on Decal plugins (ThwargFilter, etc.)
- **Login automation** — ThwargFilter login commands depend on Decal being loaded before game code runs

## When to use this skill

- Changing how `acclient.exe` is started
- Modifying `DecalInjector.cs` P/Invoke or injection sequence
- Changing Decal path detection or settings
- Debugging "Decal not found" or injection failures
- Any change to `GameLauncher.LaunchWithDecal()`

## Architecture

```
GameLauncher.LaunchGameAsync()
  └─ DecalInjector.ResolveDecalInjectPath(config.DecalPath)
       1. User-configured path (Settings → DecalPath) if file exists
       2. Registry: HKLM\SOFTWARE\Decal\Agent → AgentPath\Inject.dll
  └─ LaunchWithDecal(exePath, args, workingDir, decalInjectPath)
       if decalInjectPath != null:
         DecalInjector.LaunchSuspendedAndInject(...)
       else:
         Process.Start(...)  // single client fallback
```

## Injection sequence (do not reorder)

File: `Infrastructure/Native/DecalInjector.cs`

1. **`CreateProcess`** with `CREATE_SUSPENDED` (0x00000004)
   - Working directory = instance dir (custom DAT) or client dir (retail)
   - Command line = `"acclient.exe" {args}`

2. **`LoadLibraryW`** via `CreateRemoteThread`
   - Allocate remote memory for DLL path (Unicode)
   - Write path, create remote thread pointing at `kernel32!LoadLibraryW`
   - Wait up to 10 seconds

3. **`DecalStartup`** via `CreateRemoteThread`
   - Resolve export RVA locally (`LoadLibraryEx` with `DONT_RESOLVE_DLL_REFERENCES`)
   - Find remote module base via `EnumProcessModulesEx`
   - Call `DecalStartup` remotely — required for Decal initialization

4. **`ResumeThread`** on the main thread

5. Close process/thread handles

If injection throws, the process is still resumed (avoid zombie suspended processes).

## Path resolution

```csharp
DecalInjector.ResolveDecalInjectPath(string? configuredPath)
```

| Priority | Source | Notes |
|----------|--------|-------|
| 1 | `AppConfiguration.DecalPath` | User override in Settings |
| 2 | Registry `HKLM\SOFTWARE\Decal\Agent\AgentPath` | Standard Decal install location |
| null | — | Falls back to plain `Process.Start` |

Settings key: `DecalPath` in `%LocalAppData%\ShadowLauncher\settings.json`

## Multi-client interaction

- **Do not** manipulate the AC single-instance mutex directly
- **Do not** patch `acclient.exe` on disk
- Each multi-box client gets its own instance directory (hard links) when using custom DATs
- Decal injection happens per-process at launch time

## Working directory matters

When launching with custom DATs, `workingDir` is the instance directory (`Instances\{guid}\`), not the original AC install. Decal and acclient must both see the hard-linked DAT files in that directory.

## Debugging checklist

1. Check log: `"Decal injection: {Path}"` vs `"Decal not found — single client launch"`
2. Verify `Inject.dll` exists at resolved path
3. Verify registry key if using auto-detect
4. Check Win32 error on `CreateProcess` failure (logged with hex code)
5. Confirm working directory contains expected hard-linked files (custom DAT servers)
6. Test with Decal installed vs uninstalled to verify fallback path

## Safe change patterns

- **Add logging** — always safe around injection steps
- **Extend path resolution** — add new detection sources before registry fallback
- **Improve error messages** — surface Win32 errors to UI via `LaunchResult.ErrorMessage`

## Unsafe change patterns (avoid)

- Launching without `CREATE_SUSPENDED` then injecting (race with game init)
- Skipping `DecalStartup` call (Decal won't initialize)
- Injecting after `ResumeThread` (too late — game code may have run)
- Replacing injection with DLL hijacking / environment variable tricks
- Removing the no-Decal fallback (breaks users without Decal)

## Related files

| File | Role |
|------|------|
| `Infrastructure/Native/DecalInjector.cs` | P/Invoke, injection logic |
| `Services/Launching/GameLauncher.cs` | Orchestrates launch, calls injector |
| `Infrastructure/Configuration/AppConfiguration.cs` | `DecalPath` setting |
| `Presentation/ViewModels/SettingsViewModel.cs` | Decal path UI |
| `Infrastructure/Native/HardLinkLauncher.cs` | Creates instance dir used as working directory |

## Verification

On Windows with Decal installed:

1. Launch one client — log shows Decal injection path, PID returned
2. Launch second client same server — both run, both have Decal
3. Remove/rename Decal — single client launches without crash
4. Custom-DAT server — injection uses instance dir as working directory
