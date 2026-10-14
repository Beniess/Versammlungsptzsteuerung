namespace CameraControl_WinUI.Video;

public static class VideoShutdown
{
    public static async Task RunAsync(Func<Task> stop, Action detach, Func<Task> release)
    {
        try { await stop(); }
        finally
        {
            try { detach(); }
            finally { await release(); }
        }
    }
}
