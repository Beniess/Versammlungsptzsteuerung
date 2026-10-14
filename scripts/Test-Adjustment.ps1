[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishedExe,
    [Parameter(Mandatory)][string]$RunnerDirectory,
    [ValidateSet('1.0.0.5','1.0.0.6')][string]$ExpectedVersion = '1.0.0.5',
    [switch]$Run
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
$id = (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
$work = Join-Path ([IO.Path]::GetTempPath()) ('CameraControl.Adjustment.Guest\' + $id)
$payload = Join-Path $work 'payload'
$evidenceArea = if ($ExpectedVersion -eq '1.0.0.6') { 'foto-direkt-speichern' } else { 'responsive-nachjustierung' }
$evidence = Join-Path $repo ('.omo\evidence\' + $evidenceArea + '\guest-' + $id)
$guest = Join-Path $evidence 'guest-output'
$PublishedExe = [IO.Path]::GetFullPath($PublishedExe)
$RunnerDirectory = [IO.Path]::GetFullPath($RunnerDirectory)
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($PublishedExe).FileVersion -ne $ExpectedVersion) { throw "Es wird genau Kandidat $ExpectedVersion benötigt." }
if (-not (Test-Path -LiteralPath (Join-Path $RunnerDirectory 'NativeAdjustmentRunner.exe')) -or
    -not (Test-Path -LiteralPath (Join-Path $RunnerDirectory 'coreclr.dll'))) { throw 'Der private selbstständige QA-Runner fehlt.' }
function Get-SandboxProcesses { @(Get-CimInstance Win32_Process | Where-Object {$_.Name -in @('WindowsSandbox.exe','WindowsSandboxClient.exe','WindowsSandboxRemoteSession.exe')}) }
if ($Run -and @(Get-SandboxProcesses).Count -ne 0) { throw 'Ein anderer Sandbox-Gast ist noch aktiv.' }
$null=[IO.Directory]::CreateDirectory($payload)
$null=[IO.Directory]::CreateDirectory($guest)
$manifest=[Collections.Generic.List[object]]::new()
function Copy-Verified([string]$Source,[string]$Relative) {
    $target=[IO.Path]::GetFullPath((Join-Path $payload $Relative))
    if (-not $target.StartsWith($payload+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Fremder Payload-Zielpfad.' }
    $null=[IO.Directory]::CreateDirectory((Split-Path $target -Parent))
    $inputFile=[IO.File]::OpenRead($Source)
    try {
        $outputFile=[IO.File]::Open($target,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        try {$inputFile.CopyTo($outputFile,1048576);$outputFile.Flush($true)}finally{$outputFile.Dispose()}
    } finally {$inputFile.Dispose()}
    $hash=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    if ($hash -ne (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash) { throw 'Die Payload-Kopie ist nicht identisch.' }
    $manifest.Add([pscustomobject]@{Path=$Relative.Replace('\','/');Length=(Get-Item -LiteralPath $target).Length;SHA256=$hash})
}
Copy-Verified $PublishedExe 'App\CameraControl.WinUI.exe'
foreach ($file in Get-ChildItem -LiteralPath $RunnerDirectory -File -Recurse) { Copy-Verified $file.FullName ('Runner\' + [IO.Path]::GetRelativePath($RunnerDirectory,$file.FullName)) }
Copy-Verified (Join-Path $repo 'tests\fixtures\visca\packets.tsv') 'Repo\tests\fixtures\visca\packets.tsv'
if ($ExpectedVersion -eq '1.0.0.6') {
    Copy-Verified (Join-Path $repo '.omo\evidence\winui3-modernisierung\T3\tools.lock.json') 'Repo\.omo\evidence\winui3-modernisierung\T3\tools.lock.json'
    Copy-Verified (Join-Path $repo 'tests\fixtures\video\rtsp-h264-640x360.mp4') 'Repo\tests\fixtures\video\rtsp-h264-640x360.mp4'
    Copy-Verified (Join-Path $repo 'tests\fixtures\video\fixture.json') 'Repo\tests\fixtures\video\fixture.json'
    $mediaTools = (Get-Content -LiteralPath (Join-Path $repo '.omo\evidence\winui3-modernisierung\T3\tools.lock.json') -Raw | ConvertFrom-Json).tools
    foreach ($tool in @($mediaTools | Where-Object id -In @('MediaMTX','FFmpeg Windows essentials'))) {
        $source = [IO.Path]::GetFullPath((Join-Path $repo $tool.executable))
        if (-not $source.StartsWith($repo + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Fremder Medienwerkzeugpfad.' }
        Copy-Verified $source ('Repo\' + $tool.executable)
    }
}
Copy-Verified (Join-Path $repo 'tests\adjustment-qa\Run-AdjustmentGuest.ps1') 'Run-AdjustmentGuest.ps1'
$manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $payload 'payload-manifest.json') -Encoding utf8
[IO.File]::WriteAllBytes((Join-Path $evidence 'payload-manifest.json'),[IO.File]::ReadAllBytes((Join-Path $payload 'payload-manifest.json')))
$sourceFiles=@($PSCommandPath,(Join-Path $repo 'CameraControl.WinUI.Tests\NativeAdjustmentTests.cs'),(Join-Path $repo 'CameraControl.WinUI.Tests\PhotoViscaFixture.cs'),(Join-Path $repo 'CameraControl.WinUI.Tests\NativeQaSession.cs')) + @(Get-ChildItem -LiteralPath (Join-Path $repo 'tests\adjustment-qa') -File | Select-Object -ExpandProperty FullName)
@($sourceFiles | ForEach-Object {[pscustomobject]@{Path=[IO.Path]::GetRelativePath($repo,$_);SHA256=(Get-FileHash -LiteralPath $_).Hash}}) | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'source-hashes.json')
$config=Join-Path $work 'adjustment.wsb'
$payloadXml=[Security.SecurityElement]::Escape($payload)
$guestXml=[Security.SecurityElement]::Escape($guest)
@"
<Configuration>
  <Networking>Disable</Networking><ClipboardRedirection>Disable</ClipboardRedirection>
  <AudioInput>Disable</AudioInput><VideoInput>Disable</VideoInput><PrinterRedirection>Disable</PrinterRedirection>
  <MappedFolders>
    <MappedFolder><HostFolder>$payloadXml</HostFolder><SandboxFolder>C:\Payload</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$guestXml</HostFolder><SandboxFolder>C:\Evidence</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
  </MappedFolders>
  <LogonCommand><Command>powershell.exe -NoLogo -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File C:\Payload\Run-AdjustmentGuest.ps1</Command></LogonCommand>
</Configuration>
"@ | Set-Content -LiteralPath $config -Encoding utf8
[IO.File]::WriteAllBytes((Join-Path $evidence 'adjustment.wsb'),[IO.File]::ReadAllBytes($config))
[pscustomobject]@{PublishedExe=$PublishedExe;FileVersion=$ExpectedVersion;SHA256=(Get-FileHash -LiteralPath $PublishedExe).Hash;RunnerDirectory=$RunnerDirectory;Config=$config;Evidence=$evidence;RunRequested=[bool]$Run;RequiredCases=1;HostInputs=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'prepared.json')
Write-Host "Nachjustier-Belege: $evidence"
if (-not $Run) { return }
if (@(Get-SandboxProcesses).Count -ne 0) { throw 'Der Gastslot wurde inzwischen belegt.' }
$launcher=Start-Process -FilePath (Join-Path $env:WINDIR 'System32\WindowsSandbox.exe') -ArgumentList ('"'+$config+'"') -WindowStyle Hidden -PassThru
$failure=$null
try {
    $deadline=[DateTime]::UtcNow.AddMinutes(15)
    $earlyReported=$false
    do {
        $early=Join-Path $guest 'Adjustment\early-screens-ready.json'
        if (-not $earlyReported -and (Test-Path -LiteralPath $early)) {
            $images=Get-Content -LiteralPath $early -Raw | ConvertFrom-Json
            if ($images.Passed -and $images.Count -eq 3) {
                $paths=@(foreach($path in $images.Screenshots) { if (-not $path.StartsWith('C:\Evidence\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Fremder Screenshotpfad.' }; Join-Path $guest $path.Substring('C:\Evidence\'.Length) })
                $paths | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'early-screens-host.json')
                Write-Host ('EARLY-SCREENS: '+($paths | ConvertTo-Json -Compress))
                $earlyReported=$true
            }
        }
        if (Test-Path -LiteralPath (Join-Path $guest 'completion.json')) { break }
        Start-Sleep -Seconds 2
    } while ([DateTime]::UtcNow -lt $deadline)
    if (-not (Test-Path -LiteralPath (Join-Path $guest 'completion.json'))) { throw 'Der begrenzte einzelne Gastlauf hat keinen Abschlussbeleg.' }
    $completion=Get-Content -LiteralPath (Join-Path $guest 'completion.json') -Raw | ConvertFrom-Json
    if ($completion.ExitCode -ne 0) { throw "Nachjustierfall fehlgeschlagen: $($completion.Error)" }
    $result=Get-Content -LiteralPath (Join-Path $guest 'Adjustment\adjustment-result.json') -Raw | ConvertFrom-Json
    if (-not $result.Passed -or -not $earlyReported) { throw 'Der einzelne Nachjustierfall oder die drei frühen Bilder sind nicht vollständig belegt.' }
}
catch { $failure=$_ }
finally {
    $end=[DateTime]::UtcNow.AddSeconds(30)
    do {
        $own=@(Get-SandboxProcesses | Where-Object {$_.CommandLine -and $_.CommandLine.Contains($config,[StringComparison]::OrdinalIgnoreCase)})
        if ($own.Count -eq 0) { break }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $end)
    $close=@(foreach($item in $own) {
        $process=Get-Process -Id $item.ProcessId -ErrorAction SilentlyContinue
        if ($process) { try {[pscustomobject]@{ProcessId=$item.ProcessId;Accepted=$process.CloseMainWindow()}}finally{$process.Dispose()} }
    })
    $end=[DateTime]::UtcNow.AddSeconds(30)
    do {
        $own=@(Get-SandboxProcesses | Where-Object {$_.CommandLine -and $_.CommandLine.Contains($config,[StringComparison]::OrdinalIgnoreCase)})
        if ($own.Count -eq 0) { break }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $end)
    [pscustomobject]@{OwnCloseRequests=$close;RemainingOwnClients=@($own | Select-Object ProcessId,Name);KillUsed=$false;HostInputs=$false} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'cleanup.json')
    $launcher.Dispose()
    if ($own.Count -ne 0 -and -not $failure) { $failure=[InvalidOperationException]::new('Der eigene Gastclient ist noch aktiv.') }
}
if ($failure) { throw $failure }
Write-Host 'Ein Nachjustierfall mit drei frühen nativen Bildern ist bestanden.'
