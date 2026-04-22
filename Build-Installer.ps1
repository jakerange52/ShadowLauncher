#Requires -Version 5.1
param([string]$Version = "")

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root         = $PSScriptRoot
$appProject   = "$root\ShadowLauncher\ShadowLauncher.csproj"
$caProject    = "$root\ShadowLauncher.Installer.CustomActions\ShadowLauncher.Installer.CustomActions.csproj"
$msiPkg       = "$root\ShadowLauncher.Installer\Package.wxs"
$msiPriv      = "$root\ShadowLauncher.Installer\Privileges.wxs"
$bundleWxs    = "$root\ShadowLauncher.Installer.Bundle\Bundle.wxs"
$publishDir   = "$root\ShadowLauncher\bin\Release\net10.0-windows"
$caDir        = "$root\ShadowLauncher.Installer.CustomActions\bin\Release\net10.0-windows"
$msiOut       = "$root\ShadowLauncher.Installer\bin\ShadowLauncher-Setup.msi"
$bundleBinDir = "$root\ShadowLauncher.Installer.Bundle\bin"
$bundleOut    = "$bundleBinDir\ShadowLauncher-Setup.exe"
$logoFile     = "$root\ShadowLauncher\SLicon.ico"
$runtimeExe   = "$bundleBinDir\windowsdesktop-runtime-10.0.6-win-x86.exe"
$runtimeUrl   = "https://dotnetcli.azureedge.net/dotnet/WindowsDesktop/10.0.6/windowsdesktop-runtime-10.0.6-win-x86.exe"
$balDll       = "$env:USERPROFILE\.wix\extensions\WixToolset.Bal.wixext\5.0.2\wixext5\WixToolset.BootstrapperApplications.wixext.dll"

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

# Check prerequisites
Step "Checking prerequisites"
if (-not (Get-Command "wix" -ErrorAction SilentlyContinue)) { throw "WiX CLI not found. Run: dotnet tool install --global wix --version 5.0.2" }
if (-not (Test-Path $balDll)) { throw "WiX Bal extension not found at: $balDll" }
Write-Host "  All prerequisites found." -ForegroundColor Green

# Step 1: Build main app
Step "1/4  Building ShadowLauncher (Release x86)"
& dotnet build $appProject -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

# Step 2: Build custom actions
Step "2/4  Building Custom Actions DLL"
& dotnet build $caProject -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build (custom actions) failed" }

# Step 3: Build MSI
Step "3/4  Building ShadowLauncher-Setup.msi"
New-Item -ItemType Directory -Path (Split-Path $msiOut) -Force | Out-Null
& wix build $msiPkg $msiPriv -d "AppPublishDir=$publishDir\" -d "CustomActionsDir=$caDir\" -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext -ext WixToolset.Netfx.wixext -arch x86 -out $msiOut
if ($LASTEXITCODE -ne 0) { throw "wix build (msi) failed" }

# Step 4: Ensure .NET runtime is cached
New-Item -ItemType Directory -Path $bundleBinDir -Force | Out-Null
if (-not (Test-Path $runtimeExe)) {
    Write-Host "  Downloading .NET 10 Desktop Runtime (x86)..." -ForegroundColor Yellow
    Invoke-WebRequest $runtimeUrl -OutFile $runtimeExe -UseBasicParsing
    Write-Host "  Downloaded." -ForegroundColor Green
} else {
    Write-Host "  .NET runtime already cached." -ForegroundColor DarkGray
}

# Step 5: Build bundle
Step "4/4  Building ShadowLauncher-Setup.exe (bundle)"
& wix build $bundleWxs -d "MsiPath=$msiOut" -d "LogoFile=$logoFile" -b $bundleBinDir -ext $balDll -ext WixToolset.Netfx.wixext -arch x86 -out $bundleOut
if ($LASTEXITCODE -ne 0) { throw "wix build (bundle) failed" }

$sizeMb = [math]::Round((Get-Item $bundleOut).Length / 1MB, 1)
Write-Host ""
Write-Host "Build complete!" -ForegroundColor Green
Write-Host "  Output : $bundleOut" -ForegroundColor Green
Write-Host "  Size   : $sizeMb MB" -ForegroundColor Green
Write-Host "  Version: $Version" -ForegroundColor Green