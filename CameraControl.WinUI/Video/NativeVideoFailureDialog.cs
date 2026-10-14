using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace CameraControl_WinUI.Video;

internal static class NativeVideoFailureDialog
{
    private const string Title = "Videobibliothek fehlt";
    internal const string Message = "Die Videobibliothek fehlt im Programmpaket. Laden Sie ein vollständiges Programmpaket.";

    internal static void Show(string nativeDirectory, VideoProbeOptions? probe)
    {
        using var cancellation = new CancellationTokenSource();
        var threadId = GetCurrentThreadId();
        var observer = probe is null ? Task.CompletedTask : Task.Run(() =>
        {
            while (!cancellation.IsCancellationRequested)
            {
                nint dialog = 0;
                EnumThreadWindows(threadId, (hwnd, _) =>
                {
                    var title = new StringBuilder(128);
                    GetWindowText(hwnd, title, title.Capacity);
                    if (IsWindowVisible(hwnd) && title.ToString() == Title) dialog = hwnd;
                    return dialog == 0;
                }, 0);
                if (dialog != 0)
                {
                    var state = new { Hwnd = dialog.ToInt64(), Initialized = false, Busy = false, LastCommandId = 0,
                        Phase = "error", Error = Message, DecodedFrames = 0, DisplayedFrames = 0, StartupFailure = true,
                        NativeDirectory = nativeDirectory, MissingNativeFile = Path.Combine(nativeDirectory, "libvlc.dll") };
                    Directory.CreateDirectory(probe.OutputDirectory);
                    File.WriteAllText(Path.Combine(probe.OutputDirectory, "state.json"), JsonSerializer.Serialize(state));
                    return;
                }
                cancellation.Token.WaitHandle.WaitOne(50);
            }
        });
        try { MessageBox(0, Message, Title, 0x10); }
        finally { cancellation.Cancel(); observer.GetAwaiter().GetResult(); }
    }

    private delegate bool EnumWindowsCallback(nint hwnd, nint parameter);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint threadId, EnumWindowsCallback callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, StringBuilder text, int capacity);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)] private static extern int MessageBox(nint hwnd, string text, string caption, uint type);
}
