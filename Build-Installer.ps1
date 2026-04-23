#Requires -Version 5.1
param([string]$Version = "")

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root         = $PSScriptRoot
$appProject   = "$root\ShadowLauncher\ShadowLauncher.csproj"
$caProject    = "$root\ShadowLauncher.Installer.CustomActions\ShadowLauncher.Installer.CustomActions.csproj"
$thwargRepo   = "$root\..\ThwargLauncher\ThwargLauncher\ThwargLauncher\ThwargFilter\ThwargFilter.csproj"
$thwargOut    = "$root\..\ThwargLauncher\ThwargLauncher\ThwargLauncher\ThwargFilter\bin\Debug"
$thwargDest   = "$root\ShadowLauncher.Installer\ThirdParty\ThwargFilter"
$msbuild      = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
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
$licenseFile  = "$root\ShadowLauncher.Installer.Bundle\license.rtf"
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

# Step 1: Build ThwargFilter
Step "1/5  Building ThwargFilter (Debug x86)"
if (-not (Test-Path $thwargRepo)) { throw "ThwargLauncher repo not found at: $thwargRepo`nClone it alongside ShadowLauncher: git clone https://github.com/Thwargle/ThwargLauncher" }
if (-not (Test-Path $msbuild)) { throw "MSBuild not found at: $msbuild" }
& $msbuild $thwargRepo /p:Configuration=Debug /p:Platform=AnyCPU /p:PostBuildEvent="" /nologo /verbosity:minimal
if ($LASTEXITCODE -ne 0) { throw "ThwargFilter build failed" }
New-Item -ItemType Directory -Force -Path $thwargDest | Out-Null
Copy-Item "$thwargOut\ThwargFilter.dll"      $thwargDest -Force
Copy-Item "$thwargOut\Newtonsoft.Json.dll"   $thwargDest -Force
Copy-Item "$thwargOut\VCS5.dll"              $thwargDest -Force
Copy-Item "$thwargOut\VirindiViewService.dll" $thwargDest -Force
Write-Host "  ThwargFilter DLLs copied to $thwargDest" -ForegroundColor Green

# Step 2: Build main app
Step "2/5  Building ShadowLauncher (Release x86)"
& dotnet build $appProject -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

# Step 3: Build custom actions
Step "3/5  Building Custom Actions DLL"
& dotnet build $caProject -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build (custom actions) failed" }

# Step 4: Build MSI
Step "4/5  Building ShadowLauncher-Setup.msi"
New-Item -ItemType Directory -Path (Split-Path $msiOut) -Force | Out-Null
& wix build $msiPkg $msiPriv -d "AppPublishDir=$publishDir\" -d "CustomActionsDir=$caDir\" -d "ThirdPartyDir=$thwargDest\..\\" -d "LicenseFile=$licenseFile" -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext -ext WixToolset.Netfx.wixext -arch x86 -out $msiOut
if ($LASTEXITCODE -ne 0) { throw "wix build (msi) failed" }

# Step 5: Ensure .NET runtime is cached
New-Item -ItemType Directory -Path $bundleBinDir -Force | Out-Null
if (-not (Test-Path $runtimeExe)) {
    Write-Host "  Downloading .NET 10 Desktop Runtime (x86)..." -ForegroundColor Yellow
    Invoke-WebRequest $runtimeUrl -OutFile $runtimeExe -UseBasicParsing
    Write-Host "  Downloaded." -ForegroundColor Green
} else {
    Write-Host "  .NET runtime already cached." -ForegroundColor DarkGray
}

# Step 5: Build bundle
Step "5/5  Building ShadowLauncher-Setup.exe (bundle)"
& wix build $bundleWxs -d "MsiPath=$msiOut" -d "LogoFile=$logoFile" -d "LicenseFile=$licenseFile" -d "Version=$Version" -b $bundleBinDir -ext $balDll -ext WixToolset.Netfx.wixext -arch x86 -out $bundleOut
if ($LASTEXITCODE -ne 0) { throw "wix build (bundle) failed" }

$sizeMb = [math]::Round((Get-Item $bundleOut).Length / 1MB, 1)
Write-Host ""
Write-Host "Build complete!" -ForegroundColor Green
Write-Host "  Output : $bundleOut" -ForegroundColor Green
Write-Host "  Size   : $sizeMb MB" -ForegroundColor Green
Write-Host "  Version: $Version" -ForegroundColor Green