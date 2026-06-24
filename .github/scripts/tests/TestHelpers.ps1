# Shared helpers for release script Pester tests.
# Run locally: Install-Module Pester -Scope CurrentUser; Invoke-Pester .github/scripts/tests -Output Detailed

function New-TestProjectRoot {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("sl-test-" + [guid]::NewGuid().ToString())
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    return $root
}

function Write-TestCsproj {
    param(
        [Parameter(Mandatory)][string]$ProjectRoot,
        [Parameter(Mandatory)][string]$RelativePath,
        [string]$Version
    )

    $fullPath = Join-Path $ProjectRoot ($RelativePath -replace '/', [IO.Path]::DirectorySeparatorChar)
    $dir = Split-Path $fullPath -Parent
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    if ($PSBoundParameters.ContainsKey('Version')) {
        $content = @"
<Project>
  <PropertyGroup>
    <Version>$Version</Version>
  </PropertyGroup>
</Project>
"@
    }
    else {
        $content = @"
<Project>
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
</Project>
"@
    }

    Set-Content -Path $fullPath -Value $content -Encoding utf8
}

function Write-AllMatchingVersionFiles {
    param(
        [Parameter(Mandatory)][string]$ProjectRoot,
        [Parameter(Mandatory)][string]$Version
    )

    Write-TestCsproj -ProjectRoot $ProjectRoot -RelativePath "ShadowLauncher/ShadowLauncher.csproj" -Version $Version
    Write-TestCsproj -ProjectRoot $ProjectRoot -RelativePath "ShadowLauncher.Installer/ShadowLauncher.Installer.wixproj" -Version $Version
    Write-TestCsproj -ProjectRoot $ProjectRoot -RelativePath "ShadowLauncher.Installer.Bundle/ShadowLauncher.Installer.Bundle.wixproj" -Version $Version
}

function Invoke-ScriptWithGitHubOutput {
    param(
        [Parameter(Mandatory)][scriptblock]$ScriptBlock
    )

    $outputFile = [System.IO.Path]::GetTempFileName()
    $previousOutput = $env:GITHUB_OUTPUT
    $env:GITHUB_OUTPUT = $outputFile

    try {
        & $ScriptBlock
        $exitCode = $LASTEXITCODE
    }
    finally {
        if ($null -ne $previousOutput) {
            $env:GITHUB_OUTPUT = $previousOutput
        }
        else {
            Remove-Item Env:GITHUB_OUTPUT -ErrorAction SilentlyContinue
        }
    }

    $parsed = @{}
    if (Test-Path $outputFile) {
        Get-Content $outputFile | ForEach-Object {
            if ($_ -match '^([^=]+)=(.*)$') {
                $parsed[$Matches[1]] = $Matches[2]
            }
        }
        Remove-Item $outputFile -Force
    }

    return [PSCustomObject]@{
        Outputs  = $parsed
        ExitCode = $exitCode
    }
}

function Initialize-TestGitRepo {
    param(
        [string[]]$Tags = @()
    )

    $root = New-TestProjectRoot
    git -C $root init -q
    git -C $root config user.email "ci@test.local"
    git -C $root config user.name "CI"
    git -C $root commit --allow-empty -m "init" -q

    foreach ($tag in $Tags) {
        git -C $root tag $tag
    }

    return $root
}

function Get-ScriptRoot {
    return $PSScriptRoot
}
