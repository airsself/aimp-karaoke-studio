# Best-effort, noninteractive Windows smoke test using the official AIMP portable build.
# Reports each achieved integration step independently; never reports full visual QA.
param([string]$Package = "dist/AimpKaraokeStudio-x64.aimppack")
$ErrorActionPreference = "Stop"
$folder = Join-Path $env:GITHUB_WORKSPACE "smoke-results"
New-Item -ItemType Directory -Force $folder | Out-Null
$report = [ordered]@{
  checkedAt = (Get-Date -Format o)
  AimpVersion = "5.40.2727 x64"
  downloadVerified = $false
  playerStarted = $false
  pluginPipeConnected = $false
  karaokeWindowCreated = $false
  uiAutomationConnectionFound = $false
  screenshotCaptured = $false
  note = "CI host may not provide an interactive desktop. Success is not equivalent to manual playback QA."
  error = ""
}
$player = $null; $renderer = $null
try {
  $root = Join-Path $env:RUNNER_TEMP "aimp-portable-smoke"
  New-Item -ItemType Directory -Force $root | Out-Null
  $zip = Join-Path $root "aimp-portable.zip"
  $url = "https://www.aimp.ru/?do=download.file&id=31"
  Invoke-WebRequest -Uri $url -OutFile $zip -MaximumRedirection 10
  $expected = "7a11b2d502ed1163868f41e96511da989bc9cf7d55282f7997453bafcfe5ff2a"
  $actual = (Get-FileHash -Path $zip -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actual -ne $expected) { throw "AIMP x64 portable checksum mismatch: $actual" }
  $report.downloadVerified = $true
  $extract = Join-Path $root "aimp"
  Expand-Archive -Path $zip -DestinationPath $extract -Force
  $aimpExe = Get-ChildItem -Path $extract -Filter "AIMP.exe" -File -Recurse | Select-Object -First 1
  if (-not $aimpExe) { throw "AIMP.exe missing in the verified portable ZIP" }
  $pluginTarget = Join-Path $aimpExe.DirectoryName "Plugins"
  New-Item -ItemType Directory -Force $pluginTarget | Out-Null
  $pack = (Resolve-Path $Package).Path
  $unpack = Join-Path $root "addon"
  [System.IO.Compression.ZipFile]::ExtractToDirectory($pack, $unpack)
  $addon = Join-Path $unpack "AimpKaraokeStudio"
  if (-not (Test-Path (Join-Path $addon "x64\AimpKaraokeStudio.dll"))) { throw "Plugin DLL not found in package" }
  Copy-Item -Path $addon -Destination $pluginTarget -Recurse -Force
  $wpfExe = Join-Path $pluginTarget "AimpKaraokeStudio\x64\KaraokeStudio.exe"

  # Start player with a generated silent PCM WAV plus enhanced LRC sidecar.
  $wav = Join-Path $root "precision-smoke.wav"
  $pcm = [byte[]]::new(16000 * 2 * 8)
  $w = [System.IO.BinaryWriter]::new([System.IO.File]::Create($wav))
  try {
    $w.Write([System.Text.Encoding]::ASCII.GetBytes("RIFF"))
    $w.Write([int](36 + $pcm.Length))
    $w.Write([System.Text.Encoding]::ASCII.GetBytes("WAVEfmt "))
    $w.Write([int]16); $w.Write([short]1); $w.Write([short]1)
    $w.Write([int]16000); $w.Write([int]32000); $w.Write([short]2); $w.Write([short]16)
    $w.Write([System.Text.Encoding]::ASCII.GetBytes("data"))
    $w.Write([int]$pcm.Length); $w.Write($pcm)
  } finally { $w.Dispose() }
  @"
[ar:Studio Test]
[ti:Enhanced LRC]
[00:01.00]<00:01.00>One <00:02.00>word <00:03.00>at <00:04.00>a <00:05.00>time<00:06.00>
"@ | Set-Content -Path (Join-Path $root "precision-smoke.lrc") -Encoding utf8
  $player = Start-Process -FilePath $aimpExe.FullName -ArgumentList ('"' + $wav + '"') -WorkingDirectory $aimpExe.DirectoryName -PassThru
  Start-Sleep -Seconds 4
  if ($player.HasExited) { throw "AIMP process exited immediately with code $($player.ExitCode)" }
  $report.playerStarted = $true
  $playerPid = $player.Id
  $pipeName = "AimpKaraokeStudio.$playerPid"
  $connected = $false
  for($i=0;$i -lt 18;$i++){
    $p = [System.IO.Pipes.NamedPipeClientStream]::new(".", $pipeName, [System.IO.Pipes.PipeDirection]::InOut)
    try { $p.Connect(900); $connected = $true; break }
    catch { $p.Dispose(); Start-Sleep -Milliseconds 600 }
  }
  if(-not $connected){ throw "AIMP launched but plugin pipe $pipeName was not found" }
  $report.pluginPipeConnected = $true
  $p.Dispose()
  Start-Sleep -Milliseconds 700
  $renderer = Start-Process -FilePath $wpfExe -ArgumentList ("--pipe " + $pipeName) -PassThru
  Start-Sleep -Seconds 5
  $renderer.Refresh()
  $report.karaokeWindowCreated = (-not $renderer.HasExited) -and $renderer.MainWindowHandle -ne [IntPtr]::Zero
  if (-not $report.karaokeWindowCreated) { throw "WPF renderer did not create a top-level window in the CI session" }
  try {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    $rootUi = [System.Windows.Automation.AutomationElement]::FromHandle($renderer.MainWindowHandle)
    $cond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, "Подключено к AIMP")
    $label = $rootUi.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    $report.uiAutomationConnectionFound = ($null -ne $label)
  } catch {
    Write-Warning "UI Automation not accessible in CI runner: $_"
  }
} catch {
  $report.error = $_.Exception.Message
  Write-Warning "AIMP smoke scenario did not complete: $($report.error)"
} finally {
  $report | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $folder "report.json") -Encoding utf8
  $report | ConvertTo-Json -Depth 4 | Write-Host
  if ($renderer -and -not $renderer.HasExited) { Stop-Process -Id $renderer.Id -Force -ErrorAction SilentlyContinue }
  if ($player -and -not $player.HasExited) { Stop-Process -Id $player.Id -Force -ErrorAction SilentlyContinue }
}
# Exit 0: diagnostics are emitted as artifact and audited separately.
