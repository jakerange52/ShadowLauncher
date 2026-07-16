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

            & $script:PrepareScript -ProjectRoot $root
            $LASTEXITCODE | Should -Be 0
        }
        finally {
            Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It "copies from Decal 3.0 when missing" {
        $root = New-TestProjectRoot
        $source = "${env:ProgramFiles(x86)}\Decal 3.0\Decal.Adapter.dll"
        if (-not (Test-Path $source)) {
            Set-ItResult -Inconclusive "Decal 3.0 not installed at $source"
            return
        }

        try {
            & $script:PrepareScript -ProjectRoot $root
            $LASTEXITCODE | Should -Be 0

            $dest = Join-Path $root "externals\Decal\Decal.Adapter.dll"
            Test-Path $dest | Should -BeTrue
            (Get-Item $dest).Length | Should -Be (Get-Item $source).Length
        }
        finally {
            Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It "throws when no source is available" {
        $root = New-TestProjectRoot
        try {
            { & $script:PrepareScript -ProjectRoot $root } |
                Should -Throw "*Decal.Adapter.dll not found*"
        }
        finally {
            Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
