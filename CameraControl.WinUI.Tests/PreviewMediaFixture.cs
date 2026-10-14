using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;

namespace CameraControl.WinUI.Tests;

internal sealed class PreviewMediaFixture : IDisposable
{
    private readonly List<Process> processes = [];
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly string ffmpeg;
    private readonly string fixture;
    private readonly string directory;
    private Process? sender;
    internal int RtspPort { get; } = FreePort();
    internal int ApiPort { get; } = FreePort();
    internal string Address => $"rtsp://127.0.0.1:{RtspPort}/fixture";

    internal PreviewMediaFixture(string repo, string directory)
    {
        this.directory = directory;
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(repo, ".omo/evidence/winui3-modernisierung/T3/tools.lock.json")));
        string mediamtx = "";
        ffmpeg = "";
        foreach (var tool in manifest.RootElement.GetProperty("tools").EnumerateArray())
        {
            if (tool.GetProperty("id").GetString() is not ("MediaMTX" or "FFmpeg Windows essentials")) continue;
            var path = Path.Combine(repo, tool.GetProperty("executable").GetString()!);
            if (tool.GetProperty("id").GetString() == "MediaMTX") mediamtx = path;
            if (tool.GetProperty("id").GetString() == "FFmpeg Windows essentials") ffmpeg = path;
            Assert.That(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).Equals(tool.GetProperty("executableSha256").GetString(), StringComparison.OrdinalIgnoreCase), Is.True);
        }
        fixture = Path.Combine(repo, "tests/fixtures/video/rtsp-h264-640x360.mp4");
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(repo, "tests/fixtures/video/fixture.json")));
        Assert.That(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture))).Equals(metadata.RootElement.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase), Is.True);
        while (ApiPort == RtspPort) ApiPort = FreePort();
        var configuration = Path.Combine(directory, "mediamtx.yml");
        File.WriteAllText(configuration, $"""
            logLevel: info
            logDestinations: [file]
            logFile: mediamtx.log
            api: true
            apiAddress: 127.0.0.1:{ApiPort}
            metrics: false
            pprof: false
            playback: false
            rtsp: true
            rtspAddress: 127.0.0.1:{RtspPort}
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
        try
        {
            Start(mediamtx, [configuration]);
            Wait(() => { try { _ = Count("read"); return true; } catch (HttpRequestException) { return false; } }, 5, "Lokale MediaMTX-API");
            StartSender();
        }
        catch { Dispose(); throw; }
    }

    internal void StartSender()
    {
        sender = Start(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-re", "-stream_loop", "-1", "-i", fixture,
            "-an", "-c:v", "copy", "-f", "rtsp", "-rtsp_transport", "tcp", Address]);
        Wait(() => Count("publish") == 1, 5, "Genau ein lokaler Sender");
    }

    internal void StopSender()
    {
        if (sender is null || sender.HasExited) return;
        sender.Kill();
        Assert.That(sender.WaitForExit(3000), Is.True);
    }

    internal int Count(string state)
    {
        var json = http.GetStringAsync($"http://127.0.0.1:{ApiPort}/v3/rtspsessions/list").GetAwaiter().GetResult();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("items").EnumerateArray().Count(item => item.GetProperty("state").GetString() == state);
    }

    internal void WaitReaders(int count) => Wait(() => Count("read") == count, 5, $"RTSP-Leserzahl {count}");

    private Process Start(string executable, string[] arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        var process = Process.Start(info)!;
        processes.Add(process);
        return process;
    }

    internal static void Wait(Func<bool> predicate, int seconds, string name)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            if (predicate()) return;
            Thread.Sleep(80);
        }
        throw new AssertionException("Zeitgrenze überschritten: " + name);
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    public void Dispose()
    {
        var records = new List<object>();
        foreach (var process in processes)
        {
            if (!process.HasExited) { process.Kill(); process.WaitForExit(3000); }
            records.Add(new { process.Id, process.HasExited, process.ExitCode });
            process.Dispose();
        }
        File.WriteAllText(Path.Combine(directory, "media-cleanup.json"), JsonSerializer.Serialize(records));
        http.Dispose();
    }
}
