[CmdletBinding()]
param(
    [ValidateSet('All', 'Smoke', 'StreamFailure', 'MissingNative')]
    [string]$Scenario = 'All',
    [switch]$UsePublishedExe,
    [switch]$ExternalWindowCapture,
    [string]$NegativeBuildReadyPath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$runRoot = Join-Path $repoRoot ('.omo\evidence\winui3-modernisierung\T3\run-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$null = New-Item -ItemType Directory -Path $runRoot -Force
$bundleCacheParent = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'CameraControl.VideoQa'))
$bundleCacheLeaf = (Split-Path -Leaf $runRoot) + '-' + [Guid]::NewGuid().ToString('N')
$bundleExtractRoot = [IO.Path]::GetFullPath((Join-Path $bundleCacheParent $bundleCacheLeaf))
$null = New-Item -ItemType Directory -Path $bundleExtractRoot
[pscustomobject]@{ Path=$bundleExtractRoot; Scope='Own QA bundle extraction'; ColdStartupTimeoutSeconds=60; MediaTimeoutSeconds=10 } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'bundle-cache.json')
$processes = [Collections.Generic.List[Diagnostics.Process]]::new()
$checks = [Collections.Generic.List[object]]::new()
$toolManifest = Get-Content -LiteralPath (Join-Path $repoRoot '.omo\evidence\winui3-modernisierung\T3\tools.lock.json') -Raw | ConvertFrom-Json
$mediaTool = $toolManifest.tools | Where-Object id -eq 'MediaMTX'
$ffmpegTool = $toolManifest.tools | Where-Object id -eq 'FFmpeg Windows essentials'
$mediamtx = Join-Path $repoRoot $mediaTool.executable
$ffmpeg = Join-Path $repoRoot $ffmpegTool.executable
$fixture = Join-Path $repoRoot 'tests\fixtures\video\rtsp-h264-640x360.mp4'
$app = Join-Path $repoRoot 'artifacts\win-x64\CameraControl.WinUI.exe'
if ($Scenario -eq 'MissingNative') { $app = Join-Path $repoRoot '.worktools\negative-native\CameraControl.WinUI.exe' }
if (-not $UsePublishedExe) { $app = Join-Path $repoRoot 'CameraControl.WinUI\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\CameraControl.WinUI.exe' }
$appProcess = $null
$sender = $null
$server = $null
$commandId = 0
$statePath = Join-Path $runRoot 'state.json'

function Assert-Check([bool]$Condition, [string]$Name) {
    $checks.Add([pscustomobject]@{ Name = $Name; Passed = $Condition; Utc = (Get-Date).ToUniversalTime().ToString('o') })
    if (-not $Condition) { throw "Prüfung fehlgeschlagen: $Name" }
}

function Start-TestProcess([string]$Path, [string[]]$Arguments, [string]$Name) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new($Path)
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WorkingDirectory = $runRoot
    if ($Name -eq 'app') { $startInfo.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = $bundleExtractRoot }
    foreach ($argument in $Arguments) { $startInfo.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($startInfo)
    $processes.Add($process)
    [pscustomobject]@{ Name = $Name; Pid = $process.Id; Path = $Path; Arguments = $Arguments } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $runRoot ($Name + '-process.json'))
    return $process
}

function Wait-State([scriptblock]$Predicate, [string]$Name, [int]$TimeoutSeconds = 10) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($Name -ne 'Befehl exit' -and $null -ne $appProcess -and $appProcess.HasExited) { throw "App beendet: $Name, Exitcode $($appProcess.ExitCode)" }
        if (Test-Path -LiteralPath $statePath) {
            try {
                $stream = [IO.FileStream]::new($statePath, [IO.FileMode]::Open, [IO.FileAccess]::Read,
                    ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
                $reader = [IO.StreamReader]::new($stream)
                try { $state = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
                if (& $Predicate $state) { return $state }
            } catch [System.IO.IOException] { }
        }
        Start-Sleep -Milliseconds 80
    }
    throw "Zeitgrenze überschritten: $Name"
}

function Send-ProbeCommand([string]$Action, [string]$Path = '') {
    $script:commandId++
    $command = [pscustomobject]@{ Id = $script:commandId; Action = $Action; Path = $Path }
    $temporary = Join-Path $runRoot 'command.tmp'
    $command | ConvertTo-Json | Set-Content -LiteralPath $temporary -Encoding utf8
    $destination = Join-Path $runRoot 'command.json'
    if (Test-Path -LiteralPath $destination) {
        [IO.File]::Replace($temporary, $destination, [System.Management.Automation.Language.NullString]::Value)
    } else { [IO.File]::Move($temporary, $destination) }
    return Wait-State { param($value) $value.LastCommandId -eq $script:commandId -and -not $value.Busy } "Befehl $Action"
}

function Get-FreePort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return ([Net.IPEndPoint]$listener.LocalEndpoint).Port } finally { $listener.Stop() }
}

function Start-Sender([int]$Port) {
    return Start-TestProcess $ffmpeg @('-hide_banner', '-loglevel', 'error', '-nostdin', '-re', '-stream_loop', '-1', '-i', $fixture,
        '-an', '-c:v', 'copy', '-f', 'rtsp', '-rtsp_transport', 'tcp', "rtsp://127.0.0.1:$Port/fixture") 'ffmpeg'
}

function Read-RtspSessions([int]$Port) {
    return @( (Invoke-RestMethod -Uri "http://127.0.0.1:$Port/v3/rtspsessions/list").items )
}

function Wait-ReaderCount([int]$Port, [int]$Count) {
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $readers = @(Read-RtspSessions $Port | Where-Object state -eq 'read')
        if ($readers.Count -eq $Count) { return }
        Start-Sleep -Milliseconds 80
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "RTSP-Leserzahl ist $($readers.Count); erwartet: $Count"
}

function Test-ImageMarkers([string]$Path) {
    $bitmap = [Drawing.Bitmap]::new($Path)
    try {
        $counts = @(0, 0, 0)
        for ($y = 0; $y -lt $bitmap.Height; $y += 4) {
            for ($x = 0; $x -lt $bitmap.Width; $x += 4) {
                $pixel = $bitmap.GetPixel($x, $y)
                if ($pixel.R -gt 190 -and $pixel.G -lt 70 -and $pixel.B -lt 70) { $counts[0]++ }
                if ($pixel.G -gt 190 -and $pixel.R -lt 70 -and $pixel.B -lt 70) { $counts[1]++ }
                if ($pixel.B -gt 190 -and $pixel.R -lt 70 -and $pixel.G -lt 70) { $counts[2]++ }
            }
        }
        Assert-Check ($bitmap.Width -ge 320 -and $bitmap.Height -ge 180) "Bildmaß: $Path"
        Assert-Check ($counts[0] -ge 20 -and $counts[1] -ge 20 -and $counts[2] -ge 20) "RGB-Marker: $Path"
        return [pscustomobject]@{ Width = $bitmap.Width; Height = $bitmap.Height; MarkerSamples = $counts }
    } finally { $bitmap.Dispose() }
}

function Save-VisibleVideo($State, [string]$Name) {
    $hwnd = [IntPtr][long]$State.Hwnd
    Assert-Check ([VideoQaNative]::IsWindowVisible($hwnd)) 'Fenster sichtbar'
    Assert-Check ([VideoQaNative]::Responsive($hwnd)) 'Fenster reagiert'
    [VideoQaNative]::ShowWindow($hwnd, 9) | Out-Null
    [VideoQaNative]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 200
    $path = Join-Path $runRoot ($Name + '.png')
    if ($ExternalWindowCapture) {
        [pscustomobject]@{ Hwnd = $State.Hwnd; Name = $Name; Path = $path; Title = 'Video-Prüfung — Versammlungs-Kamerasteuerung' } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'capture-request.json')
        Write-Host "WINDOW_CAPTURE_REQUIRED $path"
        $captureDeadline = [DateTime]::UtcNow.AddMinutes(3)
        while (-not (Test-Path -LiteralPath $path) -and [DateTime]::UtcNow -lt $captureDeadline) {
            if ($appProcess.HasExited) {
                $exitCode = [int]$appProcess.ExitCode
                $unsignedExitCode = [BitConverter]::ToUInt32([BitConverter]::GetBytes($exitCode), 0)
                [pscustomobject]@{ Pid = $appProcess.Id; ExitCode = $exitCode; ExitCodeHex = ('0x{0:X8}' -f $unsignedExitCode);
                    Capturing = $Name; Utc = (Get-Date).ToUniversalTime().ToString('o') } |
                    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'unexpected-app-exit.json')
                throw "Die App wurde während der Fensteraufnahme beendet: Exitcode $exitCode."
            }
            Start-Sleep -Milliseconds 80
        }
        Assert-Check (Test-Path -LiteralPath $path) 'Windows-Fensteraufnahme vorhanden'
    } else {
        $bounds = [VideoQaNative]::WindowBounds($hwnd)
        $bitmap = [Drawing.Bitmap]::new($bounds[2], $bounds[3])
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $dc = $graphics.GetHdc()
        try { Assert-Check ([VideoQaNative]::PrintWindow($hwnd, $dc, 2)) 'PrintWindow-Fensteraufnahme' }
        finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
        try { $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png) } finally { $bitmap.Dispose() }
    }
    Test-ImageMarkers $path | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot ($Name + '-image.json'))
    if (-not $ExternalWindowCapture) {
        $windowBounds = [VideoQaNative]::WindowBounds($hwnd)
        $frame = [Drawing.Bitmap]::new($path)
        try {
            $videoBounds = $State.VideoBounds
            $cropBounds = [Drawing.Rectangle]::new([int]($videoBounds.X - $windowBounds[0]), [int]($videoBounds.Y - $windowBounds[1]), [int]$videoBounds.Width, [int]$videoBounds.Height)
            $crop = $frame.Clone($cropBounds, $frame.PixelFormat)
            $cropPath = Join-Path $runRoot ($Name + '-video.png')
            try { $crop.Save($cropPath, [Drawing.Imaging.ImageFormat]::Png) } finally { $crop.Dispose() }
            Test-ImageMarkers $cropPath | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot ($Name + '-video-image.json'))
            return $cropPath
        } finally { $frame.Dispose() }
    }
    return $path
}

try {
    foreach ($required in @($app, $mediamtx, $ffmpeg, $fixture)) { Assert-Check (Test-Path -LiteralPath $required) "Datei vorhanden: $required" }
    $fixtureManifest = Get-Content -LiteralPath (Join-Path $repoRoot 'tests\fixtures\video\fixture.json') -Raw | ConvertFrom-Json
    Assert-Check ((Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash.ToLowerInvariant() -eq $fixtureManifest.sha256) 'Fester Video-Fixturehash'
    if ($UsePublishedExe) {
        $launchDirectory = Join-Path $runRoot 'launch'
        $null = New-Item -ItemType Directory -Path $launchDirectory
        $copiedApp = Join-Path $launchDirectory 'CameraControl.WinUI.exe'
        $launchSource = [IO.FileStream]::new($app, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        $launchDestination = [IO.FileStream]::new($copiedApp, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $launchSource.CopyTo($launchDestination, 1048576); $launchDestination.Flush($true) }
        finally { $launchDestination.Dispose(); $launchSource.Dispose() }
        Assert-Check ((Get-FileHash -LiteralPath $app).Hash -eq (Get-FileHash -LiteralPath $copiedApp).Hash) 'Unveränderte EXE in anderem Ordner'
        $app = $copiedApp
    }
    foreach ($tool in @($mediaTool, $ffmpegTool)) {
        $hash = (Get-FileHash -LiteralPath (Join-Path $repoRoot $tool.executable) -Algorithm SHA256).Hash.ToLowerInvariant()
        Assert-Check ($hash -eq $tool.executableSha256) "Werkzeughash: $($tool.id)"
    }
    Add-Type -AssemblyName System.Drawing
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class VideoQaNative {
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect bounds);
    public static int[] WindowBounds(IntPtr hwnd) { Rect bounds; if (!GetWindowRect(hwnd, out bounds)) throw new InvalidOperationException("Window bounds unavailable"); return new[] { bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top }; }
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    public static bool Responsive(IntPtr hwnd) { IntPtr result; return SendMessageTimeout(hwnd, 0, IntPtr.Zero, IntPtr.Zero, 2, 2000, out result) != IntPtr.Zero; }
    public static void SetDpiAwareness() { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
}
'@
    [VideoQaNative]::SetDpiAwareness()
    $rtspPort = Get-FreePort
    $apiPort = Get-FreePort
    while ($apiPort -eq $rtspPort) { $apiPort = Get-FreePort }
    $configPath = Join-Path $runRoot 'mediamtx.yml'
    @"
logLevel: info
logDestinations: [file]
logFile: mediamtx.log
api: true
apiAddress: 127.0.0.1:$apiPort
metrics: false
pprof: false
playback: false
rtsp: true
rtspAddress: 127.0.0.1:$rtspPort
rtspTransports: [tcp]
rtmp: false
hls: false
webrtc: false
srt: false
moq: false
paths:
  fixture:
    source: publisher
"@ | Set-Content -LiteralPath $configPath
    $server = Start-TestProcess $mediamtx @($configPath) 'mediamtx'
    $serverDeadline = [DateTime]::UtcNow.AddSeconds(5)
    $apiReady = $false
    do {
        if ($server.HasExited) { throw 'MediaMTX ist beendet.' }
        try { Read-RtspSessions $apiPort | Out-Null; $apiReady = $true } catch { Start-Sleep -Milliseconds 80 }
    } while (-not $apiReady -and [DateTime]::UtcNow -lt $serverDeadline)
    Assert-Check $apiReady 'MediaMTX-API an Loopback bereit'
    $sender = Start-Sender $rtspPort
    $publisherDeadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $publisherCount = @(Read-RtspSessions $apiPort | Where-Object state -eq 'publish').Count
        if ($publisherCount -eq 1) { break }
        if ($sender.HasExited) { throw 'FFmpeg-Sender ist beendet.' }
        Start-Sleep -Milliseconds 80
    } while ([DateTime]::UtcNow -lt $publisherDeadline)
    Assert-Check ($publisherCount -eq 1) 'Genau ein lokaler Sender'
    $appArguments = @('--video-probe', $runRoot, '--rtsp', "rtsp://127.0.0.1:$rtspPort/fixture")
    $appProcess = Start-TestProcess $app $appArguments 'app'
    $startupWatch = [Diagnostics.Stopwatch]::StartNew()
    $state = if ($Scenario -eq 'MissingNative') {
        Wait-State { param($value) $value.Hwnd -ne 0 -and $value.Phase -eq 'error' -and $value.Error -match 'Videobibliothek' } 'WinUI-Fehlerfenster ohne native Videodatei' 60
    } else {
        Wait-State { param($value) $value.Hwnd -ne 0 -and $value.Initialized } 'WinUI-Fenster und VideoView-Initialisierung' 60
    }
    $startupWatch.Stop()
    [pscustomobject]@{ Milliseconds=$startupWatch.ElapsedMilliseconds; Hwnd=$state.Hwnd; Initialized=$state.Initialized } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'startup-time.json')
    Assert-Check ($state.Hwnd -ne 0) 'WinUI-HWND vorhanden'
    if ($Scenario -eq 'MissingNative') {
        Assert-Check ($state.StartupFailure -and -not (Test-Path -LiteralPath $state.MissingNativeFile)) 'Echte native Paketdatei fehlt vor WinRT'
        Assert-Check ($state.Phase -eq 'error' -and $state.Error -match 'Videobibliothek') 'Fehlende native Datei wird angezeigt'
        Assert-Check (-not $state.Initialized -and $state.DecodedFrames -eq 0 -and $state.DisplayedFrames -eq 0) 'Fehlpaket erzeugt keine VideoView oder Frames'
        Assert-Check ([VideoQaNative]::Responsive([IntPtr][long]$state.Hwnd)) 'Fehlerfenster reagiert'
        $errorHwnd = [IntPtr][long]$state.Hwnd
        Assert-Check ([VideoQaNative]::IsWindowVisible($errorHwnd)) 'Fehlerfenster sichtbar'
        $errorBounds = [VideoQaNative]::WindowBounds($errorHwnd)
        $errorBitmap = [Drawing.Bitmap]::new($errorBounds[2], $errorBounds[3])
        try {
            $errorGraphics = [Drawing.Graphics]::FromImage($errorBitmap)
            $errorDc = $errorGraphics.GetHdc()
            try { Assert-Check ([VideoQaNative]::PrintWindow($errorHwnd, $errorDc, 2)) 'Fehlerfensteraufnahme vorhanden' }
            finally { $errorGraphics.ReleaseHdc($errorDc); $errorGraphics.Dispose() }
            $errorBitmap.Save((Join-Path $runRoot 'missing-native-window.png'), [Drawing.Imaging.ImageFormat]::Png)
        } finally { $errorBitmap.Dispose() }
        Wait-ReaderCount $apiPort 0
    } else {
        $cycles = if ($Scenario -eq 'All') { 10 } else { 1 }
        for ($cycle = 1; $cycle -le $cycles; $cycle++) {
            $state = Send-ProbeCommand 'open'
            $state = Wait-State { param($value) $value.Phase -eq 'playing' -and $value.DecodedFrames -gt 0 -and $value.DisplayedFrames -gt 0 } 'Decodiertes RTSP-Bild'
            Wait-ReaderCount $apiPort 1
            $snapshot = Join-Path $runRoot ("snapshot-$cycle.png")
            $state = Send-ProbeCommand 'snapshot' $snapshot
            Assert-Check ($state.Phase -eq 'playing' -and (Test-Path -LiteralPath $snapshot)) "Snapshot $cycle geschrieben"
            $image = Test-ImageMarkers $snapshot
            Assert-Check ($image.Width -eq 640 -and $image.Height -eq 360) "Snapshot $cycle hat Originalmaß"
            if ($cycle -eq 1) {
                $first = Save-VisibleVideo $state 'visible-1'
                Start-Sleep -Milliseconds 1100
                $state = Wait-State { param($value) $value.Phase -eq 'playing' } 'Bild aktualisiert'
                $second = Save-VisibleVideo $state 'visible-2'
                Assert-Check ((Get-FileHash -LiteralPath $first).Hash -ne (Get-FileHash -LiteralPath $second).Hash) 'Gerenderte Videobilder unterscheiden sich'
                $modules = @($appProcess.Modules | Where-Object { $_.ModuleName -in @('libvlc.dll', 'libvlccore.dll') } | Select-Object ModuleName,FileName)
                $modules | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'native-modules.json')
                Assert-Check ($modules.Count -eq 2) 'Beide nativen VLC-Module geladen'
                foreach ($module in $modules) { Assert-Check ($module.FileName.StartsWith($state.NativeDirectory, [StringComparison]::OrdinalIgnoreCase)) 'VLC-Modul aus mitgeliefertem Verzeichnis' }
                $nativePackageRoot = Join-Path $env:USERPROFILE '.nuget\packages\videolan.libvlc.windows\3.0.24\build\x64'
                $nativeManifest = [Collections.Generic.List[object]]::new()
                foreach ($nativeSource in (Get-ChildItem -LiteralPath $nativePackageRoot -File -Recurse)) {
                    $relative = [IO.Path]::GetRelativePath($nativePackageRoot, $nativeSource.FullName)
                    if ($relative -notmatch '^(libvlc\.|libvlccore\.|plugins\\|lua\\|hrtfs\\)') { continue }
                    $extractedFile = Join-Path $state.NativeDirectory $relative
                    Assert-Check (Test-Path -LiteralPath $extractedFile) "Native Bundle-Datei vorhanden: $relative"
                    $sourceHash = (Get-FileHash -LiteralPath $nativeSource.FullName -Algorithm SHA256).Hash
                    $extractedHash = (Get-FileHash -LiteralPath $extractedFile -Algorithm SHA256).Hash
                    Assert-Check ($sourceHash -eq $extractedHash) "Native Bundle-Datei entspricht Paket: $relative"
                    $nativeManifest.Add([pscustomobject]@{ Path = $relative; Sha256 = $extractedHash })
                }
                Assert-Check ($nativeManifest.Count -gt 100) 'Vollständiger nativer Plugin-/Dateibaum geprüft'
                $nativeManifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'native-bundle-files.json')
            }
            $state = Send-ProbeCommand 'close'
            Assert-Check ($state.Phase -eq 'stopped') "Playerzyklus $cycle beendet"
            Wait-ReaderCount $apiPort 0
        }
        if ($Scenario -in @('All', 'StreamFailure')) {
            $null = Send-ProbeCommand 'open'
            $null = Wait-State { param($value) $value.Phase -eq 'playing' -and $value.DecodedFrames -gt 0 -and $value.DisplayedFrames -gt 0 } 'Stream vor Ausfall'
            $sender.Kill()
            $sender.WaitForExit(3000) | Out-Null
            $state = Wait-State { param($value) $value.Phase -eq 'error' } 'Senderausfall erkannt' 15
            Assert-Check ([VideoQaNative]::Responsive([IntPtr][long]$state.Hwnd)) 'App reagiert bei Senderausfall'
            $null = Send-ProbeCommand 'close'
            $sender = Start-Sender $rtspPort
            Start-Sleep -Milliseconds 400
            $null = Send-ProbeCommand 'open'
            $null = Wait-State { param($value) $value.Phase -eq 'playing' -and $value.DecodedFrames -gt 0 -and $value.DisplayedFrames -gt 0 } 'Bild nach Neuverbindung'
            $reconnectedSnapshot = Join-Path $runRoot 'snapshot-reconnected.png'
            $reconnectedState = Send-ProbeCommand 'snapshot' $reconnectedSnapshot
            Assert-Check ($reconnectedState.Phase -eq 'playing' -and (Test-Path -LiteralPath $reconnectedSnapshot)) 'Frische PNG nach Neuverbindung geschrieben'
            $reconnectedImage = Test-ImageMarkers $reconnectedSnapshot
            Assert-Check ($reconnectedImage.Width -eq 640 -and $reconnectedImage.Height -eq 360) 'PNG nach Neuverbindung hat Originalmaß'
            $null = Send-ProbeCommand 'close'
            Wait-ReaderCount $apiPort 0
        }
    }
    if ($Scenario -eq 'MissingNative') {
        Assert-Check ([VideoQaNative]::PostMessage([IntPtr][long]$state.Hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)) 'Eigener Fehlerdialog geordnet geschlossen'
    } else { $null = Send-ProbeCommand 'exit' }
    Assert-Check ($appProcess.WaitForExit(5000)) 'App geordnet beendet'
    if ($Scenario -eq 'MissingNative') { Assert-Check ($appProcess.ExitCode -eq 3) 'Definierter Fehlpaket-Exitcode 3' }
    else { Assert-Check ($appProcess.ExitCode -eq 0) 'App-Exitcode null' }
    Wait-ReaderCount $apiPort 0
    if ($Scenario -eq 'All') {
        if ($NegativeBuildReadyPath) {
            Write-Host 'NEGATIVE_BUILD_WAIT_FOR_COMPLETE_UI_SOURCE'
            $buildDeadline = [DateTime]::UtcNow.AddMinutes(5)
            while (-not (Test-Path -LiteralPath $NegativeBuildReadyPath) -and [DateTime]::UtcNow -lt $buildDeadline) { Start-Sleep -Milliseconds 100 }
            Assert-Check (Test-Path -LiteralPath $NegativeBuildReadyPath) 'Vollständiger UI-Quellsatz vor Negativcompile freigegeben'
        }
        & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Publish-App.ps1') -Runtime win-x64 -CreateMissingNativeFixture
        if ($LASTEXITCODE -ne 0) { throw 'Negativpaket konnte nicht erzeugt werden.' }
        & pwsh -NoProfile -File $PSCommandPath -Scenario MissingNative -UsePublishedExe
        Assert-Check ($LASTEXITCODE -eq 0) 'Separates Negativpaket ohne libvlc.dll geprüft'
    }
    [pscustomobject]@{ Scenario = $Scenario; Passed = $true; Checks = $checks; AppSha256 = (Get-FileHash -LiteralPath $app).Hash; RtspPort = $rtspPort; ApiPort = $apiPort } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runRoot 'summary.json')
    Write-Host "Video-Prüfung bestanden: $runRoot"
} catch {
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $runRoot 'failure.txt')
    [pscustomobject]@{ Scenario = $Scenario; Passed = $false; Checks = $checks; Error = $_.Exception.Message } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runRoot 'summary.json')
    Write-Error $_ -ErrorAction Continue
    exit 1
} finally {
    $cleanup = [Collections.Generic.List[object]]::new()
    foreach ($process in $processes) {
        $exitedBeforeCleanup = $process.HasExited
        if (-not $process.HasExited) {
            if ($process -eq $appProcess) { $process.CloseMainWindow() | Out-Null; $process.WaitForExit(3000) | Out-Null }
            if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(3000) | Out-Null }
        }
        $cleanup.Add([pscustomobject]@{ Pid = $process.Id; ExitedBeforeCleanup = $exitedBeforeCleanup;
            ExitedAfterCleanup = $process.HasExited; ExitCode = if ($process.HasExited) { $process.ExitCode } else { $null } })
        $process.Dispose()
    }
    $cleanup | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'cleanup.json')
    $resolvedCache = [IO.Path]::GetFullPath($bundleExtractRoot)
    $cachePrefix = $bundleCacheParent.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedCache.StartsWith($cachePrefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedCache) -ne $bundleCacheLeaf) { throw 'QA-Cachepfad ist nicht der eigene Laufordner.' }
    $cacheFileCount = @(Get-ChildItem -LiteralPath $resolvedCache -File -Recurse).Count
    Remove-Item -LiteralPath $resolvedCache -Recurse -Force
    [pscustomobject]@{ Path=$resolvedCache; FilesBeforeCleanup=$cacheFileCount; ExistsAfterCleanup=(Test-Path -LiteralPath $resolvedCache) } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'bundle-cache-cleanup.json')
}
