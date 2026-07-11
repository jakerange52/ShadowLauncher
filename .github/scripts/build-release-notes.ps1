#Requires -Version 5.1
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Tag,
    [string]$PreviousTag = "",
    [string]$Repo = $env:GITHUB_REPOSITORY,
    [int]$MaxLines = 25
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not $Repo) { throw "GITHUB_REPOSITORY is not set." }

$apiArgs = @(
    "repos/$Repo/releases/generate-notes",
    "-f", "tag_name=$Tag",
    "-f", "target_commitish=master"
)
if ($PreviousTag) {
    $apiArgs += @("-f", "previous_tag_name=$PreviousTag")
}

$generatedBody = gh api @apiArgs --jq .body
if (-not $generatedBody) {
    $generatedBody = "Release $Tag"
}

$lines = $generatedBody -split "`r?`n"
if ($lines.Count -gt $MaxLines) {
    $generatedBody = (($lines | Select-Object -First $MaxLines) -join "`n") + "`n`n..."
}

$finalBody = "## What's New in v$Version`n`n$generatedBody"

Write-Host "Release notes:`n$finalBody"

if ($env:GITHUB_OUTPUT) {
    $delimiter = "NOTES_EOF_$(Get-Random -Maximum 999999)"
    @"
body<<$delimiter
$finalBody
$delimiter
"@ | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8NoBOM
}
