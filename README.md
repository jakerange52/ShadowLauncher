# ShadowLauncher

A multi-boxing launcher for Asheron's Call private servers. Launch and manage multiple game clients simultaneously, with per-account credential management, server browsing, DAT file management, and Decal plugin support.

---

## Features

- **Multi-boxing** — launch multiple AC clients at once using hard links, each with its own isolated working directory
- **Account manager** — store accounts per server, auto-fill credentials on login
- **Server browser** — fetch and browse community server listings, add servers in one click
- **DAT management** — servers can require alternate DAT files (e.g. expansion content, custom worlds); ShadowLauncher fetches and caches them automatically from a community DAT registry, then uses hard links to point each client at the correct set — no manual file swapping required
- **Decal support** — inject [Decal](http://decaldev.com) into each client for plugin support
- **Login commands** — send ShadowFilter login commands globally or per-character after login
- **Auto-update** — checks GitHub Releases and installs updates in-app

---

## Requirements

- Windows 10 or 11 (x86 or x64)
- [.NET 10 Desktop Runtime (x86)](https://dotnet.microsoft.com/download/dotnet/10.0) — installed automatically by the setup wizard
- An Asheron's Call client (`acclient.exe`) and associated DAT files
- **[Decal](http://decaldev.com)** — required for login automation and plugin support
- **Decal + ShadowFilter** — required for login commands, heartbeat monitoring, and per-character automation. Setup installs `ShadowFilter.dll` under `ShadowLauncher\ShadowFilter\` and registers it with Decal automatically (or add it manually in Decal Agent if Decal was installed later)

> **Note:** Basic launching and server browsing work without Decal/ShadowFilter. Account auto-login, login commands, and per-character scripting require Decal with ShadowFilter enabled.

---

## Installation

Download `ShadowLauncher-Setup.exe` from the [latest release](../../releases/latest) and run it. The setup wizard will install the .NET 10 runtime if needed — no manual steps or special permissions required.

> **Note:** If your AC client is installed under `Program Files`, ShadowLauncher will automatically copy it to your local app data folder the first time it runs. This one-time copy (a few hundred MB) is required so hard links can be created without elevation.

---

## Building from source

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [WiX Toolset v5](https://wixtoolset.org): `dotnet tool install --global wix --version 5.0.2`
- WiX extensions: `wix extension add WixToolset.UI.wixext/5.0.2 WixToolset.Util.wixext/5.0.2 WixToolset.Netfx.wixext/5.0.2 WixToolset.Bal.wixext/5.0.2 --global`

### Build the installer

```powershell
.\Build-Installer.ps1
```

Output: `ShadowLauncher.Installer.Bundle\bin\ShadowLauncher-Setup.exe`

To override the version:

```powershell
.\Build-Installer.ps1 -Version 0.2.0
```

---

## Releasing a new version

1. Ensure the Actions secret `DECAL_ADAPTER_DLL_BASE64` is set (base64 of `Decal.Adapter.dll` — see `externals/Decal/README.md`). Hosted runners do not have Decal installed.
2. Update `<Version>` in `ShadowLauncher\ShadowLauncher.csproj`, `ShadowLauncher.Installer\ShadowLauncher.Installer.wixproj`, and `ShadowLauncher.Installer.Bundle\ShadowLauncher.Installer.Bundle.wixproj` (all three must match)
3. Open a PR and merge to `master`
4. GitHub Actions builds the installer and publishes a GitHub Release tagged `vx.y.z` with a summary of merged PRs
5. Verify the release asset (`ShadowLauncher-Setup.exe`) and notes on the [Releases](../../releases) page

Merges that do not bump the version (or that target a version ≤ the latest existing release tag) are skipped automatically. To release locally instead, run `.\Build-Installer.ps1 -Version x.y.z` and upload the output manually.

Users with the app installed will be notified via **Settings → Check for Updates**.

---

## Project structure

```
ShadowLauncher/                         Main WPF application
  Core/                                 Domain models and interfaces
  Infrastructure/                       Native interop, config, networking
  Presentation/                         ViewModels, Views, converters
  Services/                             Business logic (launch, monitor, DATs)

ShadowLauncher.Installer/               WiX MSI package
ShadowLauncher.Installer.Bundle/        WiX bootstrapper bundle (.exe)
ShadowLauncher.Installer.CustomActions/ Managed MSI custom actions (app data cleanup)
Build-Installer.ps1                     One-command release build script
```

---

## License

MIT — see [LICENSE](LICENSE).
