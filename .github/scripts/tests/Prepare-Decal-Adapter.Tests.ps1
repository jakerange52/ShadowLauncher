#Requires -Version 5.1

BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
    $script:PrepareScript = Join-Path $PSScriptRoot "../prepare-decal-adapter.ps1"
}

Describe "prepare-decal-adapter.ps1" {
    It "succeeds when Decal.Adapter.dll is already present" {
        $root = New-TestProjectRoot
        try {
            $destDir = Join-Path $root "externals\Decal"
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
            [IO.File]::WriteAllBytes((Join-Path $destDir "Decal.Adapter.dll"), [byte[]](1, 2, 3))

            & $script:PrepareScript -ProjectRoot $root -SkipInstalledDecal
            $LASTEXITCODE | Should -Be 0
        }
        finally {
            Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It "writes the DLL from base64 input" {
        $root = New-TestProjectRoot
        try {
            $payload = [Text.Encoding]::UTF8.GetBytes("decal-ref")
            $b64 = [Convert]::ToBase64String($payload)

            & $script:PrepareScript -ProjectRoot $root -Base64 $b64 -SkipInstalledDecal
            $LASTEXITCODE | Should -Be 0

            $dest = Join-Path $root "externals\Decal\Decal.Adapter.dll"
            Test-Path $dest | Should -BeTrue
            [IO.File]::ReadAllBytes($dest) | Should -Be $payload
        }
        finally {
            Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It "throws when no source is available" {
        $root = New-TestProjectRoot
        try {
            { & $script:PrepareScript -ProjectRoot $root -Base64 "" -SkipInstalledDecal } |
                Should -Throw "*Decal.Adapter.dll not found*"
        }
        finally {
            Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
