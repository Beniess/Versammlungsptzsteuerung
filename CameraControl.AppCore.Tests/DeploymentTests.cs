using System.Text.Json;
using System.Text.Json.Nodes;
using CameraControl.AppCore.Deployment;
using NUnit.Framework;

namespace CameraControl.AppCore.Tests;

[TestFixture, Category("Deployment"), NonParallelizable]
public sealed class DeploymentTests
{
    private string testRoot = null!;
    private string installationRoot = null!;
    private string dataPath = null!;
    private ScriptedRuntime runtime = null!;
    private ScriptedLinks links = null!;
    private static readonly ReleaseVersion VersionA = ReleaseVersion.Parse("1.2.0");
    private static readonly ReleaseVersion VersionB = ReleaseVersion.Parse("1.10.0");
    private string MarkerPath => Path.Combine(installationRoot, "active.json");
    private string JournalPath => Path.Combine(installationRoot, "transaction.json");

    [SetUp]
    public void SetUp()
    {
        testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CameraControl.Deployment.Tests", Guid.NewGuid().ToString("N")));
        installationRoot = Path.Combine(testRoot, "Programs", "Versammlungsptzsteuerung");
        dataPath = Path.Combine(testRoot, "Versammlungsptzsteuerung", "Data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(dataPath)!);
        File.WriteAllText(dataPath, "existing-user-data");
        runtime = new ScriptedRuntime();
        links = new ScriptedLinks(Path.Combine(testRoot, "Menu"));
    }

    [TearDown]
    public void CleanUp()
    {
        Assert.That(File.ReadAllText(dataPath), Is.EqualTo("existing-user-data"));
        string allowedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CameraControl.Deployment.Tests")) + Path.DirectorySeparatorChar;
        Assert.That(testRoot.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase), Is.True);
        Assert.That(Guid.TryParseExact(Path.GetFileName(testRoot), "N", out _), Is.True);
        Assert.That(File.GetAttributes(testRoot).HasFlag(FileAttributes.ReparsePoint), Is.False);
        Directory.Delete(testRoot, recursive: true);
        Assert.That(Directory.Exists(testRoot), Is.False);
        TestContext.WriteLine("Removed isolated deployment directory: " + testRoot);
    }

    private LocalDeployment Deployment(TimeSpan? timeout = null) => new(installationRoot, runtime, links, timeout);

    private string Source(string version, string contents = "executable-content")
    {
        string path = Path.Combine(testRoot, "Downloads", version, "downloaded.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    private async Task<InstalledRelease> InstallA()
    {
        DeploymentResult result = await Deployment().StartAsync(Source("A", "version-A"), VersionA);
        Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.CandidateCommitted));
        return result.ActiveRelease!;
    }

    private string ActiveVersion() => JsonNode.Parse(File.ReadAllText(MarkerPath))!["release"]!["version"]!.GetValue<string>();

    [TestCase("1.9.0", "1.10.0", -1)]
    [TestCase("2.0.0", "1.99.99", 1)]
    [TestCase("1.0.0", "1.0.0.0", 0)]
    [TestCase("1.0.0.1", "1.0.0", 1)]
    public void ReleaseVersionsUseNumericalComparison(string left, string right, int expected)
    {
        Assert.That(Math.Sign(ReleaseVersion.Parse(left).CompareTo(ReleaseVersion.Parse(right))), Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("1.0")]
    [TestCase("1.0.0-beta")]
    [TestCase("../1.0.0")]
    [TestCase("01.0.0")]
    [TestCase("1.0.-1")]
    [TestCase("1.0.0.0.0")]
    [TestCase("2147483648.0.0")]
    public void MalformedReleaseVersionsAreRejected(string value)
    {
        Assert.That(() => ReleaseVersion.Parse(value), Throws.TypeOf<FormatException>());
    }

    [Test]
    public async Task FirstInstallCopiesHostFileBeforeHealthAndSwitchesOnlyAfterReady()
    {
        string source = Source("A", "complete-host-executable");
        byte[] expected = File.ReadAllBytes(source);
        runtime.BeforeReady = candidate =>
        {
            Assert.That(File.ReadAllBytes(candidate.Release.ExecutablePath), Is.EqualTo(expected));
            Assert.That(File.Exists(MarkerPath), Is.False);
            Assert.That(File.Exists(links.LinkPath), Is.False);
            Assert.That(candidate.Token, Is.Not.EqualTo(Guid.Empty));
            return Task.CompletedTask;
        };

        DeploymentResult result = await Deployment().StartAsync(source, VersionA);

        Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.CandidateCommitted));
        Assert.That(result.States, Is.EqualTo(new[] { DeploymentState.Unconfigured, DeploymentState.Staged,
            DeploymentState.Starting, DeploymentState.Ready, DeploymentState.Committed }));
        Assert.That(result.ActiveRelease!.ExecutablePath, Is.EqualTo(Path.Combine(installationRoot, "Versions", "1.2.0", LocalDeployment.ExecutableName)));
        Assert.That(ActiveVersion(), Is.EqualTo("1.2.0"));
        Assert.That(File.ReadAllText(links.LinkPath), Is.EqualTo(result.ActiveRelease.ExecutablePath));
        Assert.That(runtime.Committed, Has.Count.EqualTo(1));
        Assert.That(File.Exists(JournalPath), Is.False);
        File.Delete(source);
        Assert.That(File.ReadAllBytes(result.ActiveRelease.ExecutablePath), Is.EqualTo(expected));
        TestContext.WriteLine("Activated checked local executable; deleted download; local copy remains readable.");
    }

    [Test]
    public async Task NewVersionRetainsAUntilCommitThenRemovesOnlyTheStoppedPreviousRelease()
    {
        InstalledRelease a = await InstallA();
        runtime.BeforeReady = candidate =>
        {
            Assert.That(ActiveVersion(), Is.EqualTo("1.2.0"));
            Assert.That(File.ReadAllText(links.LinkPath), Is.EqualTo(a.ExecutablePath));
            Assert.That(File.Exists(a.ExecutablePath), Is.True);
            Assert.That(candidate.Release.Version, Is.EqualTo(VersionB));
            return Task.CompletedTask;
        };
        runtime.BeforeCommit = _ => Assert.That(File.Exists(a.ExecutablePath), Is.True);

        DeploymentResult result = await Deployment().StartAsync(Source("B", "version-B"), VersionB);

        Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.CandidateCommitted));
        Assert.That(ActiveVersion(), Is.EqualTo("1.10.0"));
        Assert.That(runtime.ExitRequests, Does.Contain(a));
        Assert.That(File.Exists(a.ExecutablePath), Is.False);
        Assert.That(Directory.Exists(Path.GetDirectoryName(a.ExecutablePath)), Is.False);
        Assert.That(File.ReadAllText(Path.Combine(testRoot, "Downloads", "A", "downloaded.exe")), Is.EqualTo("version-A"));
        Assert.That(File.ReadAllText(Path.Combine(testRoot, "Downloads", "B", "downloaded.exe")), Is.EqualTo("version-B"));
        Assert.That(File.Exists(result.ActiveRelease!.ExecutablePath), Is.True);
        Assert.That(File.ReadAllText(dataPath), Is.EqualTo("existing-user-data"));
    }

    [TestCase("running")]
    [TestCase("changed-hash")]
    [TestCase("foreign-file")]
    [TestCase("foreign-directory")]
    [TestCase("query-failure")]
    [TestCase("open-file")]
    public async Task UnsafePreviousReleaseCleanupCannotUndoTheConfirmedCommit(string reason)
    {
        InstalledRelease a = await InstallA();
        if (reason == "running") runtime.RunningPaths.Add(a.ExecutablePath);
        if (reason == "changed-hash") runtime.BeforeCommit = _ => File.AppendAllText(a.ExecutablePath, "changed-after-commit");
        string foreign = Path.Combine(Path.GetDirectoryName(a.ExecutablePath)!, "foreign");
        if (reason == "foreign-file") File.WriteAllText(foreign, "keep");
        if (reason == "foreign-directory") Directory.CreateDirectory(foreign);
        if (reason == "query-failure") runtime.FailRunningQuery = true;
        using FileStream? open = reason == "open-file" ? new FileStream(a.ExecutablePath, FileMode.Open, FileAccess.Read, FileShare.Read) : null;

        DeploymentResult result = await Deployment().StartAsync(Source("B", "version-B"), VersionB);

        Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.CandidateCommitted));
        Assert.That(ActiveVersion(), Is.EqualTo(VersionB.ToString()));
        Assert.That(File.ReadAllText(links.LinkPath), Is.EqualTo(result.ActiveRelease!.ExecutablePath));
        Assert.That(File.Exists(a.ExecutablePath), Is.True);
        Assert.That(File.Exists(result.ActiveRelease.ExecutablePath), Is.True);
        Assert.That(File.Exists(JournalPath), Is.False);
        Assert.That(runtime.RunningQueries, Does.Contain(a));
        if (reason == "foreign-file") Assert.That(File.ReadAllText(foreign), Is.EqualTo("keep"));
        if (reason == "foreign-directory") Assert.That(Directory.Exists(foreign), Is.True);
    }

    [TestCase("1.2.0")]
    [TestCase("1.1.9")]
    public async Task SameOrOlderDownloadRedirectsToActiveWithoutNewCandidate(string version)
    {
        InstalledRelease a = await InstallA();
        string before = File.ReadAllText(MarkerPath);
        DeploymentResult result = await Deployment().StartAsync(Source("other", "other-content"), ReleaseVersion.Parse(version));
        Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.RedirectToActive));
        Assert.That(result.ActiveRelease, Is.EqualTo(a));
        Assert.That(runtime.Started, Has.Count.EqualTo(1));
        Assert.That(File.ReadAllText(MarkerPath), Is.EqualTo(before));
    }

    [Test]
    public async Task ActiveHostContinuesWithoutLaunchingItself()
    {
        InstalledRelease a = await InstallA();
        DeploymentResult result = await Deployment().StartAsync(a.ExecutablePath, VersionA);
        Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.ContinueCurrent));
        Assert.That(runtime.Started, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task UnresponsiveOldInstanceKeepsMarkerAndLinkAtA()
    {
        InstalledRelease a = await InstallA();
        runtime.RefuseExitPath = a.ExecutablePath;
        DeploymentResult result = await Deployment().StartAsync(Source("B"), VersionB);
        Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.Failed));
        Assert.That(result.ActiveRelease, Is.EqualTo(a));
        Assert.That(ActiveVersion(), Is.EqualTo("1.2.0"));
        Assert.That(File.ReadAllText(links.LinkPath), Is.EqualTo(a.ExecutablePath));
        Assert.That(runtime.Started, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task FailedCandidateHealthKeepsMarkerAndLinkAtA()
    {
        InstalledRelease a = await InstallA();
        runtime.Healthy = false;
        DeploymentResult result = await Deployment().StartAsync(Source("B"), VersionB);
        Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.Failed));
        Assert.That(result.States.Last(), Is.EqualTo(DeploymentState.Failed));
        Assert.That(result.ActiveRelease, Is.EqualTo(a));
        Assert.That(ActiveVersion(), Is.EqualTo("1.2.0"));
        Assert.That(File.ReadAllText(links.LinkPath), Is.EqualTo(a.ExecutablePath));
        Assert.That(runtime.Committed, Has.Count.EqualTo(1));
        Assert.That(runtime.ExitRequests.Select(release => release.Version), Does.Contain(VersionB));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LinkSwitchFailureRestoresAIncludingFailureAfterReplacement(bool afterReplacement)
    {
        InstalledRelease a = await InstallA();
        links.FailSwitch = true;
        links.FailAfterReplacement = afterReplacement;
        DeploymentResult result = await Deployment().StartAsync(Source("B"), VersionB);
        Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.Failed));
        Assert.That(ActiveVersion(), Is.EqualTo("1.2.0"));
        Assert.That(File.ReadAllText(links.LinkPath), Is.EqualTo(a.ExecutablePath));
        Assert.That(File.Exists(a.ExecutablePath), Is.True);
    }

    [Test]
    public async Task FailedFirstInstallLeavesNoActiveMarkerOrLinkAndCanBeRepeated()
    {
        string source = Source("A");
        links.FailSwitch = true;
        links.FailAfterReplacement = true;
        DeploymentResult failed = await Deployment().StartAsync(source, VersionA);
        Assert.That(failed.Disposition, Is.EqualTo(DeploymentDisposition.Failed));
        Assert.That(failed.ActiveRelease, Is.Null);
        Assert.That(File.Exists(MarkerPath), Is.False);
        Assert.That(File.Exists(links.LinkPath), Is.False);
        links.FailSwitch = false;
        Assert.That((await Deployment().StartAsync(source, VersionA)).Disposition, Is.EqualTo(DeploymentDisposition.CandidateCommitted));
    }

    [Test]
    public async Task LockedSourceCannotReplaceActiveRelease()
    {
        InstalledRelease a = await InstallA();
        string source = Source("B");
        using var locked = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None);
        DeploymentResult result = await Deployment().StartAsync(source, VersionB);
        Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.Failed));
        Assert.That(result.ActiveRelease, Is.EqualTo(a));
        Assert.That(ActiveVersion(), Is.EqualTo("1.2.0"));
    }

    [Test]
    public async Task CopyDestinationFailureCannotReplaceActiveRelease()
    {
        InstalledRelease a = await InstallA();
        File.WriteAllText(Path.Combine(installationRoot, "Versions", "1.10.0"), "foreign-file");
        DeploymentResult result = await Deployment().StartAsync(Source("B"), VersionB);
        Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.Failed));
        Assert.That(result.ActiveRelease, Is.EqualTo(a));
        Assert.That(ActiveVersion(), Is.EqualTo("1.2.0"));
        Assert.That(File.ReadAllText(Path.Combine(installationRoot, "Versions", "1.10.0")), Is.EqualTo("foreign-file"));
    }

    [Test]
    public async Task ExistingDifferentCandidateFileIsNotOverwritten()
    {
        await InstallA();
        string existing = Path.Combine(installationRoot, "Versions", "1.10.0", LocalDeployment.ExecutableName);
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        File.WriteAllText(existing, "different-release");
        DeploymentResult result = await Deployment().StartAsync(Source("B", "new-release"), VersionB);
        Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.Failed));
        Assert.That(File.ReadAllText(existing), Is.EqualTo("different-release"));
        Assert.That(ActiveVersion(), Is.EqualTo("1.2.0"));
    }

    [Test]
    public async Task ExistingUnrelatedFilesAreNotUsedAsScratchFiles()
    {
        string directory = Path.Combine(installationRoot, "Versions", "1.2.0");
        Directory.CreateDirectory(directory);
        string copyFile = Path.Combine(directory, ".copy.tmp");
        string markerFile = Path.Combine(installationRoot, "active.json.tmp");
        File.WriteAllText(copyFile, "existing-copy-name");
        File.WriteAllText(markerFile, "existing-marker-name");
        DeploymentResult result = await Deployment().StartAsync(Source("A"), VersionA);
        Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.CandidateCommitted));
        Assert.That(File.Exists(copyFile), Is.True);
        Assert.That(File.ReadAllText(copyFile), Is.EqualTo("existing-copy-name"));
        Assert.That(File.ReadAllText(markerFile), Is.EqualTo("existing-marker-name"));
    }

    [TestCase("relative.exe")]
    [TestCase("C:relative.exe")]
    [TestCase("")]
    public async Task NonAbsoluteHostPathsAreRejectedWithoutSettingUp(string source)
    {
        DeploymentResult result = await Deployment().StartAsync(source, VersionA);
        Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.Failed));
        Assert.That(runtime.Started, Is.Empty);
        Assert.That(File.Exists(MarkerPath), Is.False);
    }

    [Test]
    public async Task ExtractionDirectoryOrNonExecutableFileCannotBeStagedAsHost()
    {
        string extraction = Path.Combine(testRoot, "extracted");
        Directory.CreateDirectory(extraction);
        string dll = Path.Combine(extraction, "managed.dll");
        File.WriteAllText(dll, "managed-assembly");
        Assert.That((await Deployment().StartAsync(extraction, VersionA)).Disposition, Is.EqualTo(DeploymentDisposition.Failed));
        Assert.That((await Deployment().StartAsync(dll, VersionA)).Disposition, Is.EqualTo(DeploymentDisposition.Failed));
        Assert.That(runtime.Started, Is.Empty);
    }

    [TestCase("{")]
    [TestCase("{\"schemaVersion\":2}")]
    public async Task InvalidMarkerIsNotOverwritten(string json)
    {
        Directory.CreateDirectory(installationRoot);
        File.WriteAllText(MarkerPath, json);
        DeploymentResult result = await Deployment().StartAsync(Source("A"), VersionA);
        Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.Failed));
        Assert.That(File.ReadAllText(MarkerPath), Is.EqualTo(json));
        Assert.That(runtime.Started, Is.Empty);
    }

    [Test]
    public async Task MarkerWithForeignPathIsRejectedWithoutTouchingTarget()
    {
        await InstallA();
        string foreign = Source("foreign", "do-not-touch");
        JsonNode marker = JsonNode.Parse(File.ReadAllText(MarkerPath))!;
        marker["release"]!["executablePath"] = foreign;
        string malformed = marker.ToJsonString();
        File.WriteAllText(MarkerPath, malformed);
        DeploymentResult result = await Deployment().StartAsync(Source("B"), VersionB);
        Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.Failed));
        Assert.That(File.ReadAllText(MarkerPath), Is.EqualTo(malformed));
        Assert.That(File.ReadAllText(foreign), Is.EqualTo("do-not-touch"));
        Assert.That(runtime.Started, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ChangedActiveExecutableFailsIntegrityWithoutSilentReinstall()
    {
        InstalledRelease a = await InstallA();
        File.WriteAllText(a.ExecutablePath, "changed");
        string marker = File.ReadAllText(MarkerPath);
        DeploymentResult result = await Deployment().StartAsync(Source("A"), VersionA);
        Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.Failed));
        Assert.That(File.ReadAllText(MarkerPath), Is.EqualTo(marker));
        Assert.That(File.ReadAllText(a.ExecutablePath), Is.EqualTo("changed"));
    }

    [Test]
    public async Task InterruptedReadyTransactionRollsBackWhenOldHostStarts()
    {
        InstalledRelease a = await InstallA();
        links.InterruptSwitch = true;
        Assert.That(async () => await Deployment().StartAsync(Source("B"), VersionB), Throws.TypeOf<SimulatedTermination>());
        Assert.That(ActiveVersion(), Is.EqualTo("1.10.0"));
        Assert.That(File.Exists(JournalPath), Is.True);
        links.InterruptSwitch = false;
        DeploymentResult result = await Deployment().StartAsync(a.ExecutablePath, VersionA);
        Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.ContinueCurrent));
        Assert.That(ActiveVersion(), Is.EqualTo("1.2.0"));
        Assert.That(File.ReadAllText(links.LinkPath), Is.EqualTo(a.ExecutablePath));
        Assert.That(File.Exists(JournalPath), Is.False);
        TestContext.WriteLine("Interrupted Ready update recovered to A before normal startup.");
    }

    [Test]
    public async Task InterruptedCommittedTransactionCompletesBAndOldHostRedirects()
    {
        InstalledRelease a = await InstallA();
        runtime.InterruptCommit = true;
        Assert.That(async () => await Deployment().StartAsync(Source("B"), VersionB), Throws.TypeOf<SimulatedTermination>());
        runtime.InterruptCommit = false;
        DeploymentResult result = await Deployment().StartAsync(a.ExecutablePath, VersionA);
        Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.RedirectToActive));
        Assert.That(result.ActiveRelease!.Version, Is.EqualTo(VersionB));
        Assert.That(File.ReadAllText(links.LinkPath), Is.EqualTo(result.ActiveRelease.ExecutablePath));
        Assert.That(runtime.ExitRequests.Select(release => release.Version), Does.Contain(VersionB));
        Assert.That(File.Exists(JournalPath), Is.False);
    }

    [TestCase(DeploymentState.Staged)]
    [TestCase(DeploymentState.Starting)]
    public async Task InterruptedPreReadyStateRollsBackWithoutActivatingCandidate(DeploymentState state)
    {
        InstalledRelease a = await InstallA();
        if (state == DeploymentState.Staged) links.InterruptPrepare = true;
        else runtime.BeforeReady = _ => throw new SimulatedTermination();
        Assert.That(async () => await Deployment().StartAsync(Source("B"), VersionB), Throws.TypeOf<SimulatedTermination>());
        Assert.That(JsonNode.Parse(File.ReadAllText(JournalPath))!["state"]!.GetValue<string>(), Is.EqualTo(state.ToString()));
        links.InterruptPrepare = false;
        runtime.BeforeReady = null;
        DeploymentResult result = await Deployment().StartAsync(a.ExecutablePath, VersionA);
        Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.ContinueCurrent));
        Assert.That(ActiveVersion(), Is.EqualTo("1.2.0"));
        Assert.That(File.ReadAllText(links.LinkPath), Is.EqualTo(a.ExecutablePath));
        Assert.That(runtime.Committed, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task CleanupFailureAfterActivationDoesNotRollBackTheCommittedVersion()
    {
        await InstallA();
        links.FailDiscard = true;
        DeploymentResult result = await Deployment().StartAsync(Source("B"), VersionB);
        Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.CandidateCommitted));
        Assert.That(ActiveVersion(), Is.EqualTo("1.10.0"));
        Assert.That(File.Exists(JournalPath), Is.True);
        Assert.That(JsonNode.Parse(File.ReadAllText(JournalPath))!["state"]!.GetValue<string>(), Is.EqualTo("Committed"));
        links.FailDiscard = false;
        DeploymentResult resumed = await Deployment().StartAsync(result.ActiveRelease!.ExecutablePath, VersionB);
        Assert.That(resumed.Disposition, Is.EqualTo(DeploymentDisposition.ContinueCurrent));
        Assert.That(File.Exists(JournalPath), Is.False);
    }

    [Test]
    public async Task FailedLinkRestoreLeavesBoundedRecoverableJournalAndMarkerA()
    {
        InstalledRelease a = await InstallA();
        links.FailSwitch = true;
        links.FailAfterReplacement = true;
        links.FailRestore = true;
        DeploymentResult failed = await Deployment().StartAsync(Source("B"), VersionB);
        Assert.That(failed.Disposition, Is.EqualTo(DeploymentDisposition.Failed));
        Assert.That(ActiveVersion(), Is.EqualTo("1.2.0"));
        Assert.That(File.Exists(JournalPath), Is.True);
        links.FailRestore = false;
        links.FailSwitch = false;
        DeploymentResult recovered = await Deployment().StartAsync(a.ExecutablePath, VersionA);
        Assert.That(recovered.Disposition, Is.EqualTo(DeploymentDisposition.ContinueCurrent));
        Assert.That(File.ReadAllText(links.LinkPath), Is.EqualTo(a.ExecutablePath));
        Assert.That(File.Exists(JournalPath), Is.False);
    }

    [Test]
    public async Task ForeignPathInInterruptedJournalPreventsAllRecoveryWrites()
    {
        await InstallA();
        links.InterruptSwitch = true;
        Assert.That(async () => await Deployment().StartAsync(Source("B"), VersionB), Throws.TypeOf<SimulatedTermination>());
        JsonNode journal = JsonNode.Parse(File.ReadAllText(JournalPath))!;
        journal["candidate"]!["executablePath"] = dataPath;
        File.WriteAllText(JournalPath, journal.ToJsonString());
        string beforeMarker = File.ReadAllText(MarkerPath);
        string beforeJournal = File.ReadAllText(JournalPath);
        DeploymentResult result = await Deployment().StartAsync(Source("retry"), VersionB);
        Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.Failed));
        Assert.That(File.ReadAllText(MarkerPath), Is.EqualTo(beforeMarker));
        Assert.That(File.ReadAllText(JournalPath), Is.EqualTo(beforeJournal));
    }

    [Test]
    public async Task TwoStartsSerializeAcrossSeparatePolicyObjects()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.BeforeReady = async _ => { entered.SetResult(); await release.Task; };
        string source = Source("A");
        Task<DeploymentResult> first = Deployment().StartAsync(source, VersionA);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<DeploymentResult> second = Deployment().StartAsync(source, VersionA);
        bool completedWhileFirstWaiting;
        try { completedWhileFirstWaiting = await Task.WhenAny(second, Task.Delay(150)) == second; }
        finally { release.TrySetResult(); }
        DeploymentResult[] results = await Task.WhenAll(first, second);
        Assert.That(completedWhileFirstWaiting, Is.False);
        Assert.That(results.Select(result => result.Disposition), Is.EqualTo(new[] {
            DeploymentDisposition.CandidateCommitted, DeploymentDisposition.RedirectToActive }));
        Assert.That(runtime.Started, Has.Count.EqualTo(1));
        TestContext.WriteLine("Two independent policy instances serialized; one candidate was started.");
    }

    [Test]
    public async Task SecondStartWaitsPastTheOperationBudgetForTheCurrentValidCandidate()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.BeforeReady = async _ => { entered.TrySetResult(); await release.Task; };
        LocalDeployment Policy() => new(installationRoot, runtime, links, TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(800), TimeSpan.FromMilliseconds(1200));
        string source = Source("B");
        Task<DeploymentResult> first = Policy().StartAsync(source, VersionB);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task<DeploymentResult> second = Policy().StartAsync(source, VersionB);
        bool secondFinished;
        try { secondFinished = await Task.WhenAny(second, Task.Delay(250)) == second; }
        finally { release.TrySetResult(); }
        DeploymentResult[] results = await Task.WhenAll(first, second);
        Assert.That(secondFinished, Is.False, "Eine laufende gültige Kandidatenphase darf nicht durch die kürzere Operationsfrist abgeschnitten werden.");
        Assert.That(results.Select(result => result.Disposition), Is.EqualTo(new[] {
            DeploymentDisposition.CandidateCommitted, DeploymentDisposition.RedirectToActive }));
        Assert.That(runtime.Started, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AdmissionWaitHasItsOwnBoundAndPreservesTheActiveRelease()
    {
        await InstallA();
        string marker = File.ReadAllText(MarkerPath);
        using var lease = new FileStream(Path.Combine(installationRoot, ".deployment.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var policy = new LocalDeployment(installationRoot, runtime, links, TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(800), TimeSpan.FromMilliseconds(250));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        DeploymentResult result = await policy.StartAsync(Source("B"), VersionB).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.Failed));
        Assert.That(timer.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(200)));
        Assert.That(File.ReadAllText(MarkerPath), Is.EqualTo(marker));
        Assert.That(runtime.Started, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task CancelledCandidateReturnsToAWithoutCommit()
    {
        InstalledRelease a = await InstallA();
        runtime.WaitForCancellation = true;
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        Assert.That(async () => await Deployment().StartAsync(Source("B"), VersionB, cancelled.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(ActiveVersion(), Is.EqualTo("1.2.0"));
        Assert.That(File.ReadAllText(links.LinkPath), Is.EqualTo(a.ExecutablePath));
        Assert.That(runtime.Committed, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task UnresponsiveCandidateHasBoundedTimeout()
    {
        await InstallA();
        runtime.WaitForCancellation = true;
        DeploymentResult result = await Deployment(TimeSpan.FromMilliseconds(100)).StartAsync(Source("B"), VersionB);
        Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.Failed));
        Assert.That(ActiveVersion(), Is.EqualTo("1.2.0"));
    }

    [Test]
    public async Task ColdCandidateMayUseAnExplicitLongerStartBudget()
    {
        Task startup = Task.Delay(400);
        runtime.BeforeReady = _ => startup;
        var deployment = new LocalDeployment(installationRoot, runtime, links,
            TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(800));
        try
        {
            DeploymentResult result = await deployment.StartAsync(Source("A"), VersionA);
            Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.CandidateCommitted));
        }
        finally { await startup; }
    }

    [Test]
    public async Task ALongerCandidateBudgetDoesNotChangeTheLinkOperationDeadline()
    {
        links.PrepareDelay = TimeSpan.FromMilliseconds(400);
        var deployment = new LocalDeployment(installationRoot, runtime, links,
            TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(800));
        DeploymentResult result = await deployment.StartAsync(Source("A"), VersionA);
        Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.Failed));
        Assert.That(runtime.Started, Is.Empty);
    }

    [Test]
    public async Task SuccessfulCandidateIsNotCancelledBeforeCommit()
    {
        DeploymentResult result = await Deployment().StartAsync(Source("A"), VersionA);
        Assert.That(result.Disposition, Is.EqualTo(DeploymentDisposition.CandidateCommitted));
        Assert.That(runtime.CandidateWasCancelledBeforeCommit, Is.False);
    }

    private sealed class SimulatedTermination : Exception;

    private sealed class ScriptedRuntime : IDeploymentRuntime
    {
        public List<CandidateStart> Started { get; } = new();
        public List<InstalledRelease> ExitRequests { get; } = new();
        public List<CandidateStart> Committed { get; } = new();
        public List<InstalledRelease> RunningQueries { get; } = new();
        public HashSet<string> RunningPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool FailRunningQuery { get; set; }
        public Func<CandidateStart, Task>? BeforeReady { get; set; }
        public Action<CandidateStart>? BeforeCommit { get; set; }
        public bool Healthy { get; set; } = true;
        public bool InterruptCommit { get; set; }
        public bool WaitForCancellation { get; set; }
        public string? RefuseExitPath { get; set; }
        public bool CandidateWasCancelledBeforeCommit { get; private set; }
        private CancellationToken candidateCancellation;

        public Task<bool> IsRunningAsync(InstalledRelease release, CancellationToken cancellationToken)
        {
            RunningQueries.Add(release);
            if (FailRunningQuery) throw new IOException("QA process query failure.");
            return Task.FromResult(RunningPaths.Contains(release.ExecutablePath));
        }

        public Task<bool> RequestExitAsync(InstalledRelease release, CancellationToken cancellationToken)
        {
            ExitRequests.Add(release);
            return Task.FromResult(release.ExecutablePath != RefuseExitPath);
        }

        public async Task<bool> StartCandidateAsync(CandidateStart candidate, CancellationToken cancellationToken)
        {
            candidateCancellation = cancellationToken;
            Started.Add(candidate);
            if (BeforeReady is not null) await BeforeReady(candidate);
            if (WaitForCancellation) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Healthy;
        }

        public Task CommitCandidateAsync(CandidateStart candidate, CancellationToken cancellationToken)
        {
            if (InterruptCommit) throw new SimulatedTermination();
            BeforeCommit?.Invoke(candidate);
            CandidateWasCancelledBeforeCommit = candidateCancellation.IsCancellationRequested;
            Committed.Add(candidate);
            return Task.CompletedTask;
        }
    }

    private sealed class ScriptedLinks(string directory) : IDeploymentLinks
    {
        public string LinkPath => Path.Combine(directory, "application.lnk");
        public bool FailSwitch { get; set; }
        public bool FailAfterReplacement { get; set; }
        public bool FailRestore { get; set; }
        public bool FailDiscard { get; set; }
        public bool InterruptPrepare { get; set; }
        public bool InterruptSwitch { get; set; }
        public TimeSpan PrepareDelay { get; set; }
        private string TemporaryPath(Guid operationId) => Path.Combine(directory, operationId.ToString("N") + ".tmp");

        public async Task PrepareAsync(InstalledRelease release, Guid operationId, CancellationToken cancellationToken)
        {
            if (InterruptPrepare) throw new SimulatedTermination();
            if (PrepareDelay > TimeSpan.Zero) await Task.Delay(PrepareDelay, cancellationToken);
            Directory.CreateDirectory(directory);
            File.WriteAllText(TemporaryPath(operationId), release.ExecutablePath);
        }

        public Task SwitchAsync(InstalledRelease release, Guid operationId, CancellationToken cancellationToken)
        {
            if (InterruptSwitch) throw new SimulatedTermination();
            if (FailSwitch && !FailAfterReplacement) throw new IOException("Link switch failed.");
            File.Move(TemporaryPath(operationId), LinkPath, overwrite: true);
            if (FailSwitch) throw new IOException("Link switch failed after replacement.");
            return Task.CompletedTask;
        }

        public Task RestoreAsync(InstalledRelease? release, Guid operationId, CancellationToken cancellationToken)
        {
            if (FailRestore) throw new IOException("Link restoration failed.");
            if (release is null) File.Delete(LinkPath);
            else File.WriteAllText(LinkPath, release.ExecutablePath);
            return Task.CompletedTask;
        }

        public Task DiscardPreparedAsync(Guid operationId, CancellationToken cancellationToken)
        {
            if (FailDiscard) throw new IOException("Temporary link removal failed.");
            File.Delete(TemporaryPath(operationId));
            return Task.CompletedTask;
        }
    }
}
