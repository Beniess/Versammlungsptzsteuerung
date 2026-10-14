[CmdletBinding()]
param(
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',
    [string]$Version,
    [switch]$CreateMissingNativeFixture
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'CameraControl.WinUI\CameraControl.WinUI.csproj'
$config = Join-Path $repoRoot 'CameraControl.WinUI\NuGet.Config'
$output = Join-Path $repoRoot ("artifacts\$Runtime")
if ($CreateMissingNativeFixture) { $output = Join-Path $repoRoot '.worktools\negative-native' }
$runRoot = Join-Path $repoRoot ('.omo\evidence\winui3-modernisierung\T3\publish-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$staging = Join-Path $repoRoot ('.worktools\publish\' + (Split-Path -Leaf $runRoot))
$null = New-Item -ItemType Directory -Path $runRoot -Force
$null = New-Item -ItemType Directory -Path $output -Force
$null = New-Item -ItemType Directory -Path $staging -Force
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

if ([string]::IsNullOrWhiteSpace($Version)) { $Version = [DateTime]::UtcNow.ToString('yyyy.MMdd.HHmm.ss', [Globalization.CultureInfo]::InvariantCulture) }
$numericVersion = $null
if (-not [Version]::TryParse($Version, [ref]$numericVersion) -or $numericVersion.Revision -lt 0 -or
    @($numericVersion.Major, $numericVersion.Minor, $numericVersion.Build, $numericVersion.Revision).Where({ $_ -gt 65535 }).Count -gt 0) {
    throw 'Die Releaseversion muss aus vier nichtnegativen Zahlen bis 65535 bestehen.'
}
$Version = $numericVersion.ToString()

try {
    $lockPath = Join-Path $repoRoot 'CameraControl.WinUI\packages.lock.json'
    if (-not (Test-Path -LiteralPath $lockPath)) { throw 'NuGet-Lockdatei fehlt. Stellen Sie zuerst die festen Projektpakete wieder her.' }
    & dotnet restore $project --configfile $config --locked-mode -r $Runtime -p:Platform=x64 2>&1 |
        Tee-Object -FilePath (Join-Path $runRoot 'restore.log') | Write-Host
    if ($LASTEXITCODE -ne 0) { throw "Restore fehlgeschlagen: $LASTEXITCODE" }
    $arguments = @('publish', $project, '--no-restore', '-c', 'Release', '-r', $Runtime, '--self-contained', 'true',
        '-p:Platform=x64', '-p:PublishProfile=', '-p:PublishSingleFile=true', '-p:PublishTrimmed=false', '-p:EnableLocalDeployment=true',
        '-p:PublishReadyToRun=false', "-p:Version=$Version", "-p:FileVersion=$Version", "-p:InformationalVersion=$Version",
        '-p:IncludeSourceRevisionInInformationalVersion=false', '-p:DebugType=none', '-p:DebugSymbols=false', '-o', $staging)
    if ($CreateMissingNativeFixture) { $arguments += '-p:VideoProbeMissingNative=true' }
    & dotnet @arguments 2>&1 | Tee-Object -FilePath (Join-Path $runRoot 'publish.log') | Write-Host
    if ($LASTEXITCODE -ne 0) { throw "Publish fehlgeschlagen: $LASTEXITCODE" }
    $files = @(Get-ChildItem -LiteralPath $staging -File -Recurse)
    if ($files.Count -ne 1 -or $files[0].Name -ne 'CameraControl.WinUI.exe') {
        $files | Select-Object FullName,Length | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'unexpected-output.json')
        throw 'Die Ausgabe enthält nicht ausschließlich die einzelne EXE-Datei.'
    }
    $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($files[0].FullName)
    if ($versionInfo.FileVersion -ne $Version -or $versionInfo.ProductVersion -ne $Version) {
        throw "EXE-Versionsnachweis fehlgeschlagen: erwartet $Version, FileVersion=$($versionInfo.FileVersion), InformationalVersion=$($versionInfo.ProductVersion)."
    }
    $outputFullPath = [IO.Path]::GetFullPath($output)
    $repoFullPath = [IO.Path]::GetFullPath($repoRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $outputFullPath.StartsWith($repoFullPath, [StringComparison]::OrdinalIgnoreCase)) { throw 'Artefaktpfad liegt außerhalb des Workspace.' }
    foreach ($knownSymbolFile in @('CameraControl.AppCore.pdb', 'CameraControl.Visca.pdb')) {
        $knownSymbolPath = Join-Path $outputFullPath $knownSymbolFile
        if (Test-Path -LiteralPath $knownSymbolPath) { Remove-Item -LiteralPath $knownSymbolPath }
    }
    $finalExe = Join-Path $outputFullPath 'CameraControl.WinUI.exe'
    $sourceStream = [IO.FileStream]::new($files[0].FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $destinationStream = [IO.FileStream]::new($finalExe, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $sourceStream.CopyTo($destinationStream, 1048576); $destinationStream.Flush($true) }
    finally { $destinationStream.Dispose(); $sourceStream.Dispose() }
    if ((Get-FileHash -LiteralPath $files[0].FullName).Hash -ne (Get-FileHash -LiteralPath $finalExe).Hash) { throw 'Die finale EXE-Kopie ist unvollständig.' }
    $finalFiles = @(Get-ChildItem -LiteralPath $outputFullPath -File -Recurse)
    if ($finalFiles.Count -ne 1 -or $finalFiles[0].Name -ne 'CameraControl.WinUI.exe') { throw 'Der finale Artefaktpfad enthält zusätzliche Dateien.' }
    $nativePackageRoot = Join-Path $env:USERPROFILE '.nuget\packages\videolan.libvlc.windows\3.0.24\build\x64'
    $nativeFiles = @(Get-ChildItem -LiteralPath $nativePackageRoot -File -Recurse | Where-Object {
        $relative = [IO.Path]::GetRelativePath($nativePackageRoot, $_.FullName)
        $relative -match '^(libvlc\.|libvlccore\.|plugins\\|lua\\|hrtfs\\)'
    })
    if ($nativeFiles.Count -lt 100) { throw 'Der native Paketnachweis ist unvollständig.' }
    $nativeFiles | ForEach-Object { [pscustomobject]@{
        Path = [IO.Path]::GetRelativePath($nativePackageRoot, $_.FullName)
        Length = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'native-package-files.json')
    [pscustomobject]@{ Passed = $true; Runtime = $Runtime; NegativeNativeFixture = [bool]$CreateMissingNativeFixture;
        ExePath = $finalExe; ExeSha256 = (Get-FileHash -LiteralPath $finalExe -Algorithm SHA256).Hash;
        ExeBytes = $files[0].Length; NativePackageFileCount = $nativeFiles.Count;
        Version = $Version; FileVersion = $versionInfo.FileVersion; InformationalVersion = $versionInfo.ProductVersion } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'summary.json')
    Write-Host "Einzelne EXE erzeugt: $finalExe"
} catch {
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $runRoot 'failure.txt')
    Write-Error $_ -ErrorAction Continue
    exit 1
}
