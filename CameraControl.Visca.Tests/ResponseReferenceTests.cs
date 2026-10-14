using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace CameraControl.Visca.Tests;

[TestFixture, Category("Protocol")]
public sealed class ResponseReferenceTests
{
    [Test]
    public async Task AckPrecedesCompletion()
    {
        using var client = new ScriptedClient("ack", "complete");
        using var controller = new ViscaController(client, TimeSpan.FromSeconds(3), null);
        await controller.GoHome();
        Assert.That(client.ReceiveCount, Is.EqualTo(2));
        Assert.That(client.DisconnectCount, Is.Zero);
    }

    [TestCase("error", typeof(ViscaResponseException))]
    [TestCase("invalid-type", typeof(ViscaProtocolException))]
    [TestCase("short", typeof(ViscaProtocolException))]
    public void ErrorResponsesRemainDistinct(string response, Type expected)
    {
        using var client = new ScriptedClient(response);
        using var controller = new ViscaController(client, TimeSpan.FromSeconds(3), null);
        Assert.ThrowsAsync(expected, async () => await controller.GetZoom());
        Assert.That(client.DisconnectCount, Is.EqualTo(1));
    }

    private sealed class ScriptedClient : ViscaClientBase
    {
        private readonly Queue<ViscaPacket> responses = new();
        internal int ReceiveCount { get; private set; }
        internal int DisconnectCount { get; private set; }

        internal ScriptedClient(params string[] packets) : base(null)
        {
            foreach (string packet in packets) responses.Enqueue(Fixtures.Response(packet));
        }

        protected override Task SendPacketAsync(ViscaPacket packet, CancellationToken cancellationToken) => Task.CompletedTask;
        protected override Task<ViscaPacket> ReceivePacketAsync(CancellationToken cancellationToken)
        {
            ReceiveCount++;
            return Task.FromResult(responses.Dequeue());
        }
        protected override Task ReconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        protected override void Disconnect() => DisconnectCount++;
        public override void Dispose() { }
    }
}
