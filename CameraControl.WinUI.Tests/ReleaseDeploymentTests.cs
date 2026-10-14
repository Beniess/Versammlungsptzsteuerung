using CameraControl.AppCore.Storage;
using CameraControl.AppCore;
using CameraControl.AppCore.Deployment;
using CameraControl_WinUI.Deployment;
using CameraControl_WinUI.Startup;
using NUnit.Framework;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace CameraControl.WinUI.Tests;

[TestFixture, Category("DeploymentStartupOptions"), NonParallelizable]
public sealed class StartupOptionsTests
{
    private string root = null!;
    private string Host => Path.Combine(root, "Build", "CameraControl.WinUI.exe");
    private string BaseDirectory => Path.Combine(root, "Build");

    [SetUp]
    public void SetUp() => root = Path.Combine(Path.GetTempPath(), "CameraControl.Startup.Options.Tests", Guid.NewGuid().ToString("N"));

    private StartupOptions Read(bool published, string[]? arguments = null, Dictionary<string, string>? environment = null) =>
        StartupOptions.Read(arguments ?? [], published, Host, BaseDirectory,
            name => environment is not null && environment.TryGetValue(name, out string? value) ? value : null);

    [Test]
    public void OrdinaryBuildCannotEnterTheProductionDeploymentScope()
    {
        StartupOptions options = Read(false);
        Assert.That(options.Kind, Is.EqualTo(StartupKind.Development));
        Assert.That(options.IsQa, Is.True);
        Assert.That(options.Scope, Is.Null);
        Assert.That(options.DataDirectory.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase), Is.True);
        Assert.That(options.DataDirectory, Is.Not.EqualTo(WindowsDataPath.GetDirectory()));
    }

    [Test]
    public void ExplicitPublishedContractSelectsProduction()
    {
        StartupOptions options = Read(true);
        Assert.That(options.Kind, Is.EqualTo(StartupKind.Deployment));
        Assert.That(options.IsQa, Is.False);
        Assert.That(options.Scope!.QaRoot, Is.Null);
        Assert.That(options.DataDirectory, Is.EqualTo(WindowsDataPath.GetDirectory()));
    }

    [Test]
    public void DuplicateDownloadFileNameKeepsThePublishedReleaseContract()
    {
        string downloaded = Path.Combine(root, "Downloads", "CameraControl.WinUI (1).exe");
        StartupOptions options = StartupOptions.Read([], true, downloaded, BaseDirectory, _ => null);
        Assert.That(options.Kind, Is.EqualTo(StartupKind.Deployment));
        Assert.That(options.IsCandidate, Is.False);
        Assert.That(options.IsQa, Is.False);
    }

    [Test]
    public void LayoutQaKeepsTheStartedHostAndUsesExplicitQaData()
    {
        string data = Path.Combine(root, "qa-data");
        StartupOptions options = Read(true, environment: new()
        {
            ["CAMERA_CONTROL_QA_MODE"] = "1", ["CAMERA_CONTROL_QA_DATA_DIRECTORY"] = data
        });
        Assert.That(options.Kind, Is.EqualTo(StartupKind.UiQa));
        Assert.That(options.IsQa, Is.True);
        Assert.That(options.Scope, Is.Null);
        Assert.That(options.DataDirectory, Is.EqualTo(data));
    }

    [Test]
    public void LocalQaSeparatesProgramsDataAndLinks()
    {
        StartupOptions options = Read(true, ["--deployment-qa-root", root]);
        Assert.That(options.Kind, Is.EqualTo(StartupKind.Deployment));
        Assert.That(options.IsQa, Is.True);
        Assert.That(options.Scope!.QaRoot, Is.EqualTo(root));
        Assert.That(options.DataDirectory, Is.EqualTo(Path.Combine(root, "Data")));
        Assert.That(options.Scope.InstallationRoot.StartsWith(root, StringComparison.OrdinalIgnoreCase), Is.True);
        Assert.That(options.Scope.ShortcutsDirectory, Is.EqualTo(Path.Combine(root, "Menu")));
    }

    [Test]
    public void CandidateKeepsTheExplicitQaScope()
    {
        StartupOptions options = Read(true, ["--deployment-candidate", Guid.NewGuid().ToString("N"),
            "--deployment-version", "1.0.0", "--deployment-parent", "123", "--deployment-qa-root", root]);
        Assert.That(options.IsCandidate, Is.True);
        Assert.That(options.IsQa, Is.True);
        Assert.That(options.Kind, Is.EqualTo(StartupKind.Deployment));
        Assert.That(options.DataDirectory, Is.EqualTo(Path.Combine(root, "Data")));
    }

    [TestCase("fail-before-ready", DeploymentQaFault.FailBeforeReady)]
    [TestCase("cancel-after-ready", DeploymentQaFault.CancelAfterReady)]
    [TestCase("refuse-exit", DeploymentQaFault.RefuseExit)]
    public void FaultsRequireTheLocalQaScope(string value, DeploymentQaFault expected)
    {
        var environment = new Dictionary<string, string> { ["CAMERA_CONTROL_QA_DEPLOYMENT_FAULT"] = value };
        Assert.That(() => Read(true, environment: environment), Throws.InstanceOf<IOException>());
        Assert.That(Read(true, ["--deployment-qa-root", root], environment).Fault, Is.EqualTo(expected));
    }

    [Test]
    public void UnrecognizedFaultCannotRunInQaOrProduction()
    {
        var environment = new Dictionary<string, string> { ["CAMERA_CONTROL_QA_DEPLOYMENT_FAULT"] = "anything-else" };
        Assert.That(() => Read(true, ["--deployment-qa-root", root], environment), Throws.InstanceOf<IOException>());
    }

    [Test]
    public void QaCannotSelectTheProductionDataDirectory()
    {
        var environment = new Dictionary<string, string>
        {
            ["CAMERA_CONTROL_QA_MODE"] = "1", ["CAMERA_CONTROL_QA_DATA_DIRECTORY"] = WindowsDataPath.GetDirectory()
        };
        Assert.That(() => Read(true, environment: environment), Throws.InstanceOf<IOException>());
        environment["CAMERA_CONTROL_QA_DATA_DIRECTORY"] = Path.Combine(WindowsDataPath.GetDirectory(), "child");
        Assert.That(() => Read(true, environment: environment), Throws.InstanceOf<IOException>());
    }

    [Test]
    public void CandidateCannotFallThroughToDirectUiQa()
    {
        var environment = new Dictionary<string, string>
        {
            ["CAMERA_CONTROL_QA_MODE"] = "1", ["CAMERA_CONTROL_QA_DATA_DIRECTORY"] = Path.Combine(root, "Data")
        };
        Assert.That(() => Read(true, ["--deployment-candidate", Guid.NewGuid().ToString("N"),
            "--deployment-version", "1.0.0", "--deployment-parent", "123"], environment), Throws.InstanceOf<IOException>());
    }

    [Test]
    public void AmbiguousOrIncompleteQaInputsFailClosed()
    {
        Assert.That(() => Read(true, ["--deployment-qa-root"]), Throws.InstanceOf<IOException>());
        Assert.That(() => Read(true, ["--deployment-qa-root", root, "--deployment-qa-root", root]), Throws.InstanceOf<IOException>());
        Assert.That(() => Read(true, environment: new() { ["CAMERA_CONTROL_QA_MODE"] = "1" }), Throws.InstanceOf<IOException>());
        Assert.That(() => Read(true, environment: new() { ["CAMERA_CONTROL_QA_DATA_DIRECTORY"] = Path.Combine(root, "Data") }), Throws.InstanceOf<IOException>());
        Assert.That(() => Read(true, environment: new() { ["CAMERA_CONTROL_QA_MODE"] = "yes" }), Throws.InstanceOf<IOException>());
    }

    [Test]
    public void PlainBuildCannotPretendToBeADeploymentRelease()
    {
        Assert.That(() => Read(false, ["--deployment-qa-root", root]), Throws.InstanceOf<IOException>());
    }

    [Test]
    public void ConflictingQaDataInputsAreRejected()
    {
        var environment = new Dictionary<string, string>
        {
            ["CAMERA_CONTROL_QA_MODE"] = "1", ["CAMERA_CONTROL_QA_DATA_DIRECTORY"] = Path.Combine(root, "other-data")
        };
        Assert.That(() => Read(true, ["--deployment-qa-root", root], environment), Throws.InstanceOf<IOException>());
    }
}

[TestFixture, Category("ReleaseDeployment"), NonParallelizable]
public sealed class ReleaseDeploymentTests
{
    private string root = null!;
    private string evidence = null!;
    private string exeA = null!;
    private string exeB = null!;
    private string downloadA = null!;
    private string downloadB = null!;
    private ReleaseVersion versionA;
    private ReleaseVersion versionB;
    private DeploymentScope scope = null!;
    private string? previousBundleDirectory;
    private readonly List<Process> sources = [];
    private LaunchObservation? lastLaunch;
    private int statusWaitCount;
    private string DataDirectory => Path.Combine(root, "Data");

    [SetUp]
    public async Task SetUp()
    {
        root = null!;
        lastLaunch = null;
        statusWaitCount = 0;
        previousBundleDirectory = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR");
        exeA = RequiredFile("CAMERA_RELEASE_EXE_A");
        exeB = RequiredFile("CAMERA_RELEASE_EXE_B");
        versionA = FileVersion(exeA);
        versionB = FileVersion(exeB);
        Assert.That(versionB.CompareTo(versionA), Is.GreaterThan(0), "B muss eine höhere numerische FileVersion besitzen.");
        evidence = Path.Combine(RequiredDirectory("CAMERA_RELEASE_EVIDENCE_DIRECTORY"), TestContext.CurrentContext.Test.ID);
        Directory.CreateDirectory(evidence);
        root = Path.Combine(Path.GetTempPath(), "CameraControl.Release.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR", Path.Combine(root, "BundleCache"));
        scope = DeploymentScope.ForQa(root);
        downloadA = CopyDownload(exeA, "A");
        downloadB = CopyDownload(exeB, "B", "CameraControl.WinUI (1).exe");
        var settings = AppSettings.CreateDefault();
        settings = settings with
        {
            Presets = settings.Presets.Select(item => item.Id == PresetIds.Leser
                ? item with { Name = "QA Leser", Target = new CameraPosition(11, -22, 333) } : item).ToArray()
        };
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        await new JsonSettingsRepository(DataDirectory).SaveAsync(settings,
            new Dictionary<string, ReadOnlyMemory<byte>> { [PresetIds.Leser] = png });
        WriteEvidence("inputs.json", new { Root = root, ExeA = exeA, ExeB = exeB, VersionA = versionA.ToString(), VersionB = versionB.ToString(),
            Sha256A = Hash(exeA), Sha256B = Hash(exeB), Scope = scope.InstallationRoot, Menu = scope.ShortcutsDirectory,
            SearchEvidence = false, QaOnly = true });
    }

    [TearDown]
    public async Task TearDown()
    {
        try { await CleanUpAsync(); }
        finally { Environment.SetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR", previousBundleDirectory); }
    }

    private async Task CleanUpAsync()
    {
        if (root is null) return;
        using (var before = new OwnProcessCollection(OwnProcesses()))
            WriteEvidence("before-cleanup.json", new
            {
                Processes = before.Items.Select(ProcessSnapshot).ToArray(),
                ActiveMarker = ReadSharedText(Path.Combine(scope.InstallationRoot, "active.json")),
                Journal = ReadSharedText(Path.Combine(scope.InstallationRoot, "transaction.json"))
            });
        try
        {
            using var input = new FileStream(scope.LinkPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var output = File.Create(Path.Combine(evidence, "final-qa-link.lnk"));
            await input.CopyToAsync(output);
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        bool closed = true;
        foreach (Process process in OwnProcesses())
        {
            using (process)
            {
                try { await CloseAsync(process.Id); }
                catch (Exception error) when (error is AssertionException or TimeoutException or InvalidOperationException)
                { closed = false; TestContext.WriteLine(error.Message); }
            }
        }
        foreach (Process source in sources) source.Dispose();
        sources.Clear();
        string diagnostics = Path.Combine(root, "Diagnostics");
        if (Directory.Exists(diagnostics))
            foreach (string path in Directory.EnumerateFiles(diagnostics))
                File.Copy(path, Path.Combine(evidence, "status-" + Path.GetFileName(path)), overwrite: true);
        using (var processes = new OwnProcessCollection(OwnProcesses()))
            Assert.That(closed && processes.Items.Length == 0, Is.True, "Eigene QA-Instanzen müssen ohne Kill schließen. Testpfad bleibt sonst erhalten: " + root);
        if (TestContext.CurrentContext.Result.Outcome.Status == NUnit.Framework.Interfaces.TestStatus.Failed)
        {
            WriteEvidence("cleanup.json", new { Root = root, Removed = false, KilledProcesses = 0, RetainedForDiagnosis = true });
            return;
        }
        string allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CameraControl.Release.Tests")) + Path.DirectorySeparatorChar;
        Assert.That(Path.GetFullPath(root).StartsWith(allowed, StringComparison.OrdinalIgnoreCase), Is.True);
        Assert.That(Guid.TryParseExact(Path.GetFileName(root), "N", out _), Is.True);
        ValidateTree(root);
        Directory.Delete(root, recursive: true);
        WriteEvidence("cleanup.json", new { Root = root, Removed = !Directory.Exists(root), KilledProcesses = 0 });
    }

    [Test]
    public async Task NewDownloadedExeSwitchesToBAndTheRealLinkWorksWithoutDownloads()
    {
        Process sourceA = Launch(downloadA);
        Status firstA = await WaitCommittedAsync(versionA);
        await WaitExitedAsync(sourceA.Id);
        await AssertActiveAsync(firstA, versionA, exeA, "first-A");
        await CloseAsync(firstA.ProcessId);
        Dictionary<string, string> data = DataHashes();
        LaunchLink();
        Status runningA = await WaitCommittedAsync(versionA);
        Process sourceB = Launch(downloadB);
        Status firstB = await WaitCommittedAsync(versionB);
        await WaitExitedAsync(sourceB.Id);
        await WaitExitedAsync(runningA.ProcessId);
        await AssertActiveAsync(firstB, versionB, exeB, "first-B");
        Assert.That(File.Exists(InstalledPath(versionA)), Is.False, "Die bekannte stillstehende Vorgängerversion muss nach Commit entfernt sein.");
        Assert.That(File.Exists(downloadA), Is.True);
        Assert.That(File.Exists(downloadB), Is.True);
        AssertData(data);
        File.Delete(downloadA);
        File.Delete(downloadB);
        await CloseAsync(firstB.ProcessId);
        LaunchLink();
        Status linkedB = await WaitCommittedAsync(versionB);
        await AssertActiveAsync(linkedB, versionB, exeB, "link-B-downloads-removed");
        Process oldSource = Launch(CopyDownload(exeA, "OldA"));
        await WaitExitedAsync(oldSource.Id);
        Assert.That(ReadMarkerVersion(), Is.EqualTo(versionB.ToString()));
        Assert.That(IsLive(linkedB), Is.True);
        AssertData(data);
        WriteEvidence("local-update.json", new { Passed = true, FirstA = firstA, FirstB = firstB, LinkB = linkedB,
            RemovedDownloads = new[] { downloadA, downloadB }, DataHashes = data, OldVersionDidNotDowngrade = true });
    }

    [Test]
    public async Task UnreachableLoopbackCameraDoesNotPreventTheStartupCommit()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int unusedPort = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var repository = new JsonSettingsRepository(DataDirectory);
        AppSettings saved = (await repository.LoadAsync()).Settings;
        await repository.SaveAsync(saved with { Connection = saved.Connection with { Host = "127.0.0.1", ViscaPort = unusedPort } });
        Process source = Launch(downloadA);
        Status active = await WaitCommittedAsync(versionA);
        await WaitExitedAsync(source.Id);
        await AssertActiveAsync(active, versionA, exeA, "camera-unavailable-commit");
        WriteEvidence("camera-unavailable.json", new { Passed = true, Host = "127.0.0.1", Port = unusedPort, Active = active });
    }

    [Test]
    public async Task TwoDownloadedBStartsLeaveOneCommittedWriterAndOneWindow()
    {
        Process sourceA = Launch(downloadA);
        Status a = await WaitCommittedAsync(versionA);
        await WaitExitedAsync(sourceA.Id);
        Process first = Launch(downloadB);
        Process second = Launch(downloadB);
        Status b = await WaitCommittedAsync(versionB);
        await Task.WhenAll(WaitExitedAsync(first.Id), WaitExitedAsync(second.Id), WaitExitedAsync(a.ProcessId));
        await AssertActiveAsync(b, versionB, exeB, "concurrent-B");
        using var processes = new OwnProcessCollection(OwnProcesses());
        Assert.That(processes.Items.Select(process => process.Id), Is.EqualTo(new[] { b.ProcessId }));
        nint handle = (nint)b.WindowHandle;
        Assert.That(ShowWindowAsync(handle, 6), Is.True);
        await WaitWindowStateAsync(() => IsIconic(handle), "Das eigene QA-Fenster muss tatsächlich minimiert sein.");
        Process installedDuplicate = Launch(b.ProcessPath);
        await WaitExitedAsync(installedDuplicate.Id);
        Assert.That(IsLive(b), Is.True);
        await WaitWindowStateAsync(() => !IsIconic(handle), "Der Duplikatstart muss das vorhandene Fenster wiederherstellen.");
        await WaitWindowStateAsync(() => GetForegroundWindow() == handle, "Der Duplikatstart muss das eigene vorhandene Fenster aktivieren.");
        WriteEvidence("two-starts.json", new { Passed = true, FirstSource = first.Id, SecondSource = second.Id, Active = b,
            Writers = 1, MinimizedBeforeDuplicate = true, RestoredAfterDuplicate = true, ForegroundWindow = GetForegroundWindow().ToInt64() });
    }

    [TestCase("fail-before-ready")]
    [TestCase("cancel-after-ready")]
    public async Task FailedOrCancelledCandidatePreservesAAndUserData(string fault)
    {
        Process sourceA = Launch(downloadA);
        Status a = await WaitCommittedAsync(versionA);
        await WaitExitedAsync(sourceA.Id);
        await CloseAsync(a.ProcessId);
        Dictionary<string, string> data = DataHashes();
        LaunchLink();
        Status reopenedA = await WaitCommittedAsync(versionA);
        Process sourceB = Launch(downloadB, fault);
        Status failed = await WaitStatusAsync(status => status.ProcessId == sourceB.Id && status.Phase == "Failed");
        Assert.That(failed.ReadOnly, Is.True);
        Assert.That(ReadMarkerVersion(), Is.EqualTo(versionA.ToString()));
        Assert.That((await WindowsDeploymentLinks.InspectAsync(scope.LinkPath)).TargetPath, Is.EqualTo(InstalledPath(versionA)));
        AssertData(data);
        await WaitExitedAsync(reopenedA.ProcessId);
        await CloseAsync(sourceB.Id);
        LaunchLink();
        Status recovered = await WaitCommittedAsync(versionA);
        await AssertActiveAsync(recovered, versionA, exeA, fault + "-recovered-A");
        Assert.That(ReadStatuses().Where(status => status.ProcessPath == InstalledPath(versionB)).All(status => status.ReadOnly), Is.True);
        Assert.That(File.Exists(Path.Combine(scope.InstallationRoot, "transaction.json")), Is.False);
        WriteEvidence(fault + ".json", new { Passed = true, Failed = failed, Recovered = recovered, DataHashes = data });
    }

    [Test]
    public async Task RefusedGracefulExitKeepsAAndDoesNotStartB()
    {
        Process sourceA = Launch(downloadA, "refuse-exit");
        Status a = await WaitCommittedAsync(versionA);
        await WaitExitedAsync(sourceA.Id);
        Dictionary<string, string> data = DataHashes();
        Process sourceB = Launch(downloadB);
        await WaitStatusAsync(status => status.ProcessId == sourceB.Id && status.Phase == "Failed");
        Assert.That(IsLive(a), Is.True);
        Assert.That(ReadMarkerVersion(), Is.EqualTo(versionA.ToString()));
        Assert.That(ReadStatuses().Any(status => status.ProcessPath == InstalledPath(versionB)), Is.False);
        AssertData(data);
        WriteEvidence("refused-exit.json", new { Passed = true, Active = a, KilledProcesses = 0 });
    }

    [Test]
    public async Task LockedShellLinkPreservesAAndTheNextStartRecoversTheJournal()
    {
        Process sourceA = Launch(downloadA);
        Status a = await WaitCommittedAsync(versionA);
        await WaitExitedAsync(sourceA.Id);
        await CloseAsync(a.ProcessId);
        Dictionary<string, string> data = DataHashes();
        LaunchLink();
        await WaitCommittedAsync(versionA);
        Process sourceB;
        using (var lockedLink = new FileStream(scope.LinkPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            sourceB = Launch(downloadB);
            await WaitStatusAsync(status => status.ProcessId == sourceB.Id && status.Phase == "Failed");
            Assert.That(ReadMarkerVersion(), Is.EqualTo(versionA.ToString()));
            Assert.That((await WindowsDeploymentLinks.InspectAsync(scope.LinkPath)).TargetPath, Is.EqualTo(InstalledPath(versionA)));
            Assert.That(File.Exists(Path.Combine(scope.InstallationRoot, "transaction.json")), Is.True);
        }
        await CloseAsync(sourceB.Id);
        LaunchLink();
        Status recovered = await WaitCommittedAsync(versionA);
        Assert.That(File.Exists(Path.Combine(scope.InstallationRoot, "transaction.json")), Is.False);
        AssertData(data);
        await AssertActiveAsync(recovered, versionA, exeA, "link-recovery-A");
        WriteEvidence("link-recovery.json", new { Passed = true, Recovered = recovered, DataHashes = data });
    }

    private string CopyDownload(string source, string name, string fileName = LocalDeployment.ExecutableName)
    {
        string directory = Path.Combine(root, "Downloads", name);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            input.CopyTo(output, 1048576);
            output.Flush(flushToDisk: true);
        }
        Assert.That(Hash(path), Is.EqualTo(Hash(source)));
        Assert.That(FileVersion(path), Is.EqualTo(FileVersion(source)));
        return path;
    }

    private Process Launch(string executable, string? fault = null)
    {
        Assert.That(Path.GetFullPath(executable).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), Is.True);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
        start.ArgumentList.Add("--deployment-qa-root");
        start.ArgumentList.Add(root);
        start.Environment.Remove("CAMERA_CONTROL_QA_MODE");
        start.Environment.Remove("CAMERA_CONTROL_QA_DATA_DIRECTORY");
        start.Environment.Remove("CAMERA_CONTROL_QA_DEPLOYMENT_FAULT");
        if (fault is not null) start.Environment["CAMERA_CONTROL_QA_DEPLOYMENT_FAULT"] = fault;
        DateTimeOffset requested = DateTimeOffset.UtcNow;
        Process process = Process.Start(start) ?? throw new AssertionException("Die eigene QA-EXE wurde nicht gestartet.");
        lastLaunch = ObserveLaunch(process, requested, executable);
        sources.Add(process);
        return process;
    }

    private void LaunchLink()
    {
        Assert.That(scope.QaRoot, Is.EqualTo(root));
        string target = InstalledPath(ReleaseVersion.Parse(ReadMarkerVersion()));
        DateTimeOffset requested = DateTimeOffset.UtcNow;
        using Process? process = Process.Start(new ProcessStartInfo(scope.LinkPath) { UseShellExecute = true });
        lastLaunch = ObserveLaunch(process, requested, target);
    }

    private async Task<Status> WaitCommittedAsync(ReleaseVersion version) => await WaitStatusAsync(status =>
        status.Phase == "Committed" && status.Version == version.ToString() && status.ProcessPath == InstalledPath(version) && IsLive(status));

    private async Task<Status> WaitStatusAsync(Func<Status, bool> condition)
    {
        LaunchObservation launch = lastLaunch ?? throw new AssertionException("Der echte Prozessstart für die Statusbeobachtung fehlt.");
        DateTimeOffset overallDeadline = launch.StartedUtc.AddSeconds(60 + 90 + 120);
        DateTimeOffset deadline = launch.StartedUtc.AddSeconds(60);
        DateTimeOffset? managedStarted = null;
        DateTimeOffset? admissionStarted = null;
        DateTimeOffset? admissionAcquired = null;
        DateTimeOffset? candidateStarted = null;
        int? sourceProcessId = launch.ProcessId;
        string phase = "SourceColdStart";
        string record = "status-wait-" + (++statusWaitCount) + ".json";
        void Record(bool passed) => WriteEvidence(record, new { Passed = passed, Phase = phase, SourceProcessId = sourceProcessId,
            SourcePath = launch.ExecutablePath, SourceVersion = launch.Version, OverallDeadlineUtc = overallDeadline,
            ProcessStartedUtc = launch.StartedUtc, ManagedStartedUtc = managedStarted, CandidateStartedUtc = candidateStarted,
            AdmissionStartedUtc = admissionStarted, AdmissionAcquiredUtc = admissionAcquired,
            DeadlineUtc = deadline, ObservedUtc = DateTimeOffset.UtcNow });
        while (DateTimeOffset.UtcNow < deadline)
        {
            Status[] statuses = ReadStatuses();
            Status? match = statuses.FirstOrDefault(condition);
            if (match is not null) { Record(true); return match; }
            if (managedStarted is null)
            {
                Status? started = statuses.Where(status => status.Phase == "Started" && status.RecordedUtc >= launch.StartedUtc &&
                    status.ProcessPath.Equals(launch.ExecutablePath, StringComparison.OrdinalIgnoreCase) && status.Version == launch.Version &&
                    (sourceProcessId is null || status.ProcessId == sourceProcessId)).OrderBy(status => status.RecordedUtc).FirstOrDefault();
                if (started is not null)
                {
                    sourceProcessId = started.ProcessId;
                    managedStarted = started.RecordedUtc;
                    deadline = Earlier(managedStarted.Value.AddSeconds(90), overallDeadline);
                    phase = "ManagedSourceAndStaging";
                }
            }
            if (managedStarted is not null && sourceProcessId is int admissionSource)
            {
                var admission = ReadAdmissionTimes(admissionSource, launch);
                if (admissionStarted is null && admission.Started is DateTimeOffset waiting)
                {
                    admissionStarted = waiting;
                    overallDeadline = launch.StartedUtc.AddSeconds(60 + 300 + 90 + 120);
                    deadline = Earlier(waiting.AddMinutes(5), overallDeadline);
                    phase = "Admission";
                }
                if (admissionStarted is not null && admissionAcquired is null && admission.Acquired is DateTimeOffset acquired && acquired >= admissionStarted)
                {
                    admissionAcquired = acquired;
                    deadline = Earlier(acquired.AddSeconds(90), overallDeadline);
                    phase = "ManagedSourceAndStaging";
                }
            }
            if (managedStarted is not null && candidateStarted is null && sourceProcessId is int sourceId &&
                ReadCandidateStart(sourceId, launch) is DateTimeOffset actualStart)
            {
                candidateStarted = actualStart;
                deadline = Earlier(actualStart.AddMinutes(2), overallDeadline);
                phase = "CandidateStartAndCommit";
            }
            await Task.Delay(100);
        }
        Record(false);
        throw new AssertionException("Der erwartete echte Startstatus fehlt in Phase " + phase + ". QA-Pfad: " + root);
    }

    private static DateTimeOffset Earlier(DateTimeOffset first, DateTimeOffset second) => first <= second ? first : second;

    private static LaunchObservation ObserveLaunch(Process? process, DateTimeOffset requested, string executable)
    {
        string version = FileVersion(executable).ToString();
        if (process is null) return new LaunchObservation(null, requested, executable, version);
        try { return new LaunchObservation(process.Id, process.StartTime.ToUniversalTime(), executable, version); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        { return new LaunchObservation(process.Id, requested, executable, version); }
    }

    private DateTimeOffset? ReadCandidateStart(int sourceProcessId, LaunchObservation launch)
    {
        string? text = ReadSharedText(Path.Combine(root, "Diagnostics", sourceProcessId + ".events.jsonl"));
        if (text is null) return null;
        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement item = document.RootElement;
                string detail = item.GetProperty("detail").GetString()!;
                DateTimeOffset recorded = item.GetProperty("atUtc").GetDateTimeOffset();
                if (recorded < launch.StartedUtc || item.GetProperty("processId").GetInt32() != sourceProcessId ||
                    item.GetProperty("version").GetString() != launch.Version || !detail.StartsWith("candidate.spawned;pid=", StringComparison.Ordinal) ||
                    !detail.EndsWith(";version=" + launch.Version, StringComparison.Ordinal)) continue;
                int candidateId = int.Parse(detail.Split(';')[1][4..], System.Globalization.CultureInfo.InvariantCulture);
                try
                {
                    using Process process = Process.GetProcessById(candidateId);
                    if (!string.Equals(process.MainModule?.FileName, InstalledPath(ReleaseVersion.Parse(launch.Version)), StringComparison.OrdinalIgnoreCase)) continue;
                    DateTimeOffset actualStart = process.StartTime.ToUniversalTime();
                    if (actualStart >= launch.StartedUtc) return actualStart;
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return recorded; }
            }
            catch (JsonException) { }
        }
        return null;
    }

    private (DateTimeOffset? Started, DateTimeOffset? Acquired) ReadAdmissionTimes(int sourceProcessId, LaunchObservation launch)
    {
        DateTimeOffset? started = null;
        DateTimeOffset? acquired = null;
        string? text = ReadSharedText(Path.Combine(root, "Diagnostics", sourceProcessId + ".events.jsonl"));
        if (text is null) return (null, null);
        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement item = document.RootElement;
                DateTimeOffset recorded = item.GetProperty("atUtc").GetDateTimeOffset();
                if (recorded < launch.StartedUtc || item.GetProperty("processId").GetInt32() != sourceProcessId || item.GetProperty("version").GetString() != launch.Version) continue;
                string? detail = item.GetProperty("detail").GetString();
                if (detail == "admission.wait;seconds=300") started ??= recorded;
                else if (detail == "admission.acquired") acquired ??= recorded;
            }
            catch (JsonException) { }
        }
        return (started, acquired);
    }

    private Status[] ReadStatuses()
    {
        string directory = Path.Combine(root, "Diagnostics");
        if (!Directory.Exists(directory)) return [];
        var result = new List<Status>();
        foreach (string path in Directory.EnumerateFiles(directory, "*.json"))
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            result.Add(JsonSerializer.Deserialize<Status>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new AssertionException("Eine QA-Statusdatei ist leer."));
        }
        return result.ToArray();
    }

    private async Task AssertActiveAsync(Status status, ReleaseVersion version, string original, string name)
    {
        Assert.That(status.ReadOnly, Is.False);
        Assert.That(status.IsQa, Is.True);
        Assert.That(status.DataDirectory, Is.EqualTo(DataDirectory));
        Assert.That(status.ProcessPath, Is.EqualTo(InstalledPath(version)));
        using Process process = Process.GetProcessById(status.ProcessId);
        Assert.That(process.MainModule!.FileName, Is.EqualTo(status.ProcessPath).IgnoreCase);
        var xamlModule = process.Modules.Cast<ProcessModule>().Single(module =>
            string.Equals(module.ModuleName, "Microsoft.UI.Xaml.dll", StringComparison.OrdinalIgnoreCase));
        Assert.That(xamlModule.FileName.StartsWith(Path.Combine(root, "BundleCache") + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase), Is.True, "Die native WinUI-Komponente muss aus dem eigenen QA-SFX-Verzeichnis kommen.");
        Assert.That(FileVersion(status.ProcessPath), Is.EqualTo(version));
        Assert.That(Hash(status.ProcessPath), Is.EqualTo(Hash(original)));
        Assert.That(status.Sha256, Is.EqualTo(Hash(original)).IgnoreCase);
        Assert.That(GetWindowThreadProcessId((nint)status.WindowHandle, out uint owner), Is.Not.Zero);
        Assert.That(owner, Is.EqualTo((uint)status.ProcessId));
        Assert.That(IsWindowVisible((nint)status.WindowHandle), Is.True);
        Assert.That(ReadMarkerVersion(), Is.EqualTo(version.ToString()));
        ShellLinkInfo link = await WindowsDeploymentLinks.InspectAsync(scope.LinkPath);
        Assert.That(link.TargetPath, Is.EqualTo(status.ProcessPath));
        Assert.That(link.AppUserModelId, Is.EqualTo(scope.AppUserModelId));
        Assert.That(link.Arguments, Does.Contain(root));
        WriteEvidence(name + ".json", new { Status = status, Link = link, ActualProcessPath = process.MainModule.FileName,
            ActualFileVersion = FileVersionInfo.GetVersionInfo(status.ProcessPath).FileVersion, ActualSha256 = Hash(status.ProcessPath),
            XamlModule = xamlModule.FileName });
    }

    private async Task CloseAsync(int processId)
    {
        Process process;
        try { process = Process.GetProcessById(processId); }
        catch (ArgumentException) { return; }
        using (process)
        {
            if (process.HasExited) return;
            Assert.That(process.MainModule!.FileName.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), Is.True);
            var timer = Stopwatch.StartNew();
            nint handle = 0;
            while (!process.HasExited && timer.Elapsed < TimeSpan.FromSeconds(10))
            {
                process.Refresh();
                EnumWindows((window, _) =>
                {
                    if (IsWindowVisible(window) && GetWindowThreadProcessId(window, out uint owner) != 0 && owner == processId)
                    { handle = window; return false; }
                    return true;
                }, 0);
                if (handle != 0 && GetWindowThreadProcessId(handle, out uint currentOwner) != 0 && currentOwner == processId) break;
                handle = 0;
                await Task.Delay(100);
            }
            if (process.HasExited) return;
            Assert.That(handle, Is.Not.Zero, "Die eigene QA-Instanz hat kein schließbares Fenster.");
            Assert.That(GetWindowThreadProcessId(handle, out uint owner), Is.Not.Zero);
            Assert.That(owner, Is.EqualTo((uint)processId));
            Assert.That(PostMessage(handle, 0x0010, 0, 0), Is.True, "WM_CLOSE muss an das eigene QA-Fenster gesendet werden.");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    private static async Task WaitExitedAsync(int processId)
    {
        Process process;
        try { process = Process.GetProcessById(processId); }
        catch (ArgumentException) { return; }
        using (process) await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));
    }

    private static async Task WaitWindowStateAsync(Func<bool> expected, string message)
    {
        var timer = Stopwatch.StartNew();
        while (!expected() && timer.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(50);
        Assert.That(expected(), Is.True, message);
    }

    private IEnumerable<Process> OwnProcesses()
    {
        var candidates = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(LocalDeployment.ExecutableName)).ToList();
        var known = candidates.Select(process => process.Id).ToHashSet();
        foreach (Process source in sources)
        {
            if (!known.Add(source.Id)) continue;
            try { candidates.Add(Process.GetProcessById(source.Id)); }
            catch (ArgumentException) { }
        }
        foreach (Process process in candidates)
        {
            bool own;
            try { own = !process.HasExited && process.MainModule!.FileName.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { own = false; }
            if (own) yield return process;
            else process.Dispose();
        }
    }

    private static bool IsLive(Status status)
    {
        try { using Process process = Process.GetProcessById(status.ProcessId); return !process.HasExited &&
            string.Equals(process.MainModule?.FileName, status.ProcessPath, StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private Dictionary<string, string> DataHashes() => Directory.EnumerateFiles(DataDirectory, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(DataDirectory, path), Hash, StringComparer.OrdinalIgnoreCase);
    private void AssertData(Dictionary<string, string> expected) => Assert.That(DataHashes(), Is.EquivalentTo(expected));
    private string InstalledPath(ReleaseVersion version) => Path.Combine(scope.InstallationRoot, "Versions", version.ToString(), LocalDeployment.ExecutableName);
    private string ReadMarkerVersion()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(scope.InstallationRoot, "active.json")));
        return document.RootElement.GetProperty("release").GetProperty("version").GetString()!;
    }
    private void WriteEvidence(string name, object value) => File.WriteAllText(Path.Combine(evidence, name),
        JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    private static string? ReadSharedText(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    private static object ProcessSnapshot(Process process)
    {
        try { return new { process.Id, Path = process.MainModule?.FileName, WindowHandle = process.MainWindowHandle.ToInt64() }; }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        { return new { process.Id, Status = "Während der Aufnahme beendet oder nicht mehr lesbar.", Error = error.GetType().Name }; }
    }
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static ReleaseVersion FileVersion(string path) => ReleaseVersion.Parse(FileVersionInfo.GetVersionInfo(path).FileVersion!);
    private static string RequiredFile(string name)
    {
        string path = RequiredDirectory(name);
        Assert.That(File.Exists(path), Is.True, "QA-EXE fehlt: " + path);
        return path;
    }
    private static string RequiredDirectory(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        Assert.That(value, Is.Not.Null.And.Not.Empty, name + " fehlt. Verwende scripts/Test-Release.ps1.");
        return Path.GetFullPath(value!);
    }
    private static void ValidateTree(string path)
    {
        Assert.That(File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint), Is.False, "Kein QA-Reparsepfad darf rekursiv gelöscht werden.");
        if (Directory.Exists(path)) foreach (string child in Directory.EnumerateFileSystemEntries(path)) ValidateTree(child);
    }

    private sealed record Status(string Phase, int ProcessId, string ProcessPath, string Version, string Sha256,
        bool ReadOnly, long WindowHandle, string DataDirectory, bool IsQa, string? Error, DateTimeOffset RecordedUtc);
    private sealed record LaunchObservation(int? ProcessId, DateTimeOffset StartedUtc, string ExecutablePath, string Version);
    private sealed class OwnProcessCollection(IEnumerable<Process> processes) : IDisposable
    {
        internal Process[] Items { get; } = processes.ToArray();
        public void Dispose() { foreach (Process process in Items) process.Dispose(); }
    }
    [DllImport("user32.dll", ExactSpelling = true)] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    private delegate bool WindowCallback(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, nint parameter);
    [DllImport("user32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nuint parameter, nint value);
}
