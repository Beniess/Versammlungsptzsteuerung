[CmdletBinding()]
param(
    [ValidateSet('Layout', 'LayoutPolicy', 'Controls', 'PhotosAndPreview', 'All')]
    [string]$Scenario = 'Layout',
    [string]$AppPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'CameraControl.WinUI.Tests\CameraControl.WinUI.Tests.csproj'
$task = if ($Scenario -eq 'PhotosAndPreview') { 'T7' } else { 'T5' }
$evidenceRoot = Join-Path $repoRoot ('.omo\evidence\winui3-modernisierung\' + $task)
$runRoot = Join-Path $evidenceRoot ('run-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$null = New-Item -ItemType Directory -Path $runRoot -Force
$environmentNames = @('CAMERA_UI_EXE', 'CAMERA_UI_EVIDENCE_DIRECTORY', 'CAMERA_UI_REPO_ROOT')
$savedEnvironment = @{}
foreach ($name in $environmentNames) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

try {
    if ($Scenario -notin @('Layout', 'LayoutPolicy', 'PhotosAndPreview')) {
        throw "Szenario $Scenario benötigt noch nicht implementierte T6-/T7-Nachweise. Kein Szenario wird übersprungen."
    }
    if (-not $AppPath) {
        $AppPath = if ($Scenario -eq 'PhotosAndPreview') { Join-Path $repoRoot 'artifacts\win-x64\CameraControl.WinUI.exe' }
            else { Join-Path $repoRoot 'CameraControl.WinUI\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\CameraControl.WinUI.exe' }
    }
    $AppPath = [IO.Path]::GetFullPath($AppPath)
    if ($Scenario -ne 'LayoutPolicy' -and -not (Test-Path -LiteralPath $AppPath -PathType Leaf)) {
        throw "QA-EXE fehlt: $AppPath"
    }
    [Environment]::SetEnvironmentVariable('CAMERA_UI_EXE', $AppPath, 'Process')
    [Environment]::SetEnvironmentVariable('CAMERA_UI_EVIDENCE_DIRECTORY', $runRoot, 'Process')
    [Environment]::SetEnvironmentVariable('CAMERA_UI_REPO_ROOT', $repoRoot, 'Process')
    [pscustomobject]@{
        Scenario = $Scenario
        Executable = $AppPath
        QaPolicy = 'Eigene Prozessinstanz; keine VideoProbe; keine Produktregistrierung; isolierter QA-Datenpfad'
        StartedUtc = (Get-Date).ToUniversalTime().ToString('o')
        RequiredDpiPercent = @(100, 125, 150, 200)
        DpiPolicy = 'Nur tatsächlich gemessene Windows-DPI gilt als Nachweis.'
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $runRoot 'environment.json')
    $dotnet = Get-Command dotnet -ErrorAction Stop
    $restoreArguments = @('restore', $project)
    $testArtifacts = Join-Path $repoRoot ('.worktools\desktop-test-artifacts\' + $task)
    $buildProperties = @('-p:UseArtifactsOutput=true', "-p:ArtifactsPath=$testArtifacts")
    $restoreArguments += $buildProperties
    if (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $project) 'packages.lock.json')) {
        $restoreArguments += '--locked-mode'
    }
    & $dotnet.Source @restoreArguments 2>&1 | Tee-Object -FilePath (Join-Path $runRoot 'restore.log') | Write-Host
    if ($LASTEXITCODE -ne 0) { throw "Test-Restore fehlgeschlagen: $LASTEXITCODE" }
    $sourceFiles = @(
        Get-ChildItem -LiteralPath (Split-Path -Parent $project) -File | Where-Object { $_.Extension -in @('.cs', '.csproj') -or $_.Name -eq 'packages.lock.json' }
        Get-Item -LiteralPath $PSCommandPath
    )
    $sourceFiles | ForEach-Object {
        [pscustomobject]@{ Path = [IO.Path]::GetRelativePath($repoRoot, $_.FullName);
            SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'source-hashes.json')
    $filter = if ($Scenario -eq 'LayoutPolicy') { 'TestCategory=LayoutPolicy' }
        elseif ($Scenario -eq 'PhotosAndPreview') { 'TestCategory=PhotosAndPreview|TestCategory=VideoAdapters|TestCategory=VideoShutdown' }
        else { 'TestCategory=LayoutPolicy|TestCategory=Layout' }
    $arguments = @('test', $project, '--configuration', 'Release', '--no-restore', '--filter', $filter,
        '--logger', 'trx;LogFileName=results.trx', '--results-directory', $runRoot, '--blame-hang-timeout', '120s') + $buildProperties
    & $dotnet.Source @arguments 2>&1 | Tee-Object -FilePath (Join-Path $runRoot 'test.log') | Write-Host
    $testExit = $LASTEXITCODE
    $trxPath = Join-Path $runRoot 'results.trx'
    if (-not (Test-Path -LiteralPath $trxPath)) { throw 'TRX fehlt. Kein gültiger Testnachweis.' }
    [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
    $counts = $trx.TestRun.ResultSummary.Counters
    $total = [int]$counts.total
    $executed = [int]$counts.executed
    $passed = [int]$counts.passed
    $failed = [int]$counts.failed
    [pscustomobject]@{ Scenario = $Scenario; Total = $total; Executed = $executed; Passed = $passed; Failed = $failed;
        ExitCode = $testExit; Trx = $trxPath; DpiMatrixStatus = 'Noch nicht vollständig'; Evidence = $runRoot } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'summary.json')
    if ($testExit -ne 0 -or $total -eq 0 -or $executed -ne $total -or $passed -ne $total -or $failed -ne 0) {
        throw "Desktop-Test nicht bestanden: Gesamt=$total, Ausgeführt=$executed, Bestanden=$passed, Fehler=$failed, Exitcode=$testExit"
    }
    if ($Scenario -eq 'Layout') {
        throw 'Layoutbasis bestanden. Die vollständige DPI-/Themen-/Zustandsmatrix ist noch nicht implementiert.'
    }
    Write-Host "Belege: $runRoot"
    exit 0
}
catch {
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $runRoot 'failure.txt')
    Write-Error $_ -ErrorAction Continue
    exit 1
}
finally {
    foreach ($name in $environmentNames) {
        [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process')
    }
}
