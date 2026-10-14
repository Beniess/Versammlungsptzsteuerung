param([ValidateSet('ReleaseDeployment','ReleasePipeRegression')][string]$Mode = 'ReleaseDeployment')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:USERNAME -ine 'WDAGUtilityAccount') { throw 'Diese Release-Prüfung darf nur im eigenen Windows-Sandbox-Gast laufen.' }
$system = Get-CimInstance Win32_ComputerSystem
if ($system.Model -ne 'Virtual Machine' -or $PSScriptRoot -ine 'C:\Payload') { throw 'Die festen Sandbox-Pfade oder der Gastnachweis fehlen.' }
if (-not (Test-Path -LiteralPath 'C:\Evidence')) { throw 'Der eigene Evidence-Mount fehlt.' }
$exitCode = 1
$errorText = $null
try {
    $readOnly = $false
    try { [IO.File]::WriteAllText('C:\Payload\release-read-only-check.tmp', 'This write must fail.') }
    catch [UnauthorizedAccessException] { $readOnly = $true }
    catch [IO.IOException] { if (($_.Exception.HResult -band 65535) -notin @(5, 19)) { throw }; $readOnly = $true }
    if (-not $readOnly) { [IO.File]::Delete('C:\Payload\release-read-only-check.tmp'); throw 'Der Payload-Mount ist nicht schreibgeschützt.' }
    $files = @(foreach ($item in @(@{ Name='A'; Version='1.0.0.1' }, @{ Name='B'; Version='1.0.0.2' })) {
        $path = 'C:\Payload\' + $item.Name + '\CameraControl.WinUI.exe'
        $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($path).FileVersion
        if ($version -ne $item.Version) { throw "Falsche QA-Dateiversion: $path" }
        [pscustomobject]@{ Path=$path; SHA256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash; FileVersion=$version; Length=(Get-Item -LiteralPath $path).Length }
    })
    [pscustomobject]@{ User=$env:USERNAME; ComputerModel=$system.Model; PayloadReadOnly=$readOnly; Files=$files;
        TestScopeParent=(Join-Path ([IO.Path]::GetTempPath()) 'CameraControl.Release.Tests'); ProductRegistration=$false; SearchEvidence=$false } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath 'C:\Evidence\release-environment.json' -Encoding utf8
    foreach ($item in @(Get-ChildItem Env: | Where-Object { $_.Name -like 'CAMERA_CONTROL_QA_*' -or $_.Name -like 'CAMERA_RELEASE_*' -or $_.Name -eq 'DOTNET_BUNDLE_EXTRACT_BASE_DIR' })) {
        Remove-Item -LiteralPath ('Env:\' + $item.Name)
    }
    $runner = Start-Process -FilePath 'C:\Payload\Runner\NativeRunner.exe' -ArgumentList @('C:\Payload\A\CameraControl.WinUI.exe', 'C:\Evidence\Release', 'Observe', $Mode) -WindowStyle Hidden -PassThru -RedirectStandardOutput 'C:\Evidence\release-stdout.log' -RedirectStandardError 'C:\Evidence\release-stderr.log'
    try { $null = $runner.Handle; $runner.WaitForExit(); $exitCode = $runner.ExitCode } finally { $runner.Dispose() }
    if ($null -eq $exitCode -or $exitCode -ne 0) { throw "Die ausgewählten Release-Fälle melden Exitcode $exitCode." }
}
catch { $errorText = $_.Exception.ToString(); $exitCode = 1; Write-Error -Message $errorText -ErrorAction Continue }
finally {
    try {
        $allowed = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'CameraControl.Release.Tests')) + '\'
        $roots = @(Get-ChildItem -LiteralPath 'C:\Evidence\Release' -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            $caseInput = Join-Path $_.FullName 'inputs.json'
            if (Test-Path -LiteralPath $caseInput) {
                $caseRoot = [IO.Path]::GetFullPath((Get-Content -LiteralPath $caseInput -Raw | ConvertFrom-Json).Root)
                if (-not $caseRoot.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $caseRoot) -notmatch '^[0-9a-f]{32}$') { throw 'Fremder Testroot im Abschlussbeleg.' }
                $caseRoot
            }
        })
        $owned = @(Get-CimInstance Win32_Process | Where-Object { $path=$_.ExecutablePath; $path -and @($roots | Where-Object { $path.StartsWith($_ + '\', [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0 })
        $closed = @(foreach ($item in $owned) {
            $process = Get-Process -Id $item.ProcessId -ErrorAction SilentlyContinue
            if (-not $process) { continue }
            try {
                if ($process.Path -ne $item.ExecutablePath) { throw 'Die eigene Prozessidentitaet hat sich geaendert.' }
                $accepted = $process.CloseMainWindow()
                [pscustomobject]@{ ProcessId=$item.ProcessId; Path=$item.ExecutablePath; CloseAccepted=$accepted; Exited=$process.WaitForExit(10000) }
            } finally { $process.Dispose() }
        })
        [pscustomobject]@{ RemainingAfterRunner=@($owned | Select-Object ProcessId,ExecutablePath); CloseResults=$closed; KillUsed=$false } |
            ConvertTo-Json -Depth 5 | Set-Content -LiteralPath 'C:\Evidence\guest-process-cleanup.json' -Encoding utf8
        if ($owned.Count -ne 0 -and $exitCode -eq 0) { $exitCode=1; $errorText='Nach dem erfolgreichen Runner blieben eigene Testprozesse offen.' }
    }
    catch { $exitCode=1; $errorText=($_.Exception.ToString()); $errorText | Set-Content -LiteralPath 'C:\Evidence\guest-cleanup-error.txt' }
    [pscustomobject]@{ ExitCode=$exitCode; Error=$errorText; CompletedUtc=[DateTime]::UtcNow.ToString('o'); ShutdownRequested=$true } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath 'C:\Evidence\release-completion.json' -Encoding utf8
    & shutdown.exe /s /t 0
}
