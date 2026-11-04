# Run in Windows PowerShell or PowerShell 7, from any working directory.
param(
  [Parameter(Mandatory=$false)][string]$SdkDir = $env:AIMP_SDK_DIR,
  [ValidateSet('x64','x86')][string]$Architecture = 'x64',
  [switch]$SkipPackage
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $SdkDir) { throw 'Pass -SdkDir C:\Path\To\AIMP-SDK\Sources\Cpp (or set AIMP_SDK_DIR).' }
$SdkDir = (Resolve-Path $SdkDir).Path
if (-not (Test-Path (Join-Path $SdkDir 'apiPlugin.h'))) { throw 'apiPlugin.h was not found in -SdkDir.' }
$vsArch = if ($Architecture -eq 'x64') { 'x64' } else { 'Win32' }
$rid = if ($Architecture -eq 'x64') { 'win-x64' } else { 'win-x86' }
$cppBuild = Join-Path $root "build/plugin-$Architecture"
$publish = Join-Path $root "build/publish-$Architecture"
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
Write-Host "[1/3] Building AIMP native plugin for $Architecture..."
cmake -S (Join-Path $root 'plugin') -B $cppBuild -G 'Visual Studio 17 2022' -A $vsArch "-DAIMP_SDK_DIR=$SdkDir"
if ($LASTEXITCODE -ne 0) { throw 'CMake configure failed' }
cmake --build $cppBuild --config Release
if ($LASTEXITCODE -ne 0) { throw 'C++ build failed' }
$nativeDll = Join-Path $cppBuild 'Release/AimpKaraokeStudio.dll'
if (-not (Test-Path $nativeDll)) { throw 'Native DLL is missing after compilation' }
Write-Host "[2/4] Testing and compiling Go Bridge (source build)..."
if (-not (Get-Command go -ErrorAction SilentlyContinue)) {
  throw 'Go 1.23+ is required to compile the open-source Bridge server.'
}
Push-Location (Join-Path $root 'bridge-go')
try {
  go test ./... -count=1
  if ($LASTEXITCODE -ne 0) { throw 'Go Bridge unit tests failed' }
  go vet ./...
  if ($LASTEXITCODE -ne 0) { throw 'Go Bridge vet failed' }
  go build -trimpath -ldflags="-s -w" -o (Join-Path $root 'build/KaraokeBridge.exe') ./cmd/aimp-lyrics-bridge
  if ($LASTEXITCODE -ne 0) { throw 'Go Bridge compile failed' }
} finally { Pop-Location }
Write-Host "[2/3] Publishing Windows WPF renderer ($rid)..."
dotnet publish (Join-Path $root 'app/KaraokeStudio.csproj') -c Release -r $rid --self-contained true -o $publish -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw '.NET publish failed' }
$targetFolder = Join-Path $dist 'AimpKaraokeStudio'
$binaryFolder = if ($Architecture -eq 'x64') { Join-Path $targetFolder 'x64' } else { $targetFolder }
if (Test-Path $targetFolder) { Remove-Item -Recurse -Force $targetFolder }
New-Item -ItemType Directory -Force $binaryFolder | Out-Null
Copy-Item $nativeDll $binaryFolder
Copy-Item (Join-Path $root 'build/KaraokeBridge.exe') $binaryFolder
Copy-Item (Join-Path $root 'bridge-go/config.default.json') (Join-Path $binaryFolder 'bridge.config.json')
Copy-Item (Join-Path $publish '*') $binaryFolder -Recurse -Force
Copy-Item (Join-Path $root 'INSTALL.md') $targetFolder
Copy-Item (Join-Path $root 'LICENSE') $targetFolder
# Verify the strict AIMP plugin package layout before packaging.
if (-not (Test-Path (Join-Path $binaryFolder 'AimpKaraokeStudio.dll'))) { throw 'AIMP plugin DLL missing in architecture folder' }
if (-not (Test-Path (Join-Path $binaryFolder 'KaraokeStudio.exe'))) { throw 'WPF renderer missing in architecture folder' }
if ($Architecture -eq 'x64' -and (Test-Path (Join-Path $targetFolder 'AimpKaraokeStudio.dll'))) {
  throw 'x64 plugin incorrectly placed in package root'
}
if (-not $SkipPackage) {
  Write-Host '[3/3] Creating AIMP install package...'
  $zip = Join-Path $dist "AimpKaraokeStudio-$Architecture.zip"
  $pack = Join-Path $dist "AimpKaraokeStudio-$Architecture.aimppack"
  if (Test-Path $zip) { Remove-Item $zip -Force }
  if (Test-Path $pack) { Remove-Item $pack -Force }
  Compress-Archive -Path $targetFolder -DestinationPath $zip -CompressionLevel Optimal
  Move-Item $zip $pack
  Write-Host "Package: $pack"
}
Write-Host "Renderer: $(Join-Path $binaryFolder 'KaraokeStudio.exe')"
Write-Host 'Build succeeded. Install the .aimppack using AIMP > Settings > Plugins > Install.'
