#Requires -Version 5.1
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Tag,
    [string]$PreviousTag = "",
    [string]$Repo = $env:GITHUB_REPOSITORY
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not $Repo) { throw "GITHUB_REPOSITORY is not set." }

$baseUrl = "https://github.com/$Repo"
if ($PreviousTag) {
    $changelog = "$baseUrl/compare/$PreviousTag...$Tag"
    $generatedBody = "Full changelog: $changelog"
}
else {
    $generatedBody = "Initial release. Commits: $baseUrl/commits/$Tag"
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
