using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using NUnit.Framework;

namespace CameraControl.Visca.Tests;

internal sealed class LoopbackServer : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly List<TcpClient> clients = new();
    internal int Port { get; }

    internal LoopbackServer()
    {
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        TestContext.WriteLine("Loopback listener: 127.0.0.1:" + Port);
    }

    internal async Task Session(Func<NetworkStream, Task> script)
    {
        using TcpClient client = await Within(listener.AcceptTcpClientAsync());
        lock (clients) clients.Add(client);
        client.NoDelay = true;
        using NetworkStream stream = client.GetStream();
        await script(stream);
    }

    internal static async Task Request(NetworkStream stream, string expected)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            int count = await Within(stream.ReadAsync(one, 0, 1));
            if (count == 0) throw new EndOfStreamException("Client closed before command terminator.");
            bytes.Add(one[0]);
            if (one[0] == 0xff) break;
            if (bytes.Count > 16) throw new InvalidDataException("Command exceeds VISCA limit.");
        }
        Assert.That(bytes, Is.EqualTo(Fixtures.Wire(expected)));
        TestContext.WriteLine("Observed command: " + expected + " / " + BitConverter.ToString(bytes.ToArray()));
    }

    internal static Task Send(NetworkStream stream, string response)
    {
        byte[] bytes = Fixtures.Wire(response);
        return stream.WriteAsync(bytes, 0, bytes.Length);
    }

    internal static async Task<T> Within<T>(Task<T> operation)
    {
        if (await Task.WhenAny(operation, Task.Delay(TimeSpan.FromSeconds(5))) != operation)
            throw new TimeoutException("Test operation exceeded 5 seconds.");
        return await operation;
    }

    internal static async Task Within(Task operation)
    {
        if (await Task.WhenAny(operation, Task.Delay(TimeSpan.FromSeconds(5))) != operation)
            throw new TimeoutException("Test operation exceeded 5 seconds.");
        await operation;
    }

    public void Dispose()
    {
        listener.Stop();
        lock (clients)
        {
            foreach (TcpClient client in clients) client.Dispose();
            clients.Clear();
        }
        TestContext.WriteLine("Loopback listener and accepted sockets closed: " + Port);
    }
}
