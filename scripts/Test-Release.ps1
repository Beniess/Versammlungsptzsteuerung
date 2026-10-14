[CmdletBinding()]
param(
    [ValidateSet('LocalUpdate', 'StartupPolicy', 'Smoke', 'All')]
    [string]$Scenario = 'LocalUpdate',
    [string]$ExeA,
    [string]$ExeB
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'CameraControl.WinUI.Tests\CameraControl.WinUI.Tests.csproj'
$runRoot = Join-Path $repoRoot ('.omo\evidence\winui3-modernisierung\T8\release-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$artifacts = Join-Path $repoRoot '.worktools\T8-release-tests-artifacts'
$null = New-Item -ItemType Directory -Path $runRoot -Force
$environmentNames = @('CAMERA_RELEASE_EXE_A', 'CAMERA_RELEASE_EXE_B', 'CAMERA_RELEASE_EVIDENCE_DIRECTORY')
$savedEnvironment = @{}
foreach ($name in $environmentNames) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }

try {
    if ($Scenario -in @('All', 'Smoke')) {
        throw 'Der vollständige T9-/T10-Release-Nachweis benötigt zusätzlich Windows-Suche, sauberes Windows und die vollständigen Bedienabläufe. Diese Szenarien sind noch nicht freigegeben.'
    }
    if ($Scenario -eq 'LocalUpdate') {
        if ([string]::IsNullOrWhiteSpace($ExeA) -ne [string]::IsNullOrWhiteSpace($ExeB)) {
            throw 'Entweder beide geprüften QA-EXEs angeben oder beide lokal veröffentlichen lassen.'
        }
        if ([string]::IsNullOrWhiteSpace($ExeA)) {
            $fixtureRoot = Join-Path $repoRoot '.worktools\release-fixtures'
            foreach ($fixture in @(@{ Name = 'A'; Version = '1.0.0.1' }, @{ Name = 'B'; Version = '1.0.0.2' })) {
                & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Publish-App.ps1') -Version $fixture.Version
                if ($LASTEXITCODE -ne 0) { throw "QA-Publish $($fixture.Name) fehlgeschlagen." }
                $destination = Join-Path $fixtureRoot $fixture.Name
                $null = New-Item -ItemType Directory -Path $destination -Force
                $sourceExe = Join-Path $repoRoot 'artifacts\win-x64\CameraControl.WinUI.exe'
                $targetExe = Join-Path $destination 'CameraControl.WinUI.exe'
                $sourceStream = [IO.FileStream]::new($sourceExe, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
                $targetStream = [IO.FileStream]::new($targetExe, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
                try { $sourceStream.CopyTo($targetStream, 1048576); $targetStream.Flush($true) }
                finally { $targetStream.Dispose(); $sourceStream.Dispose() }
                if ((Get-FileHash -LiteralPath $sourceExe -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $targetExe -Algorithm SHA256).Hash -or
                    [Diagnostics.FileVersionInfo]::GetVersionInfo($targetExe).FileVersion -ne $fixture.Version) {
                    throw 'Die QA-Hostkopie hat eine falsche Prüfsumme oder FileVersion.'
                }
            }
            $ExeA = Join-Path $fixtureRoot 'A\CameraControl.WinUI.exe'
            $ExeB = Join-Path $fixtureRoot 'B\CameraControl.WinUI.exe'
        }
        $ExeA = [IO.Path]::GetFullPath($ExeA)
        $ExeB = [IO.Path]::GetFullPath($ExeB)
        if (-not (Test-Path -LiteralPath $ExeA -PathType Leaf) -or -not (Test-Path -LiteralPath $ExeB -PathType Leaf)) {
            throw 'Eine der beiden QA-EXEs fehlt.'
        }
        [Environment]::SetEnvironmentVariable('CAMERA_RELEASE_EXE_A', $ExeA, 'Process')
        [Environment]::SetEnvironmentVariable('CAMERA_RELEASE_EXE_B', $ExeB, 'Process')
    }
    [Environment]::SetEnvironmentVariable('CAMERA_RELEASE_EVIDENCE_DIRECTORY', $runRoot, 'Process')
    [pscustomobject]@{ Scenario = $Scenario; ExeA = $ExeA; ExeB = $ExeB;
        Policy = 'Eigener Temp-Programmbereich, eigene Temp-Daten, echte QA-LNK, echte Prozesse, kein Kill, keine Produktregistrierung';
        SearchEvidence = $false; CleanWindowsEvidence = $false } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'environment.json')
    $properties = @('-p:UseArtifactsOutput=true', "-p:ArtifactsPath=$artifacts")
    & dotnet restore $project --locked-mode @properties 2>&1 | Tee-Object -FilePath (Join-Path $runRoot 'restore.log') | Write-Host
    if ($LASTEXITCODE -ne 0) { throw "Test-Restore fehlgeschlagen: $LASTEXITCODE" }
    $sources = @(
        Get-ChildItem -LiteralPath (Join-Path $repoRoot 'CameraControl.WinUI\Startup') -File
        Get-ChildItem -LiteralPath (Join-Path $repoRoot 'CameraControl.WinUI\Deployment') -File
        Get-Item -LiteralPath (Join-Path $repoRoot 'CameraControl.WinUI\App.xaml.cs')
        Get-Item -LiteralPath (Join-Path $repoRoot 'CameraControl.WinUI\Program.cs')
        Get-Item -LiteralPath (Join-Path $repoRoot 'CameraControl.WinUI.Tests\ReleaseDeploymentTests.cs')
        Get-Item -LiteralPath $PSCommandPath
    )
    $sources | ForEach-Object { [pscustomobject]@{ Path = [IO.Path]::GetRelativePath($repoRoot, $_.FullName);
        SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'source-hashes.json')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'CameraControl.WinUI.Tests\ReleaseDeploymentTests.cs') -Destination (Join-Path $runRoot 'ReleaseDeploymentTests.cs.txt')
    $filter = 'TestCategory=DeploymentStartupOptions|TestCategory=DeploymentAdapters'
    if ($Scenario -eq 'LocalUpdate') { $filter += '|TestCategory=ReleaseDeployment' }
    & dotnet test $project --configuration Release --no-restore --filter $filter --logger 'trx;LogFileName=results.trx' --results-directory $runRoot @properties 2>&1 |
        Tee-Object -FilePath (Join-Path $runRoot 'test.log') | Write-Host
    $testExit = $LASTEXITCODE
    $trxPath = Join-Path $runRoot 'results.trx'
    if (-not (Test-Path -LiteralPath $trxPath)) { throw 'TRX fehlt. Kein gültiger Testnachweis.' }
    [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
    if ($Scenario -eq 'LocalUpdate') {
        $requiredCases = @(
            'NewDownloadedExeSwitchesToBAndTheRealLinkWorksWithoutDownloads',
            'UnreachableLoopbackCameraDoesNotPreventTheStartupCommit',
            'TwoDownloadedBStartsLeaveOneCommittedWriterAndOneWindow',
            'FailedOrCancelledCandidatePreservesAAndUserData("fail-before-ready")',
            'FailedOrCancelledCandidatePreservesAAndUserData("cancel-after-ready")',
            'RefusedGracefulExitKeepsAAndDoesNotStartB',
            'LockedShellLinkPreservesAAndTheNextStartRecoversTheJournal'
        )
        $observed = @($trx.TestRun.Results.UnitTestResult | ForEach-Object { [string]$_.testName })
        $missing = @($requiredCases | Where-Object { $_ -notin $observed })
        if ($missing.Count -ne 0) { throw ('Pflicht-Releasefälle fehlen im Testlauf: ' + ($missing -join ', ')) }
    }
    $counts = $trx.TestRun.ResultSummary.Counters
    $total = [int]$counts.total
    $executed = [int]$counts.executed
    $passed = [int]$counts.passed
    $failed = [int]$counts.failed
    [pscustomobject]@{ Scenario = $Scenario; Total = $total; Executed = $executed; Passed = $passed; Failed = $failed;
        ExitCode = $testExit; Trx = $trxPath; Evidence = $runRoot; SearchEvidence = $false; CleanWindowsEvidence = $false } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'summary.json')
    if ($testExit -ne 0 -or $total -eq 0 -or $executed -ne $total -or $passed -ne $total -or $failed -ne 0) {
        throw "Release-Test nicht bestanden: Gesamt=$total, Ausgeführt=$executed, Bestanden=$passed, Fehler=$failed, Exitcode=$testExit"
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
    foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
}
