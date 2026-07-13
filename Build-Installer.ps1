#Requires -Version 5.1
param([string]$Version = "")

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root            = $PSScriptRoot
$appProject      = "$root\ShadowLauncher\ShadowLauncher.csproj"
$filterProject   = "$root\ShadowFilter\ShadowFilter.csproj"
$caProject       = "$root\ShadowLauncher.Installer.CustomActions\ShadowLauncher.Installer.CustomActions.csproj"
$filterOut       = "$root\ShadowFilter\bin\Release\net472"
$filterDest      = "$root\ShadowLauncher.Installer\ThirdParty\ShadowFilter"
$decalAdapter    = "$root\externals\Decal\Decal.Adapter.dll"
$msiPkg          = "$root\ShadowLauncher.Installer\Package.wxs"
$msiPriv         = "$root\ShadowLauncher.Installer\Privileges.wxs"
$bundleWxs       = "$root\ShadowLauncher.Installer.Bundle\Bundle.wxs"
$publishDir      = "$root\ShadowLauncher\bin\Release\net10.0-windows"
$caDir           = "$root\ShadowLauncher.Installer.CustomActions\bin\Release\net10.0-windows"
$msiOut          = "$root\ShadowLauncher.Installer\bin\ShadowLauncher-Setup.msi"
$bundleBinDir    = "$root\ShadowLauncher.Installer.Bundle\bin"
$bundleOut       = "$bundleBinDir\ShadowLauncher-Setup.exe"
$logoFile        = "$root\ShadowLauncher\SLicon.ico"
$runtimeExe      = "$bundleBinDir\windowsdesktop-runtime-10.0.6-win-x86.exe"
$runtimeUrl      = "https://dotnetcli.azureedge.net/dotnet/WindowsDesktop/10.0.6/windowsdesktop-runtime-10.0.6-win-x86.exe"
$licenseFile     = "$root\ShadowLauncher.Installer.Bundle\license.rtf"
$balDll          = "$env:USERPROFILE\.wix\extensions\WixToolset.Bal.wixext\5.0.2\wixext5\WixToolset.BootstrapperApplications.wixext.dll"

function Step([string]$msg) {
    Write-Host ""
    Write-Host "-----------------------------------------------" -ForegroundColor Cyan
    Write-Host "  $msg" -ForegroundColor Cyan
    Write-Host "-----------------------------------------------" -ForegroundColor Cyan
}

if (-not $Version) {
    $xml = [xml](Get-Content $appProject)
    $Version = ($xml.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
    if (-not $Version) { $Version = "0.1.0" }
}
Write-Host "Building ShadowLauncher v$Version" -ForegroundColor Green

Step "Checking prerequisites"
if (-not (Get-Command "wix" -ErrorAction SilentlyContinue)) { throw "WiX CLI not found. Run: dotnet tool install --global wix --version 5.0.2" }
if (-not (Test-Path $balDll)) { throw "WiX Bal extension not found at: $balDll" }
if (-not (Test-Path $decalAdapter)) {
    throw @"
Decal.Adapter.dll not found at: $decalAdapter
Copy it from your Decal install before building ShadowFilter.
See externals/Decal/README.md
"@
}
Write-Host "  All prerequisites found." -ForegroundColor Green

Step "1/5  Building ShadowFilter (Release net472)"
& dotnet build $filterProject -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "ShadowFilter build failed" }
New-Item -ItemType Directory -Force -Path $filterDest | Out-Null
Copy-Item "$filterOut\ShadowFilter.dll" $filterDest -Force
Copy-Item "$filterOut\Newtonsoft.Json.dll" $filterDest -Force
Write-Host "  ShadowFilter DLLs copied to $filterDest" -ForegroundColor Green

Step "2/5  Building ShadowLauncher (Release x86)"
& dotnet publish $appProject -c Release --output $publishDir --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

Step "3/5  Building Custom Actions DLL"
& dotnet publish $caProject -c Release --output $caDir --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build (custom actions) failed" }

Step "4/5  Building ShadowLauncher-Setup.msi"
New-Item -ItemType Directory -Path (Split-Path $msiOut) -Force | Out-Null
& wix build $msiPkg $msiPriv -d "AppPublishDir=$publishDir\" -d "CustomActionsDir=$caDir\" -d "ThirdPartyDir=$filterDest\..\\" -d "LicenseFile=$licenseFile" -d "Version=$Version" -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext -ext WixToolset.Netfx.wixext -arch x86 -out $msiOut
if ($LASTEXITCODE -ne 0) { throw "wix build (msi) failed" }

Step "5a/5  Caching .NET 10 Desktop Runtime (x86)"
if (-not (Test-Path $runtimeExe)) {
    Write-Host "  Downloading .NET 10 Desktop Runtime (x86)..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $bundleBinDir -Force | Out-Null
    Invoke-WebRequest $runtimeUrl -OutFile $runtimeExe -UseBasicParsing
    Write-Host "  Downloaded." -ForegroundColor Green
} else {
    Write-Host "  .NET runtime already cached." -ForegroundColor DarkGray
}

Step "5b/5  Building ShadowLauncher-Setup.exe (bundle)"
& wix build $bundleWxs -d "MsiPath=$msiOut" -d "LogoFile=$logoFile" -d "LicenseFile=$licenseFile" -d "Version=$Version" -b $bundleBinDir -ext $balDll -ext WixToolset.Netfx.wixext -arch x86 -out $bundleOut
if ($LASTEXITCODE -ne 0) { throw "wix build (bundle) failed" }

$sizeMb = [math]::Round((Get-Item $bundleOut).Length / 1MB, 1)
Write-Host ""
Write-Host "Build complete!" -ForegroundColor Green
Write-Host "  Output : $bundleOut" -ForegroundColor Green
Write-Host "  Size   : $sizeMb MB" -ForegroundColor Green
Write-Host "  Version: $Version" -ForegroundColor Green
