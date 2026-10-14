$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:USERNAME -ine 'WDAGUtilityAccount') { throw 'Diese Probe darf nur im Windows-Sandbox-Gast laufen.' }
$system = Get-CimInstance Win32_ComputerSystem
if ($system.Model -ne 'Virtual Machine') { throw 'Der Computer ist kein Sandbox-Gast.' }
if ($PSScriptRoot -ine 'C:\Payload' -or -not (Test-Path -LiteralPath 'C:\Evidence')) { throw 'Die beiden eigenen Sandbox-Mounts fehlen.' }
$exitCode = 1
$errorText = $null
try {
    $os = Get-CimInstance Win32_OperatingSystem
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    $vlc = Get-Command vlc -ErrorAction SilentlyContinue
    $runtimePaths = @('C:\Program Files\dotnet\shared\Microsoft.NETCore.App', 'C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App',
        'C:\Program Files (x86)\dotnet\shared\Microsoft.NETCore.App', 'C:\Program Files (x86)\dotnet\shared\Microsoft.WindowsDesktop.App')
    $runtimes = @(foreach ($path in $runtimePaths) { if (Test-Path -LiteralPath $path) { Get-ChildItem -LiteralPath $path -Directory | Select-Object -ExpandProperty FullName } })
    $vlcFiles = @(@('C:\Program Files\VideoLAN\VLC\vlc.exe', 'C:\Program Files (x86)\VideoLAN\VLC\vlc.exe') | Where-Object { Test-Path -LiteralPath $_ })
    $environment = [ordered]@{ User=$env:USERNAME; ComputerModel=$system.Model; WindowsVersion=$os.Version; Build=$os.BuildNumber;
        IsWindows11=([int]$os.BuildNumber -ge 22000); DotNetCommand=$(if ($dotnet) { $dotnet.Source } else { $null }); ModernDotNetRuntimes=$runtimes;
        VlcCommand=$(if ($vlc) { $vlc.Source } else { $null }); VlcInstalledFiles=$vlcFiles;
        WinAppRuntimePackages=@(Get-AppxPackage -Name 'Microsoft.WindowsAppRuntime*' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty PackageFullName);
        CleanModernRuntimeEnvironment=($null -eq $dotnet -and $runtimes.Count -eq 0 -and $null -eq $vlc -and $vlcFiles.Count -eq 0) }
    $environment | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath 'C:\Evidence\guest-environment.json' -Encoding utf8
    if (-not $environment.IsWindows11 -or -not $environment.CleanModernRuntimeEnvironment) { throw 'Die saubere Windows-11-Laufzeitumgebung ist nicht gegeben.' }
    $payloadEntries = Get-Content -LiteralPath 'C:\Payload\payload-manifest.json' -Raw | ConvertFrom-Json
    foreach ($entry in $payloadEntries) {
        $path = [IO.Path]::GetFullPath((Join-Path 'C:\Payload' $entry.Path))
        if (-not $path.StartsWith('C:\Payload\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Ein Manifestpfad verlässt den Payload.' }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.SHA256 -or (Get-Item -LiteralPath $path).Length -ne $entry.Length) {
            throw "Ungültige Payload-Kopie: $($entry.Path)"
        }
    }
    $writeDenied = $false
    try { [IO.File]::WriteAllText('C:\Payload\read-only-check.tmp', 'This write must fail.') }
    catch [UnauthorizedAccessException] { $writeDenied = $true }
    catch [IO.IOException] {
        if (($_.Exception.HResult -band 65535) -notin @(5, 19)) { throw }
        $writeDenied = $true
    }
    if (-not $writeDenied) { [IO.File]::Delete('C:\Payload\read-only-check.tmp'); throw 'Der Payload-Mount ist schreibbar.' }
    [pscustomobject]@{ ReadOnlyWriteDenied=$writeDenied; PayloadHashesVerified=$true } |
        ConvertTo-Json | Set-Content -LiteralPath 'C:\Evidence\payload-verified.json'
    foreach ($item in @(Get-ChildItem Env: | Where-Object { $_.Name -like 'CAMERA_CONTROL_QA_*' -or $_.Name -like 'CAMERA_UI_*' -or $_.Name -eq 'DOTNET_BUNDLE_EXTRACT_BASE_DIR' })) {
        Remove-Item -LiteralPath ('Env:\' + $item.Name)
    }
    $runner = Start-Process -FilePath 'C:\Payload\Runner\NativeRunner.exe' -ArgumentList @('C:\Payload\App\CameraControl.WinUI.exe', 'C:\Evidence\Probe', 'Observe', 'CleanWindows') -WindowStyle Hidden -PassThru -RedirectStandardOutput 'C:\Evidence\runner-stdout.log' -RedirectStandardError 'C:\Evidence\runner-stderr.log'
    try { $null = $runner.Handle; $runner.WaitForExit(); $exitCode = $runner.ExitCode } finally { $runner.Dispose() }
    if ($exitCode -ne 0) { throw "NativeRunner meldet Exitcode $exitCode." }
}
catch { $errorText = $_.Exception.ToString(); $exitCode = 1; Write-Error -Message $errorText -ErrorAction Continue }
finally {
    [pscustomobject]@{ ExitCode=$exitCode; Error=$errorText; CompletedUtc=[DateTime]::UtcNow.ToString('o'); ShutdownRequested=$true } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath 'C:\Evidence\completion.json' -Encoding utf8
    & shutdown.exe /s /t 0
}
