BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
    $script:GetVersionScript = Join-Path $PSScriptRoot "../get-version.ps1"
}

Describe "get-version.ps1" {
    It "returns version when all three files match" {
        $root = New-TestProjectRoot
        try {
            Write-AllMatchingVersionFiles -ProjectRoot $root -Version "0.3.1"

            $result = Invoke-ScriptWithGitHubOutput {
                & $script:GetVersionScript -ProjectRoot $root
            }

            $result.Outputs.version | Should -Be "0.3.1"
            $result.Outputs.tag | Should -Be "v0.3.1"
        }
        finally {
            Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It "throws when versions mismatch across project files" {
        $root = New-TestProjectRoot
        try {
            Write-TestCsproj -ProjectRoot $root -RelativePath "ShadowLauncher/ShadowLauncher.csproj" -Version "0.3.1"
            Write-TestCsproj -ProjectRoot $root -RelativePath "ShadowLauncher.Installer/ShadowLauncher.Installer.wixproj" -Version "0.3.0"
            Write-TestCsproj -ProjectRoot $root -RelativePath "ShadowLauncher.Installer.Bundle/ShadowLauncher.Installer.Bundle.wixproj" -Version "0.3.1"

            { Invoke-ScriptWithGitHubOutput { & $script:GetVersionScript -ProjectRoot $root } } |
                Should -Throw "*Version mismatch*"
        }
        finally {
            Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It "throws when a version file is missing" {
        $root = New-TestProjectRoot
        try {
            Write-TestCsproj -ProjectRoot $root -RelativePath "ShadowLauncher/ShadowLauncher.csproj" -Version "0.3.1"
            Write-TestCsproj -ProjectRoot $root -RelativePath "ShadowLauncher.Installer/ShadowLauncher.Installer.wixproj" -Version "0.3.1"

            { Invoke-ScriptWithGitHubOutput { & $script:GetVersionScript -ProjectRoot $root } } |
                Should -Throw "*not found*"
        }
        finally {
            Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It "throws when a version file has no Version element" {
        $root = New-TestProjectRoot
        try {
            Write-AllMatchingVersionFiles -ProjectRoot $root -Version "0.3.1"
            Write-TestCsproj -ProjectRoot $root -RelativePath "ShadowLauncher.Installer/ShadowLauncher.Installer.wixproj"

            { Invoke-ScriptWithGitHubOutput { & $script:GetVersionScript -ProjectRoot $root } } |
                Should -Throw "*No <Version> found*"
        }
        finally {
            Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It "completes without error when GITHUB_OUTPUT is not set" {
        $root = New-TestProjectRoot
        try {
            Write-AllMatchingVersionFiles -ProjectRoot $root -Version "0.3.1"
            Remove-Item Env:GITHUB_OUTPUT -ErrorAction SilentlyContinue

            { & $script:GetVersionScript -ProjectRoot $root } | Should -Not -Throw
        }
        finally {
            Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
