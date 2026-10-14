using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using CameraControl.AppCore.Deployment;
using CameraControl_WinUI.Deployment;
using NUnit.Framework;

namespace CameraControl.WinUI.Tests
{

[TestFixture, Category("DeploymentAdapters"), NonParallelizable]
public sealed class DeploymentAdapterTests
{
    private string root = null!;
    private DeploymentScope scope = null!;
    private readonly List<DeploymentPipeServer> servers = [];

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "CameraControl.Deployment.Adapters.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        scope = DeploymentScope.ForQa(root);
    }

    [TearDown]
    public async Task TearDown()
    {
        try { foreach (DeploymentPipeServer server in servers) await server.DisposeAsync(); }
        finally
        {
            servers.Clear();
            string allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CameraControl.Deployment.Adapters.Tests")) + Path.DirectorySeparatorChar;
            Assert.That(Path.GetFullPath(root).StartsWith(allowed, StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(Guid.TryParseExact(Path.GetFileName(root), "N", out _), Is.True);
            Assert.That(File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint), Is.False);
            Directory.Delete(root, recursive: true);
            Assert.That(Directory.Exists(root), Is.False);
            TestContext.WriteLine("Removed isolated native-adapter QA directory: " + root);
        }
    }

    private InstalledRelease Release(string version = "1.0.0")
    {
        string path = Path.Combine(scope.InstallationRoot, "Versions", version, LocalDeployment.ExecutableName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes("qa-executable-" + version);
        File.WriteAllBytes(path, bytes);
        return new InstalledRelease(ReleaseVersion.Parse(version), path, bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    private void Activate(InstalledRelease release) => File.WriteAllText(Path.Combine(scope.InstallationRoot, "active.json"),
        JsonSerializer.Serialize(new { schemaVersion = 1, release = new { version = release.Version.ToString(),
            executablePath = release.ExecutablePath, length = release.Length, sha256 = release.Sha256 } }));

    private DeploymentPipeServer Server(InstalledRelease release, CandidateLaunch? candidate = null,
        Func<CancellationToken, Task<bool>>? exit = null, Func<CancellationToken, Task>? commit = null,
        Func<CancellationToken, Task>? activate = null)
    {
        var server = new DeploymentPipeServer(scope, release, exit ?? (_ => Task.FromResult(true)),
            commit ?? (_ => Task.CompletedTask), candidate, release.ExecutablePath, TimeSpan.FromSeconds(2), activate);
        servers.Add(server);
        return server;
    }

    private Task<DeploymentMessage> Send(InstalledRelease release, string command, Guid token, string? path = null) =>
        DeploymentPipeProtocol.SendAsync(scope.PipeName(release), new DeploymentMessage(command, path ?? release.ExecutablePath, token),
            Environment.ProcessId, release.ExecutablePath, _ => release.ExecutablePath, TimeSpan.FromSeconds(2), CancellationToken.None);

    [Test]
    public void QaScopeUsesDistinctDirectoriesAndIdentity()
    {
        Assert.That(scope.InstallationRoot.StartsWith(root, StringComparison.OrdinalIgnoreCase), Is.True);
        Assert.That(scope.ShortcutsDirectory.StartsWith(root, StringComparison.OrdinalIgnoreCase), Is.True);
        Assert.That(scope.AppUserModelId, Does.StartWith("Versammlungsptzsteuerung.QA."));
        Assert.That(scope.ShortcutsDirectory, Is.Not.EqualTo(Environment.GetFolderPath(Environment.SpecialFolder.Programs)));
    }

    [Test]
    public async Task RealShellLinkPreservesTargetArgumentsAndAumidAcrossSwitchAndRestore()
    {
        InstalledRelease a = Release();
        InstalledRelease b = Release("2.0.0");
        var links = new WindowsDeploymentLinks(scope);
        Guid first = Guid.NewGuid();
        await links.PrepareAsync(a, first, default);
        Assert.That(File.Exists(scope.LinkPath), Is.False);
        await links.SwitchAsync(a, first, default);
        ShellLinkInfo linkA = await WindowsDeploymentLinks.InspectAsync(scope.LinkPath);
        Assert.That(linkA.TargetPath, Is.EqualTo(a.ExecutablePath));
        Assert.That(linkA.AppUserModelId, Is.EqualTo(scope.AppUserModelId));
        Assert.That(linkA.Arguments, Does.Contain(root));
        Guid second = Guid.NewGuid();
        await links.PrepareAsync(b, second, default);
        await links.SwitchAsync(b, second, default);
        Assert.That((await WindowsDeploymentLinks.InspectAsync(scope.LinkPath)).TargetPath, Is.EqualTo(b.ExecutablePath));
        await links.RestoreAsync(a, second, default);
        Assert.That((await WindowsDeploymentLinks.InspectAsync(scope.LinkPath)).TargetPath, Is.EqualTo(a.ExecutablePath));
        await links.DiscardPreparedAsync(second, default);
        TestContext.WriteLine("Created, read, replaced and restored a real COM Shell link in the QA directory.");
    }

    [Test]
    public async Task FirstInstallRollbackRemovesOnlyTheOwnedLink()
    {
        var links = new WindowsDeploymentLinks(scope);
        InstalledRelease a = Release();
        Guid operation = Guid.NewGuid();
        await links.PrepareAsync(a, operation, default);
        await links.SwitchAsync(a, operation, default);
        string sentinel = Path.Combine(scope.ShortcutsDirectory, "foreign.txt");
        File.WriteAllText(sentinel, "keep");
        await links.RestoreAsync(null, operation, default);
        Assert.That(File.Exists(scope.LinkPath), Is.False);
        Assert.That(File.ReadAllText(sentinel), Is.EqualTo("keep"));
    }

    [Test]
    public void ShellAdapterRejectsForeignReleasePathAndEmptyOperationId()
    {
        InstalledRelease a = Release();
        var links = new WindowsDeploymentLinks(scope);
        Assert.That(async () => await links.PrepareAsync(a with { ExecutablePath = Path.Combine(root, "foreign.exe") }, Guid.NewGuid(), default),
            Throws.InstanceOf<IOException>());
        Assert.That(async () => await links.PrepareAsync(a, Guid.Empty, default), Throws.InstanceOf<IOException>());
        Assert.That(File.Exists(scope.LinkPath), Is.False);
    }

    [Test]
    public void InvalidExistingLinkIsNotOverwrittenOrDeleted()
    {
        Directory.CreateDirectory(scope.ShortcutsDirectory);
        File.WriteAllText(scope.LinkPath, "foreign-content");
        var links = new WindowsDeploymentLinks(scope);
        Assert.That(async () => await links.RestoreAsync(null, Guid.NewGuid(), default), Throws.InstanceOf<IOException>());
        Assert.That(File.ReadAllText(scope.LinkPath), Is.EqualTo("foreign-content"));
    }

    [Test]
    public async Task CandidateArgumentsUseHostPathAndRejectExtractionPath()
    {
        InstalledRelease release = Release();
        var request = new CandidateStart(release, Guid.NewGuid());
        string[] arguments = CandidateLaunch.Arguments(request, Environment.ProcessId, scope).ToArray();
        CandidateLaunch? parsed = await CandidateLaunch.ParseAsync(arguments, scope, release.ExecutablePath);
        Assert.That(parsed!.Start.Token, Is.EqualTo(request.Token));
        Assert.That(parsed.Start.Release, Is.EqualTo(release));
        Assert.That(parsed.ParentProcessId, Is.EqualTo(Environment.ProcessId));
        Assert.That(async () => await CandidateLaunch.ParseAsync(arguments, scope, Path.Combine(root, "extract", "managed.dll")),
            Throws.InstanceOf<IOException>());
        Assert.That(await CandidateLaunch.ParseAsync([], scope, release.ExecutablePath), Is.Null);
    }

    [TestCase("bad-token")]
    [TestCase("duplicate")]
    [TestCase("old-parent")]
    public void MalformedCandidateArgumentsAreRejected(string fault)
    {
        InstalledRelease release = Release();
        var args = CandidateLaunch.Arguments(new CandidateStart(release, Guid.NewGuid()), Environment.ProcessId, scope).ToList();
        if (fault == "bad-token") args[1] = "not-a-guid";
        if (fault == "duplicate") args.AddRange(["--deployment-candidate", Guid.NewGuid().ToString("N")]);
        if (fault == "old-parent") args[5] = "0";
        Assert.That(async () => await CandidateLaunch.ParseAsync(args.ToArray(), scope, release.ExecutablePath), Throws.InstanceOf<IOException>());
    }

    [Test]
    public async Task CandidateRemainsReadOnlyUntilReadyMarkerAndCommit()
    {
        InstalledRelease release = Release();
        var request = new CandidateStart(release, Guid.NewGuid());
        int commits = 0;
        DeploymentPipeServer server = Server(release, new CandidateLaunch(request, Environment.ProcessId),
            commit: _ => { commits++; return Task.CompletedTask; });
        Assert.That(server.IsReadOnly, Is.True);
        Assert.That((await Send(release, "Activate", Guid.Empty)).Command, Is.EqualTo("Starting"));
        Assert.That((await Send(release, "Ready", request.Token)).Command, Is.EqualTo("Starting"));
        Assert.That((await Send(release, "Commit", request.Token)).Command, Is.EqualTo("Rejected"));
        server.MarkShellReady();
        Assert.That((await Send(release, "Ready", request.Token)).Command, Is.EqualTo("Ready"));
        Assert.That((await Send(release, "Commit", request.Token)).Command, Is.EqualTo("Rejected"));
        Activate(release);
        Assert.That((await Send(release, "Commit", request.Token)).Command, Is.EqualTo("Committed"));
        Assert.That(server.IsReadOnly, Is.False);
        Assert.That((await Send(release, "Commit", request.Token)).Command, Is.EqualTo("Committed"));
        Assert.That(commits, Is.EqualTo(1));
    }

    [TestCase("token")]
    [TestCase("path")]
    [TestCase("parent")]
    public async Task WrongCandidateIdentityCannotEnableWrites(string fault)
    {
        InstalledRelease release = Release();
        var request = new CandidateStart(release, Guid.NewGuid());
        int commits = 0;
        DeploymentPipeServer server = Server(release, new CandidateLaunch(request, fault == "parent" ? int.MaxValue : Environment.ProcessId),
            commit: _ => { commits++; return Task.CompletedTask; });
        server.MarkShellReady();
        Activate(release);
        DeploymentMessage response = await Send(release, "Commit", fault == "token" ? Guid.NewGuid() : request.Token,
            fault == "path" ? Path.Combine(root, "foreign.exe") : null);
        Assert.That(response.Command, Is.EqualTo("Rejected"));
        Assert.That(server.IsReadOnly, Is.True);
        Assert.That(commits, Is.Zero);
    }

    [Test]
    public async Task PipeExitReportsRefusalAndThenGracefulAcceptance()
    {
        InstalledRelease release = Release();
        bool allowed = false;
        Server(release, exit: _ => Task.FromResult(allowed));
        Assert.That((await Send(release, "Exit", Guid.Empty)).Command, Is.EqualTo("Refused"));
        allowed = true;
        Assert.That((await Send(release, "Exit", Guid.Empty)).Command, Is.EqualTo("Exiting"));
    }

    [Test]
    public void OnlyOnePipeServerCanOwnAReleaseEndpoint()
    {
        InstalledRelease release = Release();
        Server(release);
        Assert.That(() => Server(release), Throws.InstanceOf<IOException>());
    }

    [Test]
    public async Task ClientCloseBeforeAcceptResetsTheSamePipeAndKeepsItsExclusiveClaim()
    {
        InstalledRelease release = Release();
        string name = scope.PipeName(release);
        using var pipe = new System.IO.Pipes.NamedPipeServerStream(name, System.IO.Pipes.PipeDirection.InOut, 1,
            System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous |
            System.IO.Pipes.PipeOptions.CurrentUserOnly | System.IO.Pipes.PipeOptions.FirstPipeInstance);
        using (var abandoned = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.InOut,
            System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly))
            await abandoned.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(2));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var originalHandle = pipe.SafePipeHandle.DangerousGetHandle();
        Assert.That(await DeploymentPipeServer.AcceptConnectionAsync(pipe, deadline.Token), Is.False);
        Assert.That(pipe.IsConnected, Is.False);
        var claimError = Assert.Throws<IOException>(() => new System.IO.Pipes.NamedPipeServerStream(name, System.IO.Pipes.PipeDirection.InOut, 1,
            System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.FirstPipeInstance),
            "The original endpoint must reject a second server instance.");
        Assert.That(claimError!.HResult & 0xffff, Is.EqualTo(231));
        Task<bool> accepted = DeploymentPipeServer.AcceptConnectionAsync(pipe, deadline.Token);
        Task<DeploymentMessage> reply = Send(release, "Ready", Guid.Empty);
        Assert.That(await accepted, Is.True);
        DeploymentMessage request = await DeploymentPipeProtocol.ReadAsync(pipe, deadline.Token);
        Assert.That(request.Command, Is.EqualTo("Ready"));
        await DeploymentPipeProtocol.WriteAsync(pipe, new DeploymentMessage("Ready", release.ExecutablePath, request.Token), deadline.Token);
        Assert.That((await reply).Command, Is.EqualTo("Ready"));
        Assert.That(pipe.SafePipeHandle.DangerousGetHandle(), Is.EqualTo(originalHandle));
        TestContext.WriteLine("A real client disconnected before Accept. The original server handle kept its exclusive name and served the next real request.");
    }

    [Test]
    public async Task RuntimeLaunchWaitsForResponsiveShellAndCommitsThroughRealPipe()
    {
        InstalledRelease release = Release();
        var request = new CandidateStart(release, Guid.NewGuid());
        ProcessStartInfo? observedStart = null;
        DeploymentPipeServer? server = null;
        var processes = new DeploymentProcessAccess(_ => [], _ => release.ExecutablePath,
            start => { observedStart = start; server = Server(release, new CandidateLaunch(request, Environment.ProcessId)); return Environment.ProcessId; },
            (_, _) => Task.CompletedTask, Environment.ProcessId);
        var runtime = new WindowsDeploymentRuntime(scope, processes, TimeSpan.FromSeconds(3));
        Task<bool> started = runtime.StartCandidateAsync(request, default);
        await Task.Delay(100);
        Assert.That(started.IsCompleted, Is.False);
        Assert.That(observedStart!.FileName, Is.EqualTo(release.ExecutablePath));
        Assert.That(observedStart.UseShellExecute, Is.False);
        Assert.That(observedStart.ArgumentList, Does.Contain(request.Token.ToString("N")));
        server!.MarkShellReady();
        Assert.That(await started, Is.True);
        Activate(release);
        await runtime.CommitCandidateAsync(request, default);
        Assert.That(server.IsReadOnly, Is.False);
        TestContext.WriteLine("Real current-user pipe completed Ready/Commit; Process.Start was substituted and no app was launched.");
    }

    [Test]
    public async Task RetirementQueryIncludesTheCurrentProcessAndRejectsForeignPaths()
    {
        InstalledRelease release = Release();
        var processes = new DeploymentProcessAccess(_ => [Environment.ProcessId], _ => release.ExecutablePath,
            _ => throw new AssertionException("No process may start."), (_, _) => throw new AssertionException("No process may close."), Environment.ProcessId);
        var runtime = new WindowsDeploymentRuntime(scope, processes);
        Assert.That(await runtime.IsRunningAsync(release, default), Is.True);
        Assert.That(async () => await runtime.IsRunningAsync(release with { ExecutablePath = Path.Combine(root, "foreign.exe") }, default),
            Throws.InstanceOf<IOException>());
    }

    [Test]
    public async Task RuntimeDoesNotCloseItsOwnProcess()
    {
        InstalledRelease release = Release();
        int waits = 0;
        var processes = new DeploymentProcessAccess(_ => [Environment.ProcessId], _ => release.ExecutablePath,
            _ => throw new AssertionException("No process may start."), (_, _) => { waits++; return Task.CompletedTask; }, Environment.ProcessId);
        Assert.That(await new WindowsDeploymentRuntime(scope, processes).RequestExitAsync(release, default), Is.True);
        Assert.That(waits, Is.Zero);
    }

    [Test]
    public async Task RuntimeRequestsGracefulExitAndWaitsForActualProcessExit()
    {
        InstalledRelease release = Release();
        bool requested = false;
        bool waited = false;
        Server(release, exit: _ => { requested = true; return Task.FromResult(true); });
        var processes = new DeploymentProcessAccess(_ => [Environment.ProcessId], _ => release.ExecutablePath,
            _ => throw new AssertionException("No process may start."), (_, _) => { waited = true; return Task.CompletedTask; }, int.MaxValue);
        Assert.That(await new WindowsDeploymentRuntime(scope, processes).RequestExitAsync(release, default), Is.True);
        Assert.That(requested && waited, Is.True);
    }

    [Test]
    public async Task CandidateColdStartHasItsOwnBoundBeforeTheResponsiveShellDeadline()
    {
        InstalledRelease release = Release();
        var request = new CandidateStart(release, Guid.NewGuid());
        Task? launchPhase = null;
        var processes = new DeploymentProcessAccess(_ => [], _ => release.ExecutablePath,
            _ =>
            {
                launchPhase = Task.Run(async () =>
                {
                    await Task.Delay(700);
                    DeploymentPipeServer server = Server(release, new CandidateLaunch(request, Environment.ProcessId));
                    await Task.Delay(100);
                    server.MarkShellReady();
                });
                return Environment.ProcessId;
            }, (_, _) => Task.CompletedTask, Environment.ProcessId);
        var runtime = new WindowsDeploymentRuntime(scope, processes, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1500));
        try { Assert.That(await runtime.StartCandidateAsync(request, default), Is.True); }
        finally { if (launchPhase is not null) await launchPhase; }
    }

    [Test]
    public async Task RuntimeTimeoutDoesNotReportCandidateReadyOrKillIt()
    {
        InstalledRelease release = Release();
        var request = new CandidateStart(release, Guid.NewGuid());
        var processes = new DeploymentProcessAccess(_ => [], _ => release.ExecutablePath,
            _ => { Server(release, new CandidateLaunch(request, Environment.ProcessId)); return Environment.ProcessId; },
            (_, _) => Task.CompletedTask, Environment.ProcessId);
        var runtime = new WindowsDeploymentRuntime(scope, processes, TimeSpan.FromMilliseconds(200));
        Assert.That(async () => await runtime.StartCandidateAsync(request, default), Throws.InstanceOf<TimeoutException>());
        Assert.That((await Send(release, "Ready", request.Token)).Command, Is.EqualTo("Starting"));
    }

    [Test]
    public void PeerProcessMismatchIsRejected()
    {
        InstalledRelease release = Release();
        Server(release);
        Assert.That(async () => await DeploymentPipeProtocol.SendAsync(scope.PipeName(release),
            new DeploymentMessage("Exit", release.ExecutablePath, Guid.Empty), int.MaxValue, release.ExecutablePath,
            _ => release.ExecutablePath, TimeSpan.FromSeconds(2), default), Throws.InstanceOf<IOException>());
    }

    [Test]
    public async Task SecondStartActivatesTheExistingWriterWithoutLaunchingAnotherProcess()
    {
        InstalledRelease release = Release();
        int activations = 0;
        DeploymentPipeServer server = Server(release, activate: _ => { activations++; return Task.CompletedTask; });
        server.MarkShellReady();
        var processes = new DeploymentProcessAccess(_ => [Environment.ProcessId], _ => release.ExecutablePath,
            _ => throw new AssertionException("A second writer must not start."), (_, _) => Task.CompletedTask, int.MaxValue);
        Assert.That(await new WindowsDeploymentRuntime(scope, processes).ActivateExistingAsync(release), Is.True);
        Assert.That(activations, Is.EqualTo(1));
    }

    [Test]
    public void ChangedCandidateIsRejectedBeforeProcessStart()
    {
        InstalledRelease release = Release();
        File.AppendAllText(release.ExecutablePath, "changed");
        var processes = new DeploymentProcessAccess(_ => [], _ => release.ExecutablePath,
            _ => throw new AssertionException("A changed candidate must not start."), (_, _) => Task.CompletedTask, Environment.ProcessId);
        Assert.That(async () => await new WindowsDeploymentRuntime(scope, processes).StartCandidateAsync(
            new CandidateStart(release, Guid.NewGuid()), default), Throws.InstanceOf<IOException>());
    }

    [Test]
    public async Task CallerCancellationLeavesAnUncommittedCandidateReadOnly()
    {
        InstalledRelease release = Release();
        var request = new CandidateStart(release, Guid.NewGuid());
        var processes = new DeploymentProcessAccess(_ => [], _ => release.ExecutablePath,
            _ => { Server(release, new CandidateLaunch(request, Environment.ProcessId)); return Environment.ProcessId; },
            (_, _) => Task.CompletedTask, Environment.ProcessId);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        Assert.That(async () => await new WindowsDeploymentRuntime(scope, processes).StartCandidateAsync(request, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That((await Send(release, "Ready", request.Token)).Command, Is.EqualTo("Starting"));
    }

    [TestCase("Commit", false)]
    [TestCase("Commit", true)]
    [TestCase("Activate", false)]
    [TestCase("Activate", true)]
    [TestCase("Exit", false)]
    [TestCase("Exit", true)]
    public async Task ExpectedUiCallbackFailureDoesNotStopThePipeServer(string command, bool denied)
    {
        InstalledRelease release = Release();
        var request = new CandidateStart(release, Guid.NewGuid());
        bool failExit = command == "Exit";
        Exception Failure() => denied ? new UnauthorizedAccessException("QA denied.") : new InvalidOperationException("QA window unavailable.");
        DeploymentPipeServer server = Server(release, command == "Commit" ? new CandidateLaunch(request, Environment.ProcessId) : null,
            exit: _ => { if (failExit) { failExit = false; throw Failure(); } return Task.FromResult(true); },
            commit: _ => throw Failure(), activate: _ => throw Failure());
        server.MarkShellReady();
        Activate(release);
        DeploymentMessage failed = await Send(release, command, command == "Commit" ? request.Token : Guid.Empty);
        Assert.That(failed.Command, Is.EqualTo(command == "Commit" ? "Failed" : "Refused"));
        if (command == "Commit")
        {
            Assert.That(server.IsReadOnly, Is.True);
            Assert.That((await Send(release, "Ready", request.Token)).Command, Is.EqualTo("Ready"));
        }
        Assert.That((await Send(release, "Exit", Guid.Empty)).Command, Is.EqualTo("Exiting"));
    }
    [TestCase(true)]
    [TestCase(false)]
    public async Task RuntimeWaitsForActualExitWhenWindowClosesBeforeThePipeResponse(bool exits)
    {
        InstalledRelease release = Release();
        using var pipe = new System.IO.Pipes.NamedPipeServerStream(scope.PipeName(release), System.IO.Pipes.PipeDirection.InOut,
            1, System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
        async Task CloseWithoutResponse()
        {
            await pipe.WaitForConnectionAsync();
            Assert.That((await DeploymentPipeProtocol.ReadAsync(pipe, default)).Command, Is.EqualTo("Exit"));
            pipe.Disconnect();
        }
        Task closure = CloseWithoutResponse();
        bool waited = false;
        var processes = new DeploymentProcessAccess(_ => [Environment.ProcessId], _ => release.ExecutablePath,
            _ => throw new AssertionException("No process may start."), async (_, token) =>
            {
                waited = true;
                if (!exits) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }, int.MaxValue);
        bool result = await new WindowsDeploymentRuntime(scope, processes, TimeSpan.FromMilliseconds(250)).RequestExitAsync(release, default);
        await closure;
        Assert.That(waited, Is.True);
        Assert.That(result, Is.EqualTo(exits));
    }

    [Test]
    public async Task ConcurrentDuplicateProcessesActivateOnlyTheVerifiedPipeOwner()
    {
        InstalledRelease release = Release();
        int activations = 0;
        DeploymentPipeServer server = Server(release, activate: _ => { activations++; return Task.CompletedTask; });
        server.MarkShellReady();
        var processes = new DeploymentProcessAccess(_ => [Environment.ProcessId, int.MaxValue - 1], _ => release.ExecutablePath,
            _ => throw new AssertionException("A duplicate must not start another process."), (_, _) => Task.CompletedTask, int.MaxValue);
        Assert.That(await new WindowsDeploymentRuntime(scope, processes).ActivateExistingAsync(release), Is.True);
        Assert.That(activations, Is.EqualTo(1));
    }

    [Test]
    public async Task ActivateOrStartUsesTheOwnedReleaseAndExplicitQaArguments()
    {
        InstalledRelease release = Release();
        bool running = false;
        ProcessStartInfo? observed = null;
        var processes = new DeploymentProcessAccess(_ => running ? [Environment.ProcessId] : [], _ => release.ExecutablePath,
            start =>
            {
                observed = start;
                running = true;
                Server(release, activate: _ => Task.CompletedTask).MarkShellReady();
                return Environment.ProcessId;
            }, (_, _) => Task.CompletedTask, int.MaxValue);
        await new WindowsDeploymentRuntime(scope, processes).ActivateOrStartAsync(release);
        Assert.That(observed!.FileName, Is.EqualTo(release.ExecutablePath));
        Assert.That(observed.UseShellExecute, Is.False);
        Assert.That(observed.ArgumentList.ToArray(), Is.EqualTo(new[] { "--deployment-qa-root", root }));
    }
    [Test]
    public async Task AbandonedPartialRequestDoesNotBlockTheNextReadyRequest()
    {
        InstalledRelease release = Release();
        var start = new CandidateStart(release, Guid.NewGuid());
        DeploymentPipeServer server = Server(release, new CandidateLaunch(start, Environment.ProcessId));
        server.MarkShellReady();
        using (var client = new System.IO.Pipes.NamedPipeClientStream(".", scope.PipeName(release),
            System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly))
        {
            await client.ConnectAsync(2000);
            await client.WriteAsync(new byte[] { 100, 0, 0, 0, 123 });
        }
        await Task.Delay(100);
        Assert.That((await Send(release, "Ready", start.Token)).Command, Is.EqualTo("Ready"));
        Assert.That((await Send(release, "Exit", Guid.Empty)).Command, Is.EqualTo("Exiting"));
    }
}
}

