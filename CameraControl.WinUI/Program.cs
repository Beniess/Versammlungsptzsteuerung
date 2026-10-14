using CameraControl_WinUI.Video;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System.Runtime.CompilerServices;

namespace CameraControl_WinUI;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var probe = VideoProbeOptions.Read(Environment.GetCommandLineArgs());
        void Stage(string message)
        {
            if (probe is null) return;
            Directory.CreateDirectory(probe.OutputDirectory);
            File.AppendAllText(Path.Combine(probe.OutputDirectory, "startup-stage.txt"), message + "\n");
        }
        void Failure(Exception error)
        {
            if (probe is not null) File.WriteAllText(Path.Combine(probe.OutputDirectory, "startup-exception.txt"), error.ToString());
        }
        Stage("Program Main");
        if (!NativeVideoBundle.TryLocate(out var nativeDirectory))
        {
            Stage("Native bundle missing before WinRT");
            NativeVideoFailureDialog.Show(nativeDirectory, probe);
            Environment.ExitCode = 3;
            return;
        }
        Stage("Native bundle present");
        RunWinUi(Stage, Failure);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunWinUi(Action<string> stage, Action<Exception> failure)
    {
        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            stage("ComWrappers initialized");
            Application.Start(initialization =>
            {
                stage("Application.Start callback");
                try
                {
                    SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                    stage("Before new App");
                    _ = new App();
                    stage("App constructed");
                }
                catch (Exception error) { failure(error); throw; }
            });
        }
        catch (Exception error) { failure(error); throw; }
    }
}
