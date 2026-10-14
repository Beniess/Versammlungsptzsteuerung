[CmdletBinding()]
param(
    [ValidateSet('Protocol', 'Legacy', 'Persistence', 'Controls', 'Presets', 'Deployment', 'All')]
    [string]$Suite = 'All',
    [string]$EvidenceDirectory,
    [string]$ArtifactsDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$isAppCoreSuite = $Suite -in @('Persistence', 'Controls', 'Deployment', 'Presets')
$projectName = if ($isAppCoreSuite) { 'CameraControl.AppCore.Tests' } else { 'CameraControl.Visca.Tests' }
$project = Join-Path $repoRoot "$projectName\$projectName.csproj"
$taskName = switch ($Suite) { 'Protocol' { 'T1' } 'Persistence' { 'T4' } 'Controls' { 'T6-core' } 'Presets' { 'T7-core' } 'Deployment' { 'T8-core' } 'All' { 'T9' } default { 'T2' } }
$resultRoot = Join-Path $repoRoot ".omo\evidence\winui3-modernisierung\$taskName"
$runRoot = Join-Path $resultRoot ('run-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
if ($EvidenceDirectory) {
    $runRoot = [IO.Path]::GetFullPath($EvidenceDirectory)
    $allowedEvidence = [IO.Path]::GetFullPath((Join-Path $repoRoot '.omo\evidence\winui3-modernisierung')) + [IO.Path]::DirectorySeparatorChar
    if (-not $runRoot.StartsWith($allowedEvidence, [StringComparison]::OrdinalIgnoreCase)) { throw 'Der Belegpfad liegt außerhalb des eigenen Workspace-Bereichs.' }
}
if ($ArtifactsDirectory) {
    $ArtifactsDirectory = [IO.Path]::GetFullPath($ArtifactsDirectory)
    $allowedArtifacts = [IO.Path]::GetFullPath((Join-Path $repoRoot '.worktools')) + [IO.Path]::DirectorySeparatorChar
    if (-not $ArtifactsDirectory.StartsWith($allowedArtifacts, [StringComparison]::OrdinalIgnoreCase)) { throw 'Der Buildpfad liegt außerhalb des eigenen Workspace-Bereichs.' }
}
$null = New-Item -ItemType Directory -Path $runRoot -Force
$summaries = @()

try {
    $dotnet = Get-Command dotnet -ErrorAction Stop
    if ($Suite -eq 'All') {
        $components = @('Legacy', 'Persistence', 'Controls', 'Presets', 'Deployment')
        $artifactsRoot = if ($ArtifactsDirectory) { $ArtifactsDirectory } else { Join-Path $repoRoot ('.worktools\test-artifacts\All\' + (Split-Path -Leaf $runRoot)) }
        $scopeParents = @('CameraControl.AppCore.Tests', 'CameraControl.PresetWorkflow.Tests', 'CameraControl.Deployment.Tests') |
            ForEach-Object { Join-Path ([IO.Path]::GetTempPath()) $_ }
        $beforeScopes = @(foreach ($parent in $scopeParents) { Get-ChildItem -LiteralPath $parent -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName })
        $fixtureHashes = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'tests\fixtures\visca') -File | ForEach-Object {
            [pscustomobject]@{ Path=[IO.Path]::GetRelativePath($repoRoot, $_.FullName); SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
        })
        $fixtureHashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'fixture-hashes.json')
        [pscustomobject]@{ Suite='All'; Components=$components; ArtifactsRoot=$artifactsRoot;
            FrameworkPaths=@{ 'Legacy/net481'='Visca netstandard2.0'; 'Legacy/net10.0'='Visca net10.0'; 'AppCore/net10.0'='AppCore and Visca net10.0' };
            EndpointPolicy='127.0.0.1, dynamischer Port, keine echte Kamera';
            DesktopPolicy='Keine WinUI-App starten, keine Desktop-Eingaben; WPF-Referenzfenster bleiben unsichtbar' } |
            ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runRoot 'environment.json')
        $failures = @()
        foreach ($component in $components) {
            $componentRoot = Join-Path $runRoot $component
            & pwsh -NoProfile -File $PSCommandPath -Suite $component -EvidenceDirectory $componentRoot -ArtifactsDirectory $artifactsRoot 2>&1 |
                Tee-Object -FilePath (Join-Path $runRoot ($component + '.log')) | Write-Host
            if ($LASTEXITCODE -ne 0) { $failures += "Suite ${component}: Exitcode $LASTEXITCODE" }
            $summaryPath = Join-Path $componentRoot 'summary.json'
            if (Test-Path -LiteralPath $summaryPath) { $summaries += @(Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json) }
            else { $failures += "Suite ${component}: Summary fehlt" }
            $expectedFrameworks = if ($component -eq 'Legacy') { @('net481', 'net10.0') } else { @('net10.0') }
            foreach ($expected in $expectedFrameworks) {
                if (@($summaries | Where-Object { $_.Suite -eq $component -and $_.Framework -eq $expected }).Count -ne 1) {
                    $failures += "Pflichtlauf fehlt oder ist nicht eindeutig: $component / $expected"
                }
            }
        }
        $reportedScopes = @()
        foreach ($result in $summaries) {
            [xml]$run = Get-Content -LiteralPath $result.Trx -Raw
            $matched = [regex]::Matches($run.OuterXml, 'Removed isolated (?:data|preset|deployment) directory: ([^\r\n<]+)')
            foreach ($match in $matched) {
                $path = [System.Net.WebUtility]::HtmlDecode($match.Groups[1].Value).Trim()
                $reportedScopes += [pscustomobject]@{ Path=$path; Exists=Test-Path -LiteralPath $path }
            }
        }
        $afterScopes = @(foreach ($parent in $scopeParents) { Get-ChildItem -LiteralPath $parent -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName })
        $newScopes = @($afterScopes | Where-Object { $_ -notin $beforeScopes })
        [pscustomobject]@{ Before=$beforeScopes; After=$afterScopes; NewRemaining=$newScopes; Reported=$reportedScopes } |
            ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runRoot 'scope-cleanup.json')
        if ($newScopes.Count -ne 0 -or @($reportedScopes | Where-Object Exists).Count -ne 0) { $failures += 'Ein neuer oder als entfernt gemeldeter Testscope ist noch vorhanden.' }
        $fixtureCopies = @(Get-ChildItem -LiteralPath (Join-Path $artifactsRoot 'bin') -Filter '*.tsv' -File -Recurse | ForEach-Object {
            $source = Join-Path $repoRoot ('tests\fixtures\visca\' + $_.Name)
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            $copyMatchesSource = (Test-Path -LiteralPath $source) -and $hash -eq (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
            [pscustomobject]@{ Path=[IO.Path]::GetRelativePath($artifactsRoot, $_.FullName); SHA256=$hash; MatchesSource=$copyMatchesSource }
        })
        $fixtureCopies | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'fixture-copy-hashes.json')
        if ($fixtureCopies.Count -lt 3 -or @($fixtureCopies | Where-Object { -not $_.MatchesSource }).Count -ne 0) { $failures += 'Fixture-Kopien fehlen oder stimmen nicht mit der Quelle überein.' }
        foreach ($fixture in $fixtureHashes) {
            if ((Get-FileHash -LiteralPath (Join-Path $repoRoot $fixture.Path) -Algorithm SHA256).Hash -ne $fixture.SHA256) { $failures += "Fixture wurde während des Laufs geändert: $($fixture.Path)" }
        }
        $summaries | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $runRoot 'summary.json')
        [pscustomobject]@{ Suite='All'; FrameworkRuns=$summaries.Count; Total=($summaries | Measure-Object Total -Sum).Sum;
            Executed=($summaries | Measure-Object Executed -Sum).Sum; Passed=($summaries | Measure-Object Passed -Sum).Sum;
            Failed=($summaries | Measure-Object Failed -Sum).Sum; Failures=$failures; Evidence=$runRoot } |
            ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runRoot 'all-summary.json')
        if ($failures.Count -ne 0) { throw ($failures -join [Environment]::NewLine) }
        $summaries | Format-Table Framework, Suite, Total, Executed, Passed, Failed, ExitCode
        Write-Host "Belege: $runRoot"
        exit 0
    }
    if (-not (Test-Path -LiteralPath $project)) { throw "Testprojekt fehlt: $project" }
    $sdkVersion = & $dotnet.Source --version
    if ($LASTEXITCODE -ne 0) { throw 'SDK-Version kann nicht gelesen werden.' }
    [pscustomobject]@{ Suite = $Suite; Configuration = 'Release'; SdkVersion = $sdkVersion;
        PowerShellVersion = $PSVersionTable.PSVersion.ToString(); StartedUtc = (Get-Date).ToUniversalTime().ToString('o');
        EndpointPolicy = '127.0.0.1, dynamischer Port, keine echte Kamera';
        FrameworkPaths = $(if ($isAppCoreSuite) { @{ 'net10.0' = 'AppCore net10.0; Visca net10.0' } } else { @{ net481 = 'netstandard2.0'; 'net10.0' = 'net10.0' } }) } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $runRoot 'environment.json')
    $sourceDirectories = if ($isAppCoreSuite) { @('CameraControl.AppCore','CameraControl.AppCore.Tests','CameraControl.Visca') } else { @('CameraControl.Visca','CameraControl.Visca.Tests') }
    $sourceFiles = @(foreach ($sourceDirectory in $sourceDirectories) {
        Get-ChildItem -LiteralPath (Join-Path $repoRoot $sourceDirectory) -File -Recurse |
            Where-Object { $_.Extension -in '.cs', '.csproj' -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
    })
    $sourceFiles += Get-Item -LiteralPath $PSCommandPath
    $sourceFiles += Get-ChildItem -LiteralPath (Join-Path $repoRoot 'tests\fixtures\visca') -File
    if ($isAppCoreSuite) {
        $sourceFiles += Get-Item -LiteralPath (Join-Path $repoRoot 'CameraControl.Visca.Tests\Fixtures.cs'), (Join-Path $repoRoot 'CameraControl.Visca.Tests\LoopbackServer.cs')
    }
    if ($Suite -eq 'Legacy') {
        $sourceFiles += Get-ChildItem -LiteralPath (Join-Path $repoRoot 'CameraControl.Visca.Demo') -File |
            Where-Object { $_.Extension -in '.cs', '.csproj', '.xaml', '.config' }
    }
    $manifest = foreach ($source in $sourceFiles) {
        $relative = [IO.Path]::GetRelativePath($repoRoot, $source.FullName)
        $snapshot = Join-Path (Join-Path $runRoot 'sources') $relative
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $snapshot) -Force
        Copy-Item -LiteralPath $source.FullName -Destination $snapshot
        [pscustomobject]@{ Path = $relative; SHA256 = (Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash }
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'source-hashes.json')
    $filter = switch ($Suite) { 'Protocol' { 'TestCategory=Protocol' } 'Persistence' { 'TestCategory=Persistence' } 'Controls' { 'TestCategory=Controls' } 'Presets' { 'TestCategory=Presets' } 'Deployment' { 'TestCategory=Deployment' } default { 'TestCategory=Protocol|TestCategory=Transport|TestCategory=Legacy' } }
    $buildProperties = @()
    if ($isAppCoreSuite -or $ArtifactsDirectory) {
        $artifactsRoot = if ($ArtifactsDirectory) { $ArtifactsDirectory } else { Join-Path $repoRoot ".worktools\test-artifacts\$Suite" }
        $buildProperties = @('-p:UseArtifactsOutput=true', "-p:ArtifactsPath=$artifactsRoot")
    }
    $restoreLog = Join-Path $runRoot 'restore.log'
    & $dotnet.Source restore $project @buildProperties 2>&1 | Tee-Object -FilePath $restoreLog | Write-Host
    $restoreExit = $LASTEXITCODE
    if ($restoreExit -ne 0) { throw "Restore fehlgeschlagen: Exitcode $restoreExit" }
    $assetsPath = Join-Path (Split-Path -Parent $project) 'obj\project.assets.json'
    if ($isAppCoreSuite -or $ArtifactsDirectory) { $assetsPath = Join-Path $artifactsRoot "obj\$projectName\project.assets.json" }
    elseif ($env:UseArtifactsOutput -eq 'true' -and -not [string]::IsNullOrWhiteSpace($env:ArtifactsPath)) {
        $assetsPath = Join-Path $env:ArtifactsPath "obj\$projectName\project.assets.json"
    }
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    $assets.libraries.PSObject.Properties.Name | Sort-Object |
        Set-Content -LiteralPath (Join-Path $runRoot 'resolved-dependencies.txt')
    $frameworks = if ($isAppCoreSuite) { @('net10.0') } else { @('net481', 'net10.0') }
    foreach ($framework in $frameworks) {
        $frameworkRoot = Join-Path $runRoot $framework
        $null = New-Item -ItemType Directory -Path $frameworkRoot
        $logPath = Join-Path $frameworkRoot 'command.log'
        $arguments = @('test', $project, '--framework', $framework, '--configuration', 'Release',
            '--filter', $filter, '--logger', 'trx;LogFileName=results.trx', '--results-directory', $frameworkRoot,
            '--blame-hang-timeout', '30s', '--no-restore') + $buildProperties
        "dotnet $($arguments -join ' ')" | Set-Content -LiteralPath $logPath
        & $dotnet.Source @arguments 2>&1 | Tee-Object -FilePath $logPath -Append | Write-Host
        $testExit = $LASTEXITCODE
        $trxPath = Join-Path $frameworkRoot 'results.trx'
        if (-not (Test-Path -LiteralPath $trxPath)) { throw "TRX fehlt: $framework" }
        [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
        $counters = $trx.TestRun.ResultSummary.Counters
        $total = [int]$counters.total
        $executed = [int]$counters.executed
        $passed = [int]$counters.passed
        $failed = [int]$counters.failed
        $results = @($trx.TestRun.Results.UnitTestResult)
        $categories = if ($isAppCoreSuite) { @($Suite) } else { @('Protocol') }
        if ($Suite -eq 'Legacy') { $categories += 'Transport'; if ($framework -eq 'net481') { $categories += 'Legacy' } }
        foreach ($category in $categories) {
            $requiredClass = switch ($category) {
                'Protocol' { 'CommandReferenceTests' }
                'Transport' { 'TransportReferenceTests' }
                'Legacy' { 'LegacyWpfTests' }
                'Persistence' { 'PersistenceTests' }
                'Controls' { 'ControlsTests' }
                'Presets' { 'PresetWorkflowTests' }
                'Deployment' { 'DeploymentTests' }
            }
            if (@($results | Where-Object { $_.testName -match $requiredClass }).Count -eq 0) {
                $definitions = @($trx.TestRun.TestDefinitions.UnitTest | Where-Object { $_.TestMethod.className -match $requiredClass })
                if ($definitions.Count -eq 0) { throw "Pflichtnachweis fehlt: $requiredClass / $framework" }
            }
        }
        $summaries += [pscustomobject]@{ Framework = $framework; Suite = $Suite; Total = $total; Executed = $executed;
            Passed = $passed; Failed = $failed; ExitCode = $testExit; Trx = $trxPath }
        if ($testExit -ne 0 -or $total -eq 0 -or $executed -ne $total -or $passed -ne $total -or $failed -ne 0) {
            throw "Testlauf nicht bestanden: $framework, Gesamt=$total, Ausgeführt=$executed, Bestanden=$passed, Fehler=$failed, Exitcode=$testExit"
        }
    }
    $summaries | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $runRoot 'summary.json')
    $summaries | Format-Table Framework, Suite, Total, Executed, Passed, Failed, ExitCode
    Write-Host "Belege: $runRoot"
    exit 0
}
catch {
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $runRoot 'failure.txt')
    if ($summaries.Count -gt 0) { $summaries | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $runRoot 'summary.json') }
    Write-Error $_ -ErrorAction Continue
    exit 1
}
