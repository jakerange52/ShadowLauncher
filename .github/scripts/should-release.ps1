#Requires -Version 5.1
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Set-ShouldRelease([bool]$Value) {
    Write-Host "should_release=$Value"
    if ($env:GITHUB_OUTPUT) {
        "should_release=$Value" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    }
}

$tag = "v$Version"
Push-Location $ProjectRoot
try {
    git fetch --tags --quiet 2>$null

    $existingTag = git tag -l $tag 2>$null
    if ($existingTag) {
        Write-Host "Tag $tag already exists — skipping release."
        Set-ShouldRelease $false
        exit 0
    }

    $latestTag = git tag -l 'v*' --sort=-v:refname 2>$null | Select-Object -First 1

    if (-not $latestTag) {
        Write-Host "No previous tags found — releasing $tag."
        Set-ShouldRelease $true
        exit 0
    }

    $latestVersionStr = $latestTag.TrimStart('v')
    $current = [Version]$Version
    $latest = [Version]$latestVersionStr

    if ($current -gt $latest) {
        Write-Host "Version $Version is newer than latest tag $latestTag — releasing."
        Set-ShouldRelease $true
    }
    else {
        Write-Host "Version $Version is not newer than latest tag $latestTag — skipping release."
        Set-ShouldRelease $false
    }
}
finally {
    Pop-Location
}
