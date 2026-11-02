# Windows-only build orchestrator for AIMP Karaoke Studio v0.3.
# Prerequisites: Visual Studio 2022 with C++ desktop workload, .NET SDK 10, internet to fetch AIMP SDK (optional with -SdkDir).
[CmdletBinding()]
param(
    [ValidateSet('x64','x86','both')][string]$Architecture = 'x64',
    [string]$SdkDir = '',
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$logFolder = Join-Path $root 'build'
New-Item -ItemType Directory -Path $logFolder -Force | Out-Null
$logFile = Join-Path $logFolder ('windows-release-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
Start-Transcript -Path $logFile -Force | Out-Null
try {
    function Require-Command([string]$Name, [string]$Tip) {
        if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) { throw "Missing $Name. $Tip" }
    }
    if ($PSVersionTable.PSEdition -eq 'Core' -and -not $IsWindows) { throw 'This script must run on Windows.' }
    Require-Command 'cmake' 'Install Visual Studio Desktop development with C++ and CMake.'
    Require-Command 'dotnet' 'Install the .NET 10 SDK from Microsoft.'
    $dotnetSdks = (dotnet --list-sdks) -join "`n"
    if ($LASTEXITCODE -ne 0 -or $dotnetSdks -notmatch '(?m)^10\.') { throw '.NET SDK 10.x not found. Install .NET SDK 10.' }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) { throw 'Visual Studio Installer (vswhere) not found. Install Visual Studio 2022 with Desktop development with C++.' }
    $vs = & $vswhere -latest -version '[17.0,18.0)' -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vs) { throw 'Visual Studio 2022 with C++ build tools is required.' }
    Write-Host "VS: $vs"

    # AIMP SDK is downloaded only if the user did not provide an SDK folder.
    if (-not $SdkDir) {
        $sdkRoot = Join-Path $root 'build\aimp-sdk'
        $sdkCpp = Join-Path $sdkRoot 'aimp-sdk-main\Sources\Cpp'
        if (-not (Test-Path (Join-Path $sdkCpp 'apiPlugin.h'))) {
            Write-Host 'Downloading public AIMP SDK headers...' -ForegroundColor Cyan
            New-Item -ItemType Directory -Path $sdkRoot -Force | Out-Null
            $sdkZip = Join-Path $sdkRoot 'sdk.zip'
            Invoke-WebRequest -Uri 'https://github.com/Exle/aimp-sdk/archive/refs/heads/main.zip' -OutFile $sdkZip -UseBasicParsing
            Expand-Archive -Path $sdkZip -DestinationPath $sdkRoot -Force
        }
        $SdkDir = $sdkCpp
    }
    $SdkDir = (Resolve-Path $SdkDir).Path
    if (-not (Test-Path (Join-Path $SdkDir 'apiPlugin.h'))) { throw "Invalid AIMP SDK C++ headers directory: $SdkDir" }
    Write-Host "SDK: $SdkDir"

    if (-not $SkipTests) {
        Write-Host 'Building native LRC parser tests...'
        $testDir = Join-Path $root 'build\parser-test'
        & cmake -S (Join-Path $root 'tests') -B $testDir -G 'Visual Studio 17 2022' -A x64
        if ($LASTEXITCODE -ne 0) { throw 'CMake configuration of native tests failed.' }
        & cmake --build $testDir --config Release
        if ($LASTEXITCODE -ne 0) { throw 'Native test build failed.' }
        & ctest --test-dir $testDir -C Release --output-on-failure
        if ($LASTEXITCODE -ne 0) { throw 'Native tests FAILED.' }
        Write-Host 'Running managed parser tests...'
        & dotnet run --project (Join-Path $root 'tests\LrcReaderTests\LrcReaderTests.csproj') -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Managed LRC tests FAILED.' }
    }

    $architectures = if ($Architecture -eq 'both') { @('x86','x64') } else { @($Architecture) }
    foreach ($arch in $architectures) {
        Write-Host "Building and packaging $arch ..." -ForegroundColor Cyan
        & (Join-Path $root 'build.ps1') -SdkDir $SdkDir -Architecture $arch
        if ($LASTEXITCODE -ne 0) { throw "Build failed for $arch" }
        $package = Join-Path $root "dist\AimpKaraokeStudio-$arch.aimppack"
        if (-not (Test-Path $package)) { throw "Expected .aimppack not created: $package" }
        Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
        $zip = [System.IO.Compression.ZipFile]::OpenRead($package)
        try {
            $names = @($zip.Entries | ForEach-Object FullName)
            $dir = if ($arch -eq 'x64') {'AimpKaraokeStudio/x64/'} else {'AimpKaraokeStudio/'}
            # Compress-Archive uses forward slash separators within ZIP archives.
            $dll = $dir + 'AimpKaraokeStudio.dll'
            $exe = $dir + 'KaraokeStudio.exe'
            if ($names -notcontains $dll -or $names -notcontains $exe) { throw "Package missing $dll or $exe" }
            if ($arch -eq 'x64' -and $names -contains 'AimpKaraokeStudio/AimpKaraokeStudio.dll') { throw 'x64 DLL incorrectly placed in package root' }
        } finally { $zip.Dispose() }
        $sha = Get-FileHash -Path $package -Algorithm SHA256
        Write-Host "Created: $package" -ForegroundColor Green
        Write-Host "SHA256: $($sha.Hash)"
    }
    Write-Host 'COMPLETED. Install .aimppack via AIMP > Preferences > Plugins > Install.' -ForegroundColor Green
    Write-Host 'NOTE: This build has NOT been verified by running AIMP; test it locally.' -ForegroundColor Yellow
} finally {
    Stop-Transcript | Out-Null
    Write-Host "Build log: $logFile"
}
