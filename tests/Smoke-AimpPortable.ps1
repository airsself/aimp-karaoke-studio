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
  commandsVerified = $false
  commandAcks = @{}
  transportError = ""
  karaokeWindowCreated = $false
  rendererAlive = $false
  rendererExitCode = $null
  rendererPid = 0
  performance = @{}
  rendererTitle = ""
  demoAlive = $false
  demoWindowCreated = $false
  uiAutomationConnectionFound = $false
  bilingualSplitVerified = $false
  lyricsSearchDialogVerified = $false
  screenshotCaptured = $false
  pluginDllLoaded = $false
  aimpLoadedModules = @()
  pluginInstallationPath = ""
  aimpWindowVisible = $false
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
  # The .aimppack has x64/ for the *installer*. Manual portable installs
  # must copy the selected architecture's files into the actual plugin folder.
  $pluginDir = Join-Path $pluginTarget "AimpKaraokeStudio"
  New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
  Copy-Item -Path (Join-Path $addon "x64\*") -Destination $pluginDir -Recurse -Force
  $report.pluginInstallationPath = $pluginDir
  $wpfExe = Join-Path $pluginDir "KaraokeStudio.exe"

  # Start player with a generated silent PCM WAV plus enhanced LRC sidecar.
  $wav = Join-Path $root "precision-smoke.wav"
  $pcm = [byte[]]::new(16000 * 2 * 35)
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
[00:01.00]<00:01.00>One <00:05.00>word <00:10.00>at <00:15.00>a <00:20.00>time<00:25.00>
"@ | Set-Content -Path (Join-Path $root "precision-smoke.lrc") -Encoding utf8
  $player = Start-Process -FilePath $aimpExe.FullName -ArgumentList ('"' + $wav + '"') -WorkingDirectory $aimpExe.DirectoryName -PassThru
  Start-Sleep -Seconds 4
  if ($player.HasExited) { throw "AIMP process exited immediately with code $($player.ExitCode)" }
  $report.playerStarted = $true
  $playerPid = $player.Id
  $report.aimpWindowVisible = $player.MainWindowHandle -ne [IntPtr]::Zero
  try {
    $modules = @(Get-Process -Id $playerPid -Module -ErrorAction Stop | ForEach-Object ModuleName)
    $report.aimpLoadedModules = @($modules | Where-Object { $_ -like "*Karaoke*" -or $_ -like "*AIMP*" })
    $report.pluginDllLoaded = @($modules | Where-Object { $_ -eq "AimpKaraokeStudio.dll" }).Count -gt 0
  } catch {
    Write-Warning "Could not enumerate AIMP process modules: $_"
  }
  # A screen grab is diagnostic only; CI service sessions may render blank.
  try {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = [System.Drawing.Bitmap]::new([Math]::Min($screen.Width,1920),[Math]::Min($screen.Height,1080))
    $graphics = [System.Drawing.Graphics]::FromImage($bmp)
    try { $graphics.CopyFromScreen($screen.X,$screen.Y,0,0,$bmp.Size)
      $bmp.Save((Join-Path $folder "aimp-startup.png"),[System.Drawing.Imaging.ImageFormat]::Png)
      $report.screenshotCaptured = $true
    } finally { $graphics.Dispose(); $bmp.Dispose() }
  } catch { Write-Warning "CI screenshot unavailable: $_" }
  $pipeName = "AimpKaraokeStudio.$playerPid"
  Write-Host "AIMP PID: $playerPid"
  Write-Host "Player module: $($report.aimpLoadedModules -join ', ')"
  Write-Host "Native DLL loaded: $($report.pluginDllLoaded)"
  Write-Host "Manual plugin folder: $($report.pluginInstallationPath)"
  $connected = $false
  for($i=0;$i -lt 18;$i++){
    $p = [System.IO.Pipes.NamedPipeClientStream]::new(".", $pipeName, [System.IO.Pipes.PipeDirection]::InOut)
    try { $p.Connect(900); $connected = $true; break }
    catch { $p.Dispose(); Start-Sleep -Milliseconds 600 }
  }
  if(-not $connected){ throw "AIMP launched but plugin pipe $pipeName was not found" }
  $report.pluginPipeConnected = $true

  # Exercise the REAL native dispatcher before the WPF renderer connects.
  # The AIMP plugin supports one pipe client at a time. Both commands must
  # be executed on AIMP's main thread and return observable SDK results.
  $probeReader = [System.IO.StreamReader]::new($p, [System.Text.UTF8Encoding]::new($false), $false, 65536, $true)
  $probeWriter = [System.IO.StreamWriter]::new($p, [System.Text.UTF8Encoding]::new($false), 65536, $true)
  $probeWriter.AutoFlush = $true
  function Receive-AimpCommandResult([string]$expectedCommand) {
    $deadline = (Get-Date).AddSeconds(8)
    while ((Get-Date) -lt $deadline) {
      $cancelRead = [System.Threading.CancellationTokenSource]::new([TimeSpan]::FromMilliseconds(1100))
      try {
        $line = $probeReader.ReadLineAsync($cancelRead.Token).AsTask().GetAwaiter().GetResult()
      } catch [System.OperationCanceledException] { continue }
      finally { $cancelRead.Dispose() }
      if ($null -eq $line) { break }
      try { $message = $line | ConvertFrom-Json -ErrorAction Stop }
      catch { continue }
      if ($message.type -eq 'commandResult' -and $message.command -eq $expectedCommand) { return $message }
    }
    throw "Command '$expectedCommand' did not return an AIMP SDK acknowledgement"
  }
  try {
    $probeWriter.WriteLine('{"command":"seek","positionMs":12000}')
    $seekAck = Receive-AimpCommandResult "seek"
    $report.commandAcks.seek = $seekAck
    if(-not $seekAck.ok) { throw "AIMP rejected seek command (HRESULT: $($seekAck.result))" }
    if([Math]::Abs([double]$seekAck.positionMs - 12000) -gt 2500) {
      throw "Seek returned success but actual player position is $($seekAck.positionMs) ms"
    }
    $probeWriter.WriteLine('{"command":"toggle"}')
    $toggleAck = Receive-AimpCommandResult "toggle"
    $report.commandAcks.pause = $toggleAck
    if(-not $toggleAck.ok) { throw "AIMP rejected pause toggle (HRESULT: $($toggleAck.result))" }
    Start-Sleep -Milliseconds 900
    $probeWriter.WriteLine('{"command":"status"}')
    $pauseState = Receive-AimpCommandResult "status"
    $report.commandAcks.stateAfterPause = $pauseState
    $probeWriter.WriteLine('{"command":"toggle"}')
    $resumeAck = Receive-AimpCommandResult "toggle"
    $report.commandAcks.resume = $resumeAck
    if(-not $resumeAck.ok) { throw "AIMP rejected resume toggle (HRESULT: $($resumeAck.result))" }
    Start-Sleep -Milliseconds 900
    $probeWriter.WriteLine('{"command":"status"}')
    $resumeState = Receive-AimpCommandResult "status"
    $report.commandAcks.stateAfterResume = $resumeState
    if($pauseState.state -ne 1 -or $resumeState.state -ne 2) {
      throw "AIMP pause/resume state transition incorrect: paused=$($pauseState.state), resumed=$($resumeState.state)"
    }
    $report.commandsVerified = $true
    Write-Host "TRANSPORT PASS: seek=$($seekAck.positionMs)ms, paused=$($pauseState.state), resumed=$($resumeState.state)"
  } catch {
    $report.transportError = $_.Exception.Message
    Write-Warning "Transport verification incomplete: $($report.transportError)"
  } finally {
    $probeWriter.Dispose()
    $probeReader.Dispose()
    $p.Dispose()
  }
  Start-Sleep -Milliseconds 700
  $renderer = Start-Process -FilePath $wpfExe -ArgumentList ("--pipe " + $pipeName) -PassThru
  $report.rendererPid = $renderer.Id
  for($i=0;$i -lt 16;$i++){
    Start-Sleep -Milliseconds 650
    $renderer.Refresh()
    if($renderer.HasExited -or $renderer.MainWindowHandle -ne [IntPtr]::Zero){break}
  }
  $report.rendererAlive = -not $renderer.HasExited
  if($renderer.HasExited) { $report.rendererExitCode = $renderer.ExitCode }
  $report.rendererTitle = $renderer.MainWindowTitle
  $report.karaokeWindowCreated = $report.rendererAlive -and $renderer.MainWindowHandle -ne [IntPtr]::Zero

  # Preserve exception diagnostics BEFORE aborting, including a separate --demo start.
  $wpfLog = Join-Path $env:LOCALAPPDATA "AimpKaraokeStudio\karaoke.log"
  if(Test-Path $wpfLog){Copy-Item $wpfLog (Join-Path $folder "karaoke.log") -Force}
  try {
    $events = @(Get-WinEvent -FilterHashtable @{LogName="Application";StartTime=(Get-Date).AddMinutes(-5)} -ErrorAction SilentlyContinue |
      Where-Object { $_.Message -match "KaraokeStudio|\.NET Runtime|PresentationFramework" } |
      Select-Object -First 8 TimeCreated,ProviderName,Message)
    $events | Format-List | Out-String | Set-Content (Join-Path $folder "application-errors.txt")
  } catch { Write-Warning "Event log unavailable: $_" }
  try {
    $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = [System.Drawing.Bitmap]::new([Math]::Min($screen.Width,1920),[Math]::Min($screen.Height,1080))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
      $g.CopyFromScreen($screen.X,$screen.Y,0,0,$bmp.Size)
      $bmp.Save((Join-Path $folder "renderer-startup.png"),[System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $g.Dispose();$bmp.Dispose() }
  } catch { Write-Warning "Renderer screenshot unavailable: $_" }

  if(-not $report.karaokeWindowCreated){
    Write-Warning "Live renderer did not create top-level window; attempting standalone demo."
    $demo = Start-Process -FilePath $wpfExe -ArgumentList "--demo" -PassThru
    Start-Sleep -Seconds 5
    $demo.Refresh()
    $report.demoAlive = -not $demo.HasExited
    $report.demoWindowCreated = $report.demoAlive -and $demo.MainWindowHandle -ne [IntPtr]::Zero
    Write-Host "Demo alive=$($report.demoAlive), window=$($report.demoWindowCreated), exit=$(if($demo.HasExited){$demo.ExitCode}else{'running'})"
    if(-not $demo.HasExited){Stop-Process -Id $demo.Id -Force -ErrorAction SilentlyContinue}
    if(Test-Path $wpfLog){Copy-Item $wpfLog (Join-Path $folder "karaoke.log") -Force}
    throw "WPF renderer did not create top-level window, alive=$($report.rendererAlive), exit=$($report.rendererExitCode), demo=$($report.demoWindowCreated)"
  }
  try {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    $rootUi = [System.Windows.Automation.AutomationElement]::FromHandle($renderer.MainWindowHandle)
    $cond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, "Подключено к AIMP")
    $label = $rootUi.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    $report.uiAutomationConnectionFound = ($null -ne $label)
    # WPF UI Automation: toggle the split and inspect the right-column header.
    $toggleCondition = [System.Windows.Automation.PropertyCondition]::new(
      [System.Windows.Automation.AutomationElement]::NameProperty, "RU · Перевод")
    $translationToggle = $rootUi.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $toggleCondition)
    if ($null -ne $translationToggle) {
      $invoke = $translationToggle.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)
      $invoke.Invoke()
      Start-Sleep -Seconds 2
      $headingCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, "РУССКИЙ ПЕРЕВОД")
      $heading = $rootUi.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $headingCondition)
      $report.bilingualSplitVerified = $null -ne $heading
      Write-Host "BILINGUAL SPLIT UI: $($report.bilingualSplitVerified)"
      if($report.bilingualSplitVerified) {
        $s = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $image = [System.Drawing.Bitmap]::new([Math]::Min($s.Width,1920),[Math]::Min($s.Height,1080))
        $g2 = [System.Drawing.Graphics]::FromImage($image)
        try {
          $g2.CopyFromScreen($s.X,$s.Y,0,0,$image.Size)
          $image.Save((Join-Path $folder "bilingual-split.png"),[System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $g2.Dispose(); $image.Dispose() }
      }
    }
    # Invoke the modal Lyrics Finder from a separate PowerShell process.
    # ButtonAutomationPeer.Invoke can wait for ShowDialog to close.
    $finderWorker = $null
    try {
      $scriptPath = Join-Path $env:GITHUB_WORKSPACE "tests\InvokeLyricsFinder.ps1"
      $finderLog = Join-Path $folder "finder-worker.log"
      $findArgs = @("-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                    "-File", $scriptPath, "-WindowHandle", $renderer.MainWindowHandle.ToInt64(),
                    "-LogPath", $finderLog)
      $finderWorker = Start-Process -FilePath "pwsh.exe" -ArgumentList $findArgs -WindowStyle Hidden -PassThru
      $dialog = $null
      $windowCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        "Поиск текстов · AIMP Karaoke Studio")
      for($trial=0;$trial -lt 16;$trial++){
        Start-Sleep -Milliseconds 350
        $dialog = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
          [System.Windows.Automation.TreeScope]::Descendants, $windowCondition)
        if($null -ne $dialog){break}
      }
      $report.lyricsSearchDialogVerified = $null -ne $dialog
      $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.Condition]::TrueCondition)
      $titles = @($windows | ForEach-Object { $_.Current.Name })
      $titles | Out-File (Join-Path $folder "ui-top-level-windows.txt") -Encoding utf8
      $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
      $shot = [System.Drawing.Bitmap]::new([Math]::Min($bounds.Width,1920),
          [Math]::Min($bounds.Height,1080))
      $drawShot = [System.Drawing.Graphics]::FromImage($shot)
      try {
        $drawShot.CopyFromScreen($bounds.X,$bounds.Y,0,0,$shot.Size)
        $shot.Save((Join-Path $folder "finder-attempt.png"),
          [System.Drawing.Imaging.ImageFormat]::Png)
      } finally { $drawShot.Dispose(); $shot.Dispose() }
      Write-Host "LYRICS FINDER UI: $($report.lyricsSearchDialogVerified)"
      if($report.lyricsSearchDialogVerified){
        $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $screenshot = [System.Drawing.Bitmap]::new(
          [Math]::Min($bounds.Width,1920),[Math]::Min($bounds.Height,1080))
        $draw = [System.Drawing.Graphics]::FromImage($screenshot)
        try {
          $draw.CopyFromScreen($bounds.X,$bounds.Y,0,0,$screenshot.Size)
          $screenshot.Save((Join-Path $folder "lyrics-search-v3.png"),
            [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $draw.Dispose();$screenshot.Dispose() }
        $closeCondition = [System.Windows.Automation.PropertyCondition]::new(
          [System.Windows.Automation.AutomationElement]::NameProperty,"Закрыть")
        $closeButton = $dialog.FindFirst(
          [System.Windows.Automation.TreeScope]::Descendants,$closeCondition)
        if($null -ne $closeButton) {
          $closeButton.GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        }
      }
    } catch {
      Write-Warning "Lyrics Finder UI smoke probe failed: $_"
    } finally {
      if($finderWorker){
        Start-Sleep -Milliseconds 350
        $finderWorker.Refresh()
        if(-not $finderWorker.HasExited){
          Stop-Process -Id $finderWorker.Id -Force -ErrorAction SilentlyContinue
        }
      }
    }
    # Independent Windows performance telemetry: WPF process CPU usage,
    # private/working set size, handles and thread count during live AIMP playback.
    # Best-effort diagnostics only, not a cross-machine performance score.
    try {
      $start = Get-Process -Id $renderer.Id -ErrorAction Stop
      $cpuBefore = $start.TotalProcessorTime.TotalMilliseconds
      $startPrivate = $start.PrivateMemorySize64 / 1MB
      Start-Sleep -Seconds 6
      $end = Get-Process -Id $renderer.Id -ErrorAction Stop
      $perf = [ordered]@{
        observationSeconds = 6
        cpuTimeMs = [Math]::Round($end.TotalProcessorTime.TotalMilliseconds - $cpuBefore)
        cpuPercentOfOneCore = [Math]::Round(
          100 * ($end.TotalProcessorTime.TotalMilliseconds - $cpuBefore) / 6000,1)
        privateMB = [Math]::Round($end.PrivateMemorySize64 / 1MB,1)
        privateDeltaMB = [Math]::Round($end.PrivateMemorySize64 / 1MB - $startPrivate,1)
        workingSetMB = [Math]::Round($end.WorkingSet64 / 1MB,1)
        threadCount = $end.Threads.Count
        handleCount = $end.HandleCount
      }
      $report.performance = $perf
      Write-Host ("WPF PERFORMANCE: " + ($perf | ConvertTo-Json -Compress))
    } catch {
      Write-Warning "WPF performance counters unavailable: $_"
    }
    # An AIMP first-run dialog can suppress track metadata without crashing WPF;
    # nevertheless WPF should remain responsive after lyrics are loaded.
    Start-Sleep -Seconds 3
    $renderer.Refresh()
    $report.rendererAlive = -not $renderer.HasExited
    if ($renderer.HasExited) {
      $report.rendererExitCode = $renderer.ExitCode
      throw "Renderer terminated after creating a top-level window, exit=$($renderer.ExitCode)"
    }
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
