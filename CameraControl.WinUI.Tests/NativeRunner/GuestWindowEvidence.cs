using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

internal sealed class GuestWindowEvidence : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Task observer;
    internal GuestWindowEvidence(string executable, string evidence)
    {
        observer = Task.Run(async () =>
        {
            var seen = new HashSet<string>();
            var records = new List<object>();
            while (!stop.IsCancellationRequested)
            {
                foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
                {
                    using (process)
                    {
                        try
                        {
                            if (!string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase)) continue;
                            EnumWindows((window, _) =>
                            {
                                GetWindowThreadProcessId(window, out var pid);
                                if (pid != process.Id || !seen.Add(pid + "/" + window)) return true;
                                var title = new StringBuilder(256); GetWindowText(window, title, title.Capacity);
                                GetWindowRect(window, out var bounds);
                                records.Add(new { ProcessId = pid, WindowHandle = window.ToInt64(), Title = title.ToString(),
                                    Dpi = GetDpiForWindow(window), Bounds = new { X = bounds.Left, Y = bounds.Top,
                                        Width = bounds.Right - bounds.Left, Height = bounds.Bottom - bounds.Top } });
                                File.WriteAllText(Path.Combine(evidence, "own-guest-windows.json"), JsonSerializer.Serialize(records,
                                    new JsonSerializerOptions { WriteIndented = true }));
                                return true;
                            }, 0);
                        }
                        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException) { }
                    }
                }
                try { await Task.Delay(100, stop.Token); }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            }
        });
    }
    public void Dispose() { stop.Cancel(); observer.GetAwaiter().GetResult(); stop.Dispose(); }
    private delegate bool WindowCallback(nint window, nint parameter);
    [StructLayout(LayoutKind.Sequential)] private struct WindowRectangle { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, nint parameter);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out WindowRectangle bounds);
}
