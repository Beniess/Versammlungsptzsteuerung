$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:USERNAME -ine 'WDAGUtilityAccount' -or $PSScriptRoot -ine 'C:\Payload') { throw 'Dieser Test benötigt den eigenen Sandbox-Gast.' }
$system = Get-CimInstance Win32_ComputerSystem
if ($system.Model -ne 'Virtual Machine') { throw 'Der bestätigte Gast fehlt.' }
$exitCode = 1
$errorText = $null
try {
    $entries = Get-Content -LiteralPath 'C:\Payload\payload-manifest.json' -Raw | ConvertFrom-Json
    foreach ($entry in $entries) {
        $path = [IO.Path]::GetFullPath((Join-Path 'C:\Payload' $entry.Path))
        if (-not $path.StartsWith('C:\Payload\',[StringComparison]::OrdinalIgnoreCase) -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.SHA256) { throw 'Der eigene Payload ist nicht identisch.' }
    }
    $readOnly = $false
    try { [IO.File]::WriteAllText('C:\Payload\read-only-check.tmp','This write must fail.') }
    catch [UnauthorizedAccessException] { $readOnly = $true }
    catch [IO.IOException] { if (($_.Exception.HResult -band 65535) -notin @(5,19)) { throw }; $readOnly = $true }
    if (-not $readOnly) { [IO.File]::Delete('C:\Payload\read-only-check.tmp'); throw 'Der Payload-Mount ist schreibbar.' }
    [pscustomobject]@{User=$env:USERNAME;ComputerModel=$system.Model;PayloadReadOnly=$readOnly;PayloadHashesVerified=$true;HostInputs=$false;RequiredCases=1} |
        ConvertTo-Json | Set-Content -LiteralPath 'C:\Evidence\guest-environment.json' -Encoding utf8
    foreach ($item in @(Get-ChildItem Env: | Where-Object { $_.Name -like 'CAMERA_CONTROL_QA_*' -or $_.Name -like 'CAMERA_UI_*' -or $_.Name -eq 'DOTNET_BUNDLE_EXTRACT_BASE_DIR' })) {
        Remove-Item -LiteralPath ('Env:\' + $item.Name)
    }
    $runner = Start-Process -FilePath 'C:\Payload\Runner\NativeAdjustmentRunner.exe' -ArgumentList @('C:\Payload\App\CameraControl.WinUI.exe','C:\Evidence\Adjustment','C:\Payload\Repo') -WindowStyle Hidden -PassThru -RedirectStandardOutput 'C:\Evidence\runner-stdout.log' -RedirectStandardError 'C:\Evidence\runner-stderr.log'
    try { $null=$runner.Handle; $runner.WaitForExit(); $exitCode=$runner.ExitCode } finally { $runner.Dispose() }
    if ($null -eq $exitCode -or $exitCode -ne 0) { throw "Der einzelne Nachjustierfall meldet Exitcode $exitCode." }
}
catch { $errorText=$_.Exception.ToString(); $exitCode=1; Write-Error -Message $errorText -ErrorAction Continue }
finally {
    $remaining = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -ieq 'C:\Payload\App\CameraControl.WinUI.exe' })
    $closeResults = @(foreach ($item in $remaining) {
        $process = Get-Process -Id $item.ProcessId -ErrorAction SilentlyContinue
        if ($process) {
            try {
                $null=$process.Handle
                $accepted=$process.CloseMainWindow()
                [pscustomobject]@{ProcessId=$item.ProcessId;CloseAccepted=$accepted;Exited=$process.WaitForExit(15000)}
            } finally { $process.Dispose() }
        }
    })
    $after = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -ieq 'C:\Payload\App\CameraControl.WinUI.exe' })
    if ($after.Count -ne 0) { $exitCode=1; $errorText='Eigene App-Prozesse sind nach dem geordneten Schließen noch aktiv.' }
    [pscustomobject]@{RemainingOwnApps=@($after | Select-Object ProcessId,ExecutablePath);CloseResults=$closeResults;KillUsed=$false} |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath 'C:\Evidence\guest-cleanup.json' -Encoding utf8
    [pscustomobject]@{ExitCode=$exitCode;Error=$errorText;CompletedUtc=[DateTime]::UtcNow.ToString('o');ShutdownRequested=$true} |
        ConvertTo-Json -Depth 3 | Set-Content -LiteralPath 'C:\Evidence\completion.json' -Encoding utf8
    & shutdown.exe /s /t 0
}
