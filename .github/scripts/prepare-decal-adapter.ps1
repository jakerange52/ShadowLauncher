#Requires -Version 5.1
param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$source = "${env:ProgramFiles(x86)}\Decal 3.0\Decal.Adapter.dll"
$dest = Join-Path $ProjectRoot "externals\Decal\Decal.Adapter.dll"

if (Test-Path $dest) {
    Write-Host "Decal.Adapter.dll already present at $dest"
    exit 0
}

if (-not (Test-Path $source)) {
    throw "Decal.Adapter.dll not found at $dest or $source"
}

New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
Copy-Item $source $dest -Force
Write-Host "Copied Decal.Adapter.dll from $source"
