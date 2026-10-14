using System.Runtime.InteropServices;
using CameraControl.AppCore.Control;
using CameraControl.AppCore.Presets;
using CameraControl.AppCore.Storage;

namespace CameraControl_WinUI;

public static class UserOperation
{
    public static bool IsHandled(Exception error) => error is CameraOperationException or OperationCanceledException or
        InvalidOperationException or ArgumentException or IOException or SettingsStorageException or PhotoCaptureException or
        TimeoutException or COMException;

    public static async Task<string?> RunAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception error) when (IsHandled(error))
        {
            return error switch
            {
                TimeoutException => "Der Auftrag hat die Wartezeit überschritten. Prüfe die Verbindung und versuche es erneut.",
                COMException => "Das Bild kann nicht gelesen werden. Der gespeicherte Stand bleibt erhalten.",
                OperationCanceledException => "Der Auftrag wurde abgebrochen.",
                _ => error.Message
            };
        }
    }
}
