# ShadowLauncher

A multi-boxing launcher for Asheron's Call private servers. Launch and manage multiple game clients simultaneously, with per-account credential management, server browsing, DAT file management, and Decal plugin support.

---

## Features

- **Multi-boxing** — launch multiple AC clients at once via symlink junctions, each with its own working directory
- **Account manager** — store accounts per server, auto-fill credentials on login
- **Server browser** — fetch and browse community server listings, add servers in one click
- **DAT manager** — automatically download and cache server-specific DAT files
- **Decal support** — optional Decal injection for plugin support on servers that require it
- **Auto-update** — checks GitHub Releases and installs updates in-app

---

## Requirements

- Windows 10 or 11 (x86 or x64)
- [.NET 10 Desktop Runtime (x86)](https://dotnet.microsoft.com/download/dotnet/10.0) — installed automatically by the setup wizard
- An Asheron's Call client (`acclient.exe`) and associated DAT files

---

## Installation

Download `ShadowLauncher-Setup.exe` from the [latest release](../../releases/latest) and run it. The setup wizard will install the .NET 10 runtime if needed and grant the symlink permission required for multi-boxing — no manual steps required.

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

1. Update `<Version>` in `ShadowLauncher\ShadowLauncher.csproj`
2. Run `.\Build-Installer.ps1 -Version x.y.z`
3. Create a GitHub Release tagged `vx.y.z`
4. Upload `ShadowLauncher-Setup.exe` as the release asset

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
ShadowLauncher.Installer.CustomActions/ Managed MSI custom actions (symlink privilege)
Build-Installer.ps1                     One-command release build script
```
