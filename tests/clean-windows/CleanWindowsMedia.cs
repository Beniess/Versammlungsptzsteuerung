using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

internal sealed class CleanWindowsMedia : IDisposable
{
    private readonly string evidence;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly Process server;
    private readonly Process sender;
    private readonly int apiPort;
    private readonly List<Task> logs = new();
    internal string Address { get; }

    internal CleanWindowsMedia(string media, string evidence)
    {
        this.evidence = evidence;
        var port = FreePort();
        apiPort = FreePort();
        while (apiPort == port) apiPort = FreePort();
        Address = $"rtsp://127.0.0.1:{port}/fixture";
        var config = Path.Combine(evidence, "mediamtx.yml");
        File.WriteAllText(config, $"""
            logLevel: info
            logDestinations: [stdout]
            api: true
            apiAddress: 127.0.0.1:{apiPort}
            metrics: false
            pprof: false
            playback: false
            rtsp: true
            rtspAddress: 127.0.0.1:{port}
            rtspTransports: [tcp]
            rtmp: false
            hls: false
            webrtc: false
            srt: false
            moq: false
            paths:
              fixture:
                source: publisher
            """);
        server = Start(Path.Combine(media, "mediamtx.exe"), "mediamtx", config);
        try
        {
            CleanWindowsProbe.WaitUntil(() => { using var sessions = Sessions(); return sessions is not null; }, "Der eigene Loopback-RTSP-Server ist nicht bereit.", 10);
            sender = Start(Path.Combine(media, "ffmpeg.exe"), "ffmpeg", "-hide_banner", "-loglevel", "error", "-re", "-stream_loop", "-1", "-i", Path.Combine(media, "fixture.mp4"), "-an", "-c:v", "copy", "-f", "rtsp", "-rtsp_transport", "tcp", Address);
            CleanWindowsProbe.WaitUntil(() => Count("publish") == 1, "Der eigene RTSP-Sender ist nicht bereit.", 10);
        }
        catch { server.Dispose(); http.Dispose(); throw; }
    }

    internal void WaitReader()
    {
        CleanWindowsProbe.WaitUntil(() => Count("read") == 1, "Der Produktprozess liest den Loopback-Stream nicht.", 10);
        CleanWindowsProbe.Write(evidence, "rtsp-reader.json", new { Address, Readers = Count("read"), Publishers = Count("publish"), FixtureSha256 = CleanWindowsProbe.Hash(@"C:\Payload\Media\fixture.mp4") });
    }

    internal void WaitNoReader() => CleanWindowsProbe.WaitUntil(() => Count("read") == 0, "Nach dem Schließen bleibt eine eigene RTSP-Lesesitzung offen.", 10);

    private JsonDocument? Sessions()
    {
        try { return JsonDocument.Parse(http.GetStringAsync($"http://127.0.0.1:{apiPort}/v3/rtspsessions/list").GetAwaiter().GetResult()); }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException) { return null; }
    }

    private int Count(string state)
    {
        using var document = Sessions();
        return document?.RootElement.GetProperty("items").EnumerateArray().Count(item => item.GetProperty("state").GetString() == state) ?? 0;
    }

    private Process Start(string executable, string name, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = evidence, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        var process = Process.Start(info) ?? throw new IOException("Das eigene Testwerkzeug wurde nicht gestartet.");
        logs.Add(SaveLog(process.StandardOutput, Path.Combine(evidence, name + "-stdout.log")));
        logs.Add(SaveLog(process.StandardError, Path.Combine(evidence, name + "-stderr.log")));
        return process;
    }

    private static async Task SaveLog(StreamReader reader, string path)
    {
        using var writer = new StreamWriter(path) { AutoFlush = true };
        while (await reader.ReadLineAsync() is { } line) await writer.WriteLineAsync(line);
    }
    private static int FreePort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }

    public void Dispose()
    {
        if (!sender.HasExited) { sender.StandardInput.WriteLine("q"); sender.StandardInput.Flush(); sender.WaitForExit(10000); }
        var senderExited = sender.HasExited;
        CleanWindowsProbe.Write(evidence, "media-cleanup.json", new { SenderProcessId = sender.Id, SenderExited = sender.HasExited, ServerProcessId = server.Id,
            ServerStoppedByGuestShutdown = true, KillUsed = false });
        sender.Dispose();
        server.Dispose();
        http.Dispose();
        CleanWindowsProbe.Require(senderExited, "Der eigene FFmpeg-Sender hat sich nach q nicht beendet.");
    }
}
