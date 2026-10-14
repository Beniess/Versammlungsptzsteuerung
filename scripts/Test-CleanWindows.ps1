[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishedExe,
    [Parameter(Mandatory)][string]$RunnerDirectory,
    [switch]$Run,
    [ValidateRange(120, 1800)][int]$TimeoutSeconds = 900
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$id = (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$work = Join-Path ([IO.Path]::GetTempPath()) ('CameraControl.CleanWindows.Guest\' + $id)
$payload = Join-Path $work 'payload'
$evidence = Join-Path $repoRoot ('.omo\evidence\winui3-modernisierung\T9\clean-windows-' + $id)
$guestEvidence = Join-Path $evidence 'guest-output'
$manifest = [Collections.Generic.List[object]]::new()

function Copy-VerifiedFile([string]$Source, [string]$RelativeTarget) {
    $sourcePath = [IO.Path]::GetFullPath($Source)
    $target = [IO.Path]::GetFullPath((Join-Path $payload $RelativeTarget))
    if (-not $target.StartsWith($payload + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Ungültiger Payload-Zielpfad.' }
    if ((Get-Item -LiteralPath $sourcePath).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Verknüpfte Quelldatei: $sourcePath" }
    $null = [IO.Directory]::CreateDirectory((Split-Path -Parent $target))
    $inputFile = [IO.File]::Open($sourcePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $outputFile = [IO.File]::Open($target, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $inputFile.CopyTo($outputFile); $outputFile.Flush($true) } finally { $outputFile.Dispose() }
    } finally { $inputFile.Dispose() }
    $sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
    $targetHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    if ($sourceHash -ne $targetHash) { throw "Kopierprüfung fehlgeschlagen: $RelativeTarget" }
    $sourceVersion = if ([IO.Path]::GetExtension($sourcePath) -eq '.exe') { [Diagnostics.FileVersionInfo]::GetVersionInfo($sourcePath).FileVersion } else { $null }
    $targetVersion = if ($sourceVersion) { [Diagnostics.FileVersionInfo]::GetVersionInfo($target).FileVersion } else { $null }
    if ($sourceVersion -ne $targetVersion) { throw "Dateiversion der Kopie weicht ab: $RelativeTarget" }
    $manifest.Add([pscustomobject]@{ Path=$RelativeTarget.Replace('\', '/'); SHA256=$targetHash; Length=(Get-Item -LiteralPath $target).Length; FileVersion=$targetVersion })
}

function Sandbox-Processes {
    @(Get-CimInstance Win32_Process | Where-Object { $_.Name -in @('WindowsSandbox.exe', 'WindowsSandboxClient.exe', 'WindowsSandboxRemoteSession.exe') })
}

$PublishedExe = [IO.Path]::GetFullPath($PublishedExe)
$RunnerDirectory = [IO.Path]::GetFullPath($RunnerDirectory).TrimEnd('\')
if (-not (Test-Path -LiteralPath $PublishedExe -PathType Leaf)) { throw 'Die veröffentlichte EXE fehlt.' }
if (-not (Test-Path -LiteralPath (Join-Path $RunnerDirectory 'NativeRunner.exe') -PathType Leaf)) { throw 'Der veröffentlichte NativeRunner fehlt.' }
if (-not (Test-Path -LiteralPath (Join-Path $RunnerDirectory 'coreclr.dll') -PathType Leaf)) { throw 'NativeRunner muss seine private .NET-Runtime mitbringen.' }
if ($Run -and @(Sandbox-Processes).Count -ne 0) { throw 'Eine andere Windows-Sandbox läuft. Dieser Test startet keine zweite Sandbox.' }
$null = [IO.Directory]::CreateDirectory($payload)
$null = [IO.Directory]::CreateDirectory($guestEvidence)
$sourceFiles = @($PSCommandPath) + @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'tests\clean-windows') -File | Select-Object -ExpandProperty FullName) +
    @((Join-Path $repoRoot 'CameraControl.WinUI.Tests\NativeRunner\Program.cs'), (Join-Path $repoRoot 'CameraControl.WinUI.Tests\NativeRunner\NativeRunner.csproj'))
@($sourceFiles | ForEach-Object { [pscustomobject]@{ Path=[IO.Path]::GetRelativePath($repoRoot, $_); SHA256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash } }) |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'harness-source-hashes.json')
Copy-VerifiedFile $PublishedExe 'App\CameraControl.WinUI.exe'
foreach ($file in Get-ChildItem -LiteralPath $RunnerDirectory -Recurse -File) {
    $relative = [IO.Path]::GetRelativePath($RunnerDirectory, $file.FullName)
    Copy-VerifiedFile $file.FullName ('Runner\' + $relative)
}
foreach ($name in @('Run-CleanWindowsGuest.ps1', 'RunCleanWindows.cmd')) {
    Copy-VerifiedFile (Join-Path $repoRoot ('tests\clean-windows\' + $name)) $name
}
$toolsLock = Get-Content -LiteralPath (Join-Path $repoRoot '.omo\evidence\winui3-modernisierung\T3\tools.lock.json') -Raw | ConvertFrom-Json
foreach ($selection in @(@('MediaMTX', 'Media\mediamtx.exe'), @('FFmpeg Windows essentials', 'Media\ffmpeg.exe'))) {
    $tool = @($toolsLock.tools | Where-Object id -EQ $selection[0])
    if ($tool.Count -ne 1) { throw "Werkzeug ist nicht eindeutig: $($selection[0])" }
    $source = Join-Path $repoRoot $tool[0].executable
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $tool[0].executableSha256) { throw 'Werkzeugprüfsumme weicht vom T3-Lock ab.' }
    Copy-VerifiedFile $source $selection[1]
}
$fixture = Get-Content -LiteralPath (Join-Path $repoRoot 'tests\fixtures\video\fixture.json') -Raw | ConvertFrom-Json
$fixturePath = Join-Path $repoRoot ('tests\fixtures\video\' + $fixture.file)
if ((Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash -ne $fixture.sha256) { throw 'Die Video-Fixture stimmt nicht mit dem Manifest überein.' }
Copy-VerifiedFile $fixturePath 'Media\fixture.mp4'
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $payload 'payload-manifest.json') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $payload 'payload-manifest.json') -Destination (Join-Path $evidence 'payload-manifest.json')
$sandboxFile = Join-Path $work 'clean-windows.wsb'
$payloadXml = [Security.SecurityElement]::Escape($payload)
$evidenceXml = [Security.SecurityElement]::Escape($guestEvidence)
@"
<Configuration>
  <Networking>Disable</Networking>
  <ClipboardRedirection>Disable</ClipboardRedirection>
  <AudioInput>Disable</AudioInput>
  <VideoInput>Disable</VideoInput>
  <PrinterRedirection>Disable</PrinterRedirection>
  <MappedFolders>
    <MappedFolder><HostFolder>$payloadXml</HostFolder><SandboxFolder>C:\Payload</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$evidenceXml</HostFolder><SandboxFolder>C:\Evidence</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
  </MappedFolders>
  <LogonCommand><Command>cmd.exe /c C:\Payload\RunCleanWindows.cmd</Command></LogonCommand>
</Configuration>
"@ | Set-Content -LiteralPath $sandboxFile -Encoding utf8
Copy-Item -LiteralPath $sandboxFile -Destination (Join-Path $evidence 'clean-windows.wsb')
[pscustomobject]@{ PreparedUtc=[DateTime]::UtcNow.ToString('o'); PublishedExe=$PublishedExe; RunnerDirectory=$RunnerDirectory;
    SandboxFile=$sandboxFile; Payload=$payload; GuestEvidence=$guestEvidence; RunRequested=[bool]$Run;
    SourceContract='T9/environment/environment-probe.wsb'; GuestMode='normal release, no product QA variables';
    HostDesktopInput=$false; HostProductRegistration=$false } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'prepared.json')
Write-Host "Vorbereitet: $sandboxFile"
Write-Host "Belege: $evidence"
if (-not $Run) { return }

$sandbox = Join-Path $env:WINDIR 'System32\WindowsSandbox.exe'
if (-not (Test-Path -LiteralPath $sandbox)) { throw 'Windows Sandbox ist nicht verfügbar. Es werden keine Windows-Funktionen installiert.' }
if (@(Sandbox-Processes).Count -ne 0) { throw 'Eine andere Windows-Sandbox wurde inzwischen gestartet.' }
$launcher = Start-Process -FilePath $sandbox -ArgumentList ('"' + $sandboxFile + '"') -WindowStyle Hidden -PassThru
$ownClients = @()
$failure = $null
try {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $ownClients = @(Sandbox-Processes | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($sandboxFile, [StringComparison]::OrdinalIgnoreCase) })
        [pscustomobject]@{ LauncherId=$launcher.Id; OwnClients=@($ownClients | Select-Object ProcessId, Name, ExecutablePath, CommandLine) } |
            ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'launch-status.json')
        if (Test-Path -LiteralPath (Join-Path $guestEvidence 'completion.json')) { break }
        Start-Sleep -Seconds 2
    } while ([DateTime]::UtcNow -lt $deadline)
    if (-not (Test-Path -LiteralPath (Join-Path $guestEvidence 'completion.json'))) { throw 'Der Gast hat keinen Abschlussbeleg innerhalb der Testfrist geschrieben.' }
    $completion = Get-Content -LiteralPath (Join-Path $guestEvidence 'completion.json') -Raw | ConvertFrom-Json
    if ($completion.ExitCode -ne 0) { throw "Gastprüfung fehlgeschlagen: $($completion.Error)" }
    $resultPath = Join-Path $guestEvidence 'Probe\clean-windows-results.json'
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if ($result.Passed -ne $true -or $result.Scenarios.Count -ne 4 -or $result.Cleanup.RemainingOwnProcessIds.Count -ne 0) {
        throw 'Pflichtszenarien oder Prozessbereinigung sind nicht vollständig grün.'
    }
}
catch { $failure = $_ }
finally {
    $exitDeadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $remaining = @(Sandbox-Processes | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($sandboxFile, [StringComparison]::OrdinalIgnoreCase) })
        if ($remaining.Count -eq 0) { break }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $exitDeadline)
    $closeRequests = @()
    foreach ($item in $remaining) {
        $process = Get-Process -Id $item.ProcessId -ErrorAction SilentlyContinue
        if ($process) { $closeRequests += [pscustomobject]@{ ProcessId=$item.ProcessId; Accepted=$process.CloseMainWindow() }; $process.Dispose() }
    }
    if ($remaining.Count -gt 0) { Start-Sleep -Seconds 2 }
    $remaining = @(Sandbox-Processes | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($sandboxFile, [StringComparison]::OrdinalIgnoreCase) })
    [pscustomobject]@{ OwnCloseRequests=$closeRequests; RemainingOwnClients=@($remaining | Select-Object ProcessId, Name);
        KillUsed=$false; PayloadKept=$true; HostAppStarted=$false; ProductHostRegistration=$false;
        CompletedUtc=[DateTime]::UtcNow.ToString('o') } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'cleanup.json')
    $launcher.Dispose()
    if ($remaining.Count -gt 0 -and -not $failure) { $failure = [InvalidOperationException]::new('Der eigene Sandbox-Client ist noch aktiv.') }
}
if ($failure) { throw $failure }
Write-Host 'Clean-Windows: alle vier Szenarien und Bereinigung grün.'
