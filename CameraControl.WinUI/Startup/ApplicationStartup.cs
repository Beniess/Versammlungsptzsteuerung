using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using CameraControl.AppCore.Deployment;
using CameraControl.AppCore.Storage;
using CameraControl_WinUI.Deployment;
using CameraControl_WinUI.Video;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;
using Windows.Graphics;

namespace CameraControl_WinUI.Startup;

internal sealed class ApplicationStartup
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly TaskCompletionSource<MainWindow> shell = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource committed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ApplicationServices? services;
    private MainWindow? window;
    private Window? errorWindow;
    private DeploymentPipeServer? pipe;
    private AppInstance? directInstance;
    private StartupDiagnostics? diagnostics;
    private StartupOptions? options;
    private nint windowHandle;

    internal async Task StartAsync(string[] arguments)
    {
#if LOCAL_DEPLOYMENT_RELEASE
        const bool deploymentRelease = true;
#else
        const bool deploymentRelease = false;
#endif
        try
        {
            string processPath = Environment.ProcessPath ?? throw new IOException("Der Host-Prozesspfad ist nicht verfügbar.");
            options = StartupOptions.Read(arguments, deploymentRelease, processPath, AppContext.BaseDirectory, Environment.GetEnvironmentVariable);
            InstalledRelease host = await StartupHost.ReadAsync(processPath, lifetime.Token);
            diagnostics = new StartupDiagnostics(options, host);
            diagnostics.Record("Started", true);
            if (options.Kind != StartupKind.Deployment)
            {
                await StartDirectAsync(options);
                return;
            }
            DeploymentScope scope = options.Scope!;
            StartupHost.SetAppIdentity(scope.AppUserModelId);
            var runtime = new WindowsDeploymentRuntime(scope, options.IsQa ? diagnostics.RecordEvent : null);
            CandidateLaunch? candidate = await CandidateLaunch.ParseAsync(arguments, scope, processPath, lifetime.Token);
            if (candidate is not null)
            {
                if (candidate.Start.Release != host || candidate.ParentProcessId == Environment.ProcessId)
                    throw new IOException("Die gestartete Host-EXE passt nicht zur Kandidatenanfrage.");
                await StartInstalledAsync(scope, host, runtime, candidate);
                return;
            }
            IDeploymentRuntime deploymentRuntime = options.Fault == DeploymentQaFault.CancelAfterReady
                ? new CancelAfterReadyRuntime(runtime, lifetime) : runtime;
            var deployment = new LocalDeployment(scope.InstallationRoot, deploymentRuntime, new WindowsDeploymentLinks(scope),
                candidateStartTimeout: TimeSpan.FromMinutes(2), admissionTimeout: TimeSpan.FromMinutes(5));
            diagnostics.RecordEvent("admission.wait;seconds=300");
            DeploymentResult result = await deployment.StartAsync(processPath, host.Version, lifetime.Token,
                admissionAcquired: () => diagnostics.RecordEvent("admission.acquired"));
            switch (result.Disposition)
            {
                case DeploymentDisposition.ContinueCurrent:
                    await StartInstalledAsync(scope, result.ActiveRelease!, runtime, null);
                    break;
                case DeploymentDisposition.RedirectToActive:
                    await runtime.ActivateOrStartAsync(result.ActiveRelease!, lifetime.Token);
                    diagnostics.Record("Redirected", true);
                    Application.Current.Exit();
                    break;
                case DeploymentDisposition.CandidateCommitted:
                    diagnostics.Record("SourceCompleted", true, error: result.Error);
                    Application.Current.Exit();
                    break;
                case DeploymentDisposition.Failed:
                    throw new IOException(result.Error ?? "Die lokale Programmeinrichtung ist fehlgeschlagen.");
                default:
                    throw new InvalidOperationException("Das Startergebnis ist nicht bekannt.");
            }
        }
        catch (Exception error) when (IsStartFailure(error))
        {
            diagnostics?.Record("Failed", services?.IsReadOnly ?? true, Handle, error.Message);
            if (options?.IsCandidate == true || arguments.Contains("--deployment-candidate", StringComparer.Ordinal))
                await CloseCandidateAsync();
            else
            {
                ShowFailure(error is OperationCanceledException ? "Die Programmeinrichtung wurde abgebrochen." : error.Message);
                if (window is not null) await CloseCandidateAsync();
            }
        }
    }

    private async Task StartDirectAsync(StartupOptions selected)
    {
        string key = "Versammlungsptzsteuerung.QA." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(selected.DataDirectory.ToUpperInvariant())))[..24];
        StartupHost.SetAppIdentity(key);
        directInstance = AppInstance.FindOrRegisterForKey(key);
        if (!directInstance.IsCurrent)
        {
            async Task RedirectAsync() => await directInstance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs());
            await RedirectAsync().WaitAsync(TimeSpan.FromSeconds(30), lifetime.Token);
            Application.Current.Exit();
            return;
        }
        directInstance.Activated += OnDirectActivation;
        await CreateWindowAsync(selected);
        await CommitServicesAsync(lifetime.Token);
    }

    private async void OnDirectActivation(object? sender, AppActivationArguments args)
    {
        try { await (await shell.Task.WaitAsync(lifetime.Token)).ActivateForDeploymentAsync(lifetime.Token); }
        catch (Exception error) when (IsStartFailure(error)) { Debug.WriteLine(error.Message); }
    }

    private async Task StartInstalledAsync(DeploymentScope scope, InstalledRelease release, WindowsDeploymentRuntime runtime, CandidateLaunch? candidate)
    {
        try
        {
            pipe = new DeploymentPipeServer(scope, release, RequestExitAsync, CommitServicesAsync, candidate,
                async token => await (await shell.Task.WaitAsync(token)).ActivateForDeploymentAsync(token),
                options!.IsQa ? diagnostics!.RecordEvent : null);
        }
        catch (IOException) when (candidate is null)
        {
            if (!await runtime.ActivateExistingAsync(release, lifetime.Token)) throw;
            diagnostics!.Record("Redirected", true);
            Application.Current.Exit();
            return;
        }
        await CreateWindowAsync(options!);
        if (candidate is null)
        {
            if (!await scope.IsActiveAsync(release, lifetime.Token)) throw new IOException("Die lokale Version ist nicht mehr aktiv.");
            await CommitServicesAsync(lifetime.Token);
            pipe.MarkShellReady();
            return;
        }
        if (options!.Fault == DeploymentQaFault.FailBeforeReady) throw new IOException("QA: Der Kandidatenstart wurde vor Ready abgebrochen.");
        pipe.MarkShellReady();
        diagnostics!.Record("Ready", true, Handle);
        _ = WatchCandidateAsync(candidate);
    }

    private async Task CreateWindowAsync(StartupOptions selected)
    {
        services = await ApplicationServices.CreateAsync(selected.DataDirectory, readOnly: true, isQa: selected.IsQa, cancellationToken: lifetime.Token);
        window = new MainWindow(services);
        windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        window.Closed += OnClosed;
        PreviewCoordinator.Attach(services, window);
        window.Activate();
        await window.WaitForResponsiveReadyAsync(lifetime.Token).WaitAsync(TimeSpan.FromSeconds(30), lifetime.Token);
        shell.TrySetResult(window);
    }

    private async Task CommitServicesAsync(CancellationToken cancellationToken)
    {
        MainWindow ready = await shell.Task.WaitAsync(cancellationToken);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!ready.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await services!.ActivateCommittedAsync(cancellationToken);
                diagnostics?.Record("Committed", false, Handle);
                committed.TrySetResult();
                completion.TrySetResult();
            }
            catch (Exception error) { completion.TrySetException(error); }
        })) throw new InvalidOperationException("Der Fenster-Dispatcher ist nicht verfügbar.");
        await completion.Task.WaitAsync(cancellationToken);
    }

    private async Task<bool> RequestExitAsync(CancellationToken cancellationToken)
    {
        if (options!.Fault == DeploymentQaFault.RefuseExit && committed.Task.IsCompletedSuccessfully) return false;
        return await (await shell.Task.WaitAsync(cancellationToken)).CloseForDeploymentAsync(cancellationToken);
    }

    private async Task WatchCandidateAsync(CandidateLaunch candidate)
    {
        using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        try
        {
            Task parentExit = DeploymentProcessAccess.System.WaitForExit(candidate.ParentProcessId, watchdog.Token);
            await Task.WhenAny(committed.Task, parentExit, Task.Delay(TimeSpan.FromSeconds(45), watchdog.Token));
            if (committed.Task.IsCompletedSuccessfully || lifetime.IsCancellationRequested) return;
            diagnostics?.RecordEvent($"watchdog.expired;parentExited={parentExit.IsCompleted};committed={committed.Task.IsCompletedSuccessfully}");
            diagnostics?.Record("CandidateExpired", true, Handle);
            await CloseCandidateAsync();
        }
        catch (Exception error) when (IsStartFailure(error))
        {
            if (!lifetime.IsCancellationRequested && !committed.Task.IsCompletedSuccessfully)
            {
                diagnostics?.Record("CandidateExpired", true, Handle, error.Message);
                await CloseCandidateAsync();
            }
        }
        finally { await watchdog.CancelAsync(); }
    }

    private async Task CloseCandidateAsync()
    {
        if (window is null) { Application.Current.Exit(); return; }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await window.CloseForDeploymentAsync(deadline.Token);
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        diagnostics?.Record("Closed", services?.IsReadOnly ?? true, Handle);
        await lifetime.CancelAsync();
        if (directInstance?.IsCurrent == true)
        {
            directInstance.Activated -= OnDirectActivation;
            directInstance.UnregisterKey();
        }
        if (pipe is not null) await pipe.DisposeAsync();
    }

    private nint Handle => windowHandle;

    private void ShowFailure(string message)
    {
        errorWindow = new Window { Title = "Versammlungs-Kamerasteuerung" };
        var panel = new StackPanel { Spacing = 16, Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Das Programm konnte nicht gestartet werden.", FontSize = 20, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        var close = new Button { Content = "Schließen" };
        close.Click += (_, _) => errorWindow.Close();
        panel.Children.Add(close);
        errorWindow.Content = panel;
        errorWindow.AppWindow.Resize(new SizeInt32(520, 280));
        errorWindow.Activate();
    }

    private static bool IsStartFailure(Exception error) => error is IOException or UnauthorizedAccessException or InvalidOperationException or SettingsStorageException
        or TimeoutException or OperationCanceledException or COMException or Win32Exception;

    private sealed class CancelAfterReadyRuntime(WindowsDeploymentRuntime inner, CancellationTokenSource cancellation) : IDeploymentRuntime
    {
        public Task<bool> IsRunningAsync(InstalledRelease release, CancellationToken token) => inner.IsRunningAsync(release, token);
        public Task<bool> RequestExitAsync(InstalledRelease release, CancellationToken token) => inner.RequestExitAsync(release, token);
        public async Task<bool> StartCandidateAsync(CandidateStart candidate, CancellationToken token)
        {
            bool ready = await inner.StartCandidateAsync(candidate, token);
            if (ready) await cancellation.CancelAsync();
            return ready;
        }
        public Task CommitCandidateAsync(CandidateStart candidate, CancellationToken token) => inner.CommitCandidateAsync(candidate, token);
    }
}
