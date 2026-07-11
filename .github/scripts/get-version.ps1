#Requires -Version 5.1
param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$versionFiles = @(
    "ShadowLauncher/ShadowLauncher.csproj",
    "ShadowLauncher.Installer/ShadowLauncher.Installer.wixproj",
    "ShadowLauncher.Installer.Bundle/ShadowLauncher.Installer.Bundle.wixproj"
)

$versions = @()
foreach ($rel in $versionFiles) {
    $path = Join-Path $ProjectRoot ($rel -replace '/', '\')
    if (-not (Test-Path $path)) { throw "Version file not found: $path" }

    $xml = [xml](Get-Content $path)
    $versionNode = $xml.Project.PropertyGroup | ForEach-Object { $_.SelectSingleNode("Version") } | Where-Object { $_ } | Select-Object -First 1
    if (-not $versionNode) { throw "No <Version> found in $rel" }
    $versions += $versionNode.InnerText
}

$unique = @($versions | Select-Object -Unique)
if ($unique.Count -ne 1) {
    $details = 0..($versionFiles.Count - 1) | ForEach-Object { "  $($versionFiles[$_]): $($versions[$_])" }
    throw "Version mismatch across project files:`n$($details -join "`n")"
}

$version = $unique[0]
Write-Host "Version: $version (tag: v$version)"

if ($env:GITHUB_OUTPUT) {
    "version=$version" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "tag=v$version" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}
