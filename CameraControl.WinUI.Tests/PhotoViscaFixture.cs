using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using NUnit.Framework;

namespace CameraControl.WinUI.Tests;

internal sealed class PhotoViscaFixture : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<string, byte[]> packets;
    private readonly List<TcpClient> clients = [];
    private readonly List<Task> sessions = [];
    private readonly List<string> observed = [];
    private readonly Task accept;
    private readonly string evidence;
    private readonly bool allowManualControl;
    private Exception? failure;
    private volatile bool changePosition;
    private int photoPanReads;
    internal int Port { get; }

    internal PhotoViscaFixture(string repo, string evidence, bool allowManualControl = false)
    {
        this.evidence = evidence;
        this.allowManualControl = allowManualControl;
        packets = File.ReadAllLines(Path.Combine(repo, "tests/fixtures/visca/packets.tsv"))
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
            .Select(line => line.Split('|')).ToDictionary(parts => parts[0], parts =>
                parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(value => Convert.ToByte(value, 16)).ToArray());
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        accept = Task.Run(AcceptAsync);
    }

    internal void ChangeNextPhotoPosition()
    {
        photoPanReads = 0;
        changePosition = true;
    }

    internal void StablePosition() => changePosition = false;
    internal void AssertHealthy() => Assert.That(failure, Is.Null);
    internal int Count(string packet) { lock (observed) return observed.Count(value => value == packet); }
    internal void WaitForCount(string packet, int count) => NativeQaSession.WaitUntil(() => Count(packet) >= count,
        "Das erwartete zusätzliche VISCA-Paket fehlt: " + packet);

    private async Task AcceptAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(lifetime.Token);
                lock (clients) { clients.Add(client); sessions.Add(Task.Run(() => ServeAsync(client))); }
            }
        }
        catch (Exception error) when (error is OperationCanceledException or SocketException) { }
    }

    private async Task ServeAsync(TcpClient client)
    {
        try
        {
            using var stream = client.GetStream();
            var request = new List<byte>();
            byte[] one = new byte[1];
            while (await stream.ReadAsync(one, lifetime.Token) != 0)
            {
                request.Add(one[0]);
                if (one[0] != 0xff) { if (request.Count > 16) throw new InvalidDataException("Ungültige VISCA-Testanfrage."); continue; }
                string name = packets.FirstOrDefault(packet => packet.Value.SequenceEqual(request)).Key ??
                    throw new InvalidDataException("Die VISCA-Anfrage gehört nicht zu den festen Testpaketen.");
                string response = name switch
                {
                    "power-query" => "power-answer-on",
                    "pan-query" => changePosition && Interlocked.Increment(ref photoPanReads) == 2 ? "pan-answer-zero" : "pan-answer-unit",
                    "zoom-query" => "zoom-answer-4660",
                    "pan-stop" or "zoom-stop" or "home" or "absolute-unit" or "zoom-4660" => "ack-complete",
                    "pan-right" when allowManualControl => "ack-complete",
                    _ => throw new InvalidDataException("Unerwartete Steuerung im Foto-Test.")
                };
                lock (observed) observed.Add(name);
                await stream.WriteAsync(packets[response], lifetime.Token);
                request.Clear();
            }
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or ObjectDisposedException) { }
        catch (Exception error) { failure = error; }
    }

    public void Dispose()
    {
        lifetime.Cancel();
        listener.Stop();
        lock (clients) foreach (var client in clients) client.Dispose();
        try { accept.GetAwaiter().GetResult(); Task.WhenAll(sessions).GetAwaiter().GetResult(); }
        finally
        {
            File.WriteAllText(Path.Combine(evidence, "visca-queries.json"), JsonSerializer.Serialize(observed));
            lifetime.Dispose();
        }
    }
}
