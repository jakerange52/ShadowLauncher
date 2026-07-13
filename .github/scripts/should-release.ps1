#Requires -Version 5.1
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Temp test repos have no remote; don't treat native non-zero exits as terminating (PS 7.3+).
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
    $PSNativeCommandUseErrorActionPreference = $false
}

function Set-ShouldRelease {
    param(
        [Parameter(Mandatory)][bool]$Value,
        [string]$PreviousTag = ""
    )

    Write-Host "should_release=$Value"
    if ($PreviousTag) {
        Write-Host "previous_tag=$PreviousTag"
    }
    if ($env:GITHUB_OUTPUT) {
        "should_release=$Value" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        if ($PreviousTag) {
            "previous_tag=$PreviousTag" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        }
    }
}

$tag = "v$Version"
$legacyTag = $Version

Push-Location $ProjectRoot
try {
    git fetch --tags --quiet 2>$null

    $raw = git tag -l 2>$null
    if ($null -eq $raw) {
        $allTags = @()
    }
    elseif ($raw -is [System.Array]) {
        $allTags = @($raw)
    }
    else {
        $allTags = @("$raw")
    }

    $hasExact = $false
    foreach ($t in $allTags) {
        if ($t -eq $tag -or $t -eq $legacyTag) {
            $hasExact = $true
            break
        }
    }

    if ($hasExact) {
        Write-Host "Tag for $Version already exists - skipping release."
        Set-ShouldRelease -Value $false
        exit 0
    }

    $bestTag = $null
    $bestVersion = $null
    foreach ($t in $allTags) {
        # Accept historical unprefixed tags (0.3.2) and new v-prefixed tags (v0.3.4).
        if ($t -match '^v?(\d+(\.\d+){1,3})$') {
            try {
                $parsed = [Version]$Matches[1]
            }
            catch {
                continue
            }

            if ($null -eq $bestVersion -or $parsed -gt $bestVersion) {
                $bestVersion = $parsed
                $bestTag = $t
            }
        }
    }

    if ($null -eq $bestVersion) {
        Write-Host "No previous version tags found - releasing $tag."
        Set-ShouldRelease -Value $true
        exit 0
    }

    $current = [Version]$Version
    if ($current -gt $bestVersion) {
        Write-Host "Version $Version is newer than latest tag $bestTag - releasing."
        Set-ShouldRelease -Value $true -PreviousTag $bestTag
    }
    else {
        Write-Host "Version $Version is not newer than latest tag $bestTag - skipping release."
        Set-ShouldRelease -Value $false -PreviousTag $bestTag
    }
}
finally {
    Pop-Location
}
