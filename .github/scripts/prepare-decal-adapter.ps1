#Requires -Version 5.1
param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path,
    [string]$Base64 = $env:DECAL_ADAPTER_DLL_BASE64,
    [switch]$SkipInstalledDecal
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$dest = Join-Path $ProjectRoot "externals\Decal\Decal.Adapter.dll"
$destDir = Split-Path $dest -Parent

if (Test-Path $dest) {
    Write-Host "Decal.Adapter.dll already present at $dest"
    exit 0
}

New-Item -ItemType Directory -Force -Path $destDir | Out-Null

if ($Base64) {
    $bytes = [Convert]::FromBase64String($Base64)
    [IO.File]::WriteAllBytes($dest, $bytes)
    Write-Host "Wrote Decal.Adapter.dll from DECAL_ADAPTER_DLL_BASE64 ($($bytes.Length) bytes)"
    exit 0
}

if (-not $SkipInstalledDecal) {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Common Files\Decal\Decal.Adapter.dll",
        "$env:ProgramFiles\Common Files\Decal\Decal.Adapter.dll"
    )

    foreach ($src in $candidates) {
        if (Test-Path $src) {
            Copy-Item $src $dest -Force
            Write-Host "Copied Decal.Adapter.dll from $src"
            exit 0
        }
    }
}

throw @"
Decal.Adapter.dll not found.

CI options (pick one):
  1. Add repo secret DECAL_ADAPTER_DLL_BASE64 (base64 of Decal.Adapter.dll)
  2. Commit externals/Decal/Decal.Adapter.dll (remove it from externals/Decal/.gitignore)
  3. Install Decal on a self-hosted Windows runner

Locally: copy from 'C:\Program Files (x86)\Common Files\Decal\Decal.Adapter.dll'
See externals/Decal/README.md
"@
