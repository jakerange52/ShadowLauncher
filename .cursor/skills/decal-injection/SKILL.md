---
name: decal-injection
description: Modify Decal injection and multi-client launch in ShadowLauncher. Veteran-level guidance on Inject.dll, DecalStartup, suspended CreateProcess, and the AC single-instance mutex. Use when touching launch or injection code.
---

# Decal Injection

## Background — why we inject externally

Decal was built for a single AC client. `Inject.dll` is a native DLL that loads into acclient and bootstraps the Decal Agent — a .NET Framework host that loads plugin assemblies (VTank/VirindiTank, UtilityBelt, ThwargFilter, Mag-Tools, etc.). The Decal tray app can inject on its own ("alternate injection method" in Decal settings), but multi-box launchers can't rely on that — it's timing-dependent and fights the single-instance mutex.

AC creates a named mutex early in startup. Without Decal in the process, a second `acclient.exe` exits immediately. Decal intercepts this once `DecalStartup` has run. That's why every serious multi-box tool — ThwargLauncher, Mag-Launcher, us — injects **before the main thread executes client init**.

The old hacks don't belong in this codebase:

- Closing/killing the AC mutex from outside the process
- Patching `acclient.exe` on disk
- Copying the client to a new folder and hoping the mutex name differs (it doesn't)
- Injecting after `ResumeThread` (race — game code wins)

## When to use this skill

- Changing `DecalInjector.cs` or `GameLauncher.LaunchWithDecal()`
- Decal path detection / settings
- Debugging injection failures or multi-box mutex issues
- Anything involving `CREATE_SUSPENDED`, `LoadLibraryW`, or `DecalStartup`

## Decal stack (know your layers)

```
acclient.exe                    ← 32-bit native Win32 (Turbine client)
  Inject.dll                    ← native C++ injector/shim (Decal install dir)
    DecalStartup() export     ← MUST be called after LoadLibrary; init is not automatic
    Decal Agent               ← .NET Framework CLR host (version depends on Decal build)
      Plugin assemblies       ← VTank, ThwargFilter, UtilityBelt, custom plugins
        Decal.Adapter API     ← hooks into game objects, chat, movement, etc.
```

ShadowLauncher is **none of the above** — it's an external .NET 10 process that uses Win32 APIs to inject into acclient. Never confuse the launcher's CLR with Decal's in-process CLR.

## Injection sequence — do not reorder

`Infrastructure/Native/DecalInjector.cs`

```
CreateProcess(CREATE_SUSPENDED)
  → VirtualAllocEx + WriteProcessMemory (Unicode path to Inject.dll)
  → CreateRemoteThread → kernel32!LoadLibraryW
  → WaitForSingleObject (LoadLibrary completes)
  → Resolve DecalStartup export RVA locally (LoadLibraryEx + DONT_RESOLVE_DLL_REFERENCES)
  → Find Inject.dll base in remote process (EnumProcessModulesEx)
  → CreateRemoteThread → DecalStartup
  → WaitForSingleObject
  → ResumeThread(main thread)
  → CloseHandle
```

**Why suspended:** `CreateRemoteThread` works on a suspended process — the remote thread runs independently of the main thread. Once you resume, acclient's init runs with Decal already loaded.

**Why DecalStartup:** Loading Inject.dll via `LoadLibraryW` maps the DLL but does not run Decal's initialization path. The `DecalStartup` export must be explicitly invoked — this is Decal internals, not generic DLL injection. Skipping it gives you a loaded DLL and a non-functional Decal Agent.

**Failure handling:** if injection throws, still `ResumeThread` — a suspended zombie acclient is worse than a client running without Decal.

## Path resolution

```csharp
DecalInjector.ResolveDecalInjectPath(configuredPath)
```

| Priority | Source |
|----------|--------|
| 1 | `AppConfiguration.DecalPath` (Settings override) |
| 2 | `HKLM\SOFTWARE\Decal\Agent` → `AgentPath\Inject.dll` |
| null | No Decal → `Process.Start` fallback (single client) |

The registry key is where a standard Decal install registers itself. Plugin DLLs live elsewhere (`C:\Games\Decal Plugins\` or similar) — we inject **Inject.dll** from the Agent path, not individual plugins. Decal Agent loads plugins after init.

## Working directory

For custom-DAT servers, `workingDir` = `Instances\{guid}\` (the hard-link tree). acclient reads DATs relative to CWD. Decal doesn't care about DATs directly, but if CWD is wrong the client crashes or loads the wrong world before plugins ever get a chance to hook.

For retail servers, CWD = the configured client directory. No instance dir.

## Multi-box interaction

Each client is a separate process with its own Decal injection. Decal handles per-process mutex semantics. Combined with per-instance hard-link directories (for custom DATs), you get N independent clients without folder duplication.

Do not try to share one acclient process across accounts. That's not how Decal plugins work — VTank meta, character lists, and hook state are all per-process.

## ShadowFilter dependency

ShadowFilter is a first-party Decal plugin (`ShadowFilter/`). It reads the launch file written by `GameLauncher.WriteShadowFilterLaunchFile()` and acts on first server connect. Its timer runs **four ticks** (states 0–3) then stops permanently. The launch file must exist before `CreateProcess` — same constraint ThwargLauncher/optimshi enforced.

Without Decal injection, ShadowFilter never loads and login commands are dead code. Setup registers ShadowFilter with Decal automatically; FirstRun retries registration if Decal was installed later.

## Debugging — veteran checklist

1. Log line: `"Decal injection: {Path}"` vs `"Decal not found — single client launch"`
2. `Inject.dll` exists at resolved path? (Not DecalAgent.dll, not a plugin — **Inject.dll**)
3. Registry: `HKLM\SOFTWARE\Decal\Agent\AgentPath`
4. Win32 error on failed `CreateProcess` (logged decimal + hex)
5. Second client exits immediately → injection probably didn't happen on one or both
6. Plugins load but hooks fail → DecalStartup probably skipped
7. Custom-DAT server: verify CWD in instance dir has hard-linked DATs
8. "DirectX error" on multi-box → check for hard-linked `.log`/`.ini` (inode sharing bug, not GPU)

Export a Decal log (Decal tray → Help → export) when plugin issues persist past injection.

## Safe changes

- Better logging around each injection step
- Additional Decal install path detection (before registry fallback)
- Clearer UI error when injection fails (include Win32 code)
- User-facing note when Decal missing (single-client limitation)

## Do not

- Launch without `CREATE_SUSPENDED` then inject
- Skip `DecalStartup`
- Inject after `ResumeThread`
- Kill/rename the AC mutex from outside
- Patch acclient on disk
- Remove the no-Decal fallback
- Target x64 — acclient and Inject.dll are x86

## Key files

| File | Role |
|------|------|
| `Infrastructure/Native/DecalInjector.cs` | P/Invoke, injection |
| `Services/Launching/GameLauncher.cs` | Orchestration, ShadowFilter launch file, fallback |
| `Infrastructure/Decal/ShadowFilterDecalRegistration.cs` | First-run Decal plugin registration |
| `Infrastructure/Configuration/AppConfiguration.cs` | `DecalPath` |
| `Presentation/ViewModels/SettingsViewModel.cs` | Decal path UI |
| `Infrastructure/Native/HardLinkLauncher.cs` | Instance CWD for custom DATs |
| `ShadowLauncher.csproj` | `PlatformTarget=x86` |

## Verification

Windows + Decal installed + AC client:

1. Single launch — log shows inject path, PID > 0, Decal tray shows client
2. Second launch same server — both clients running, both Decal-loaded
3. VTank or ShadowFilter enabled — plugin hooks active in both (if configured)
4. Decal uninstalled — one client via Process.Start, no crash
5. Custom-DAT server — injection with instance dir as CWD
