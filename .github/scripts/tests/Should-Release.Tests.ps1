BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
    $script:ShouldReleaseScript = Join-Path $PSScriptRoot "../should-release.ps1"
}

Describe "should-release.ps1" {
    It "releases when no previous tags exist" {
        $root = Initialize-TestGitRepo
        try {
            $result = Invoke-ScriptWithGitHubOutput {
                & $script:ShouldReleaseScript -Version "0.3.0" -ProjectRoot $root
            }

            $result.Outputs.should_release | Should -Be "True"
            $result.Outputs.ContainsKey("previous_tag") | Should -BeFalse
        }
        finally {
            Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It "releases when version is newer than the latest v-prefixed tag" {
        $root = Initialize-TestGitRepo -Tags @("v0.3.0")
        try {
            $result = Invoke-ScriptWithGitHubOutput {
                & $script:ShouldReleaseScript -Version "0.3.1" -ProjectRoot $root
            }

            $result.Outputs.should_release | Should -Be "True"
            $result.Outputs.previous_tag | Should -Be "v0.3.0"
        }
        finally {
            Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It "releases when version is newer than an unprefixed historical tag" {
        $root = Initialize-TestGitRepo -Tags @("0.3.2")
        try {
            $result = Invoke-ScriptWithGitHubOutput {
                & $script:ShouldReleaseScript -Version "0.3.4" -ProjectRoot $root
            }

            $result.Outputs.should_release | Should -Be "True"
            $result.Outputs.previous_tag | Should -Be "0.3.2"
        }
        finally {
            Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It "skips when the v-prefixed tag for the current version already exists" {
        $root = Initialize-TestGitRepo -Tags @("v0.3.0")
        try {
            $result = Invoke-ScriptWithGitHubOutput {
                & $script:ShouldReleaseScript -Version "0.3.0" -ProjectRoot $root
            }

            $result.Outputs.should_release | Should -Be "False"
        }
        finally {
            Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It "skips when the unprefixed tag for the current version already exists" {
        $root = Initialize-TestGitRepo -Tags @("0.3.2")
        try {
            $result = Invoke-ScriptWithGitHubOutput {
                & $script:ShouldReleaseScript -Version "0.3.2" -ProjectRoot $root
            }

            $result.Outputs.should_release | Should -Be "False"
        }
        finally {
            Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It "skips when version is not newer than the latest tag" {
        $root = Initialize-TestGitRepo -Tags @("v0.3.1")
        try {
            $result = Invoke-ScriptWithGitHubOutput {
                & $script:ShouldReleaseScript -Version "0.3.0" -ProjectRoot $root
            }

            $result.Outputs.should_release | Should -Be "False"
        }
        finally {
            Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It "skips when version equals the latest tag but the exact tag name is absent" {
        $root = Initialize-TestGitRepo -Tags @("v0.3.0")
        try {
            # Version 0.3.0 with only v0.3.0 present hits the existing-tag early exit.
            # Use 0.3.0 against v0.3.1 to exercise the semver comparison branch.
            git -C $root tag -d v0.3.0 | Out-Null
            git -C $root tag v0.3.1

            $result = Invoke-ScriptWithGitHubOutput {
                & $script:ShouldReleaseScript -Version "0.3.0" -ProjectRoot $root
            }

            $result.Outputs.should_release | Should -Be "False"
        }
        finally {
            Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It "prefers the highest semver among mixed prefixed and unprefixed tags" {
        $root = Initialize-TestGitRepo -Tags @("0.3.2", "v0.3.1", "0.2.4")
        try {
            $result = Invoke-ScriptWithGitHubOutput {
                & $script:ShouldReleaseScript -Version "0.3.3" -ProjectRoot $root
            }

            $result.Outputs.should_release | Should -Be "True"
            $result.Outputs.previous_tag | Should -Be "0.3.2"
        }
        finally {
            Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
