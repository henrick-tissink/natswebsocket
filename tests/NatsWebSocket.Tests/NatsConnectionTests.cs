using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NatsWebSocket.Tests.Helpers;
using Xunit;

namespace NatsWebSocket.Tests
{
    public class NatsConnectionTests
    {
        private const string TestInfoJson = "{\"server_id\":\"test\",\"server_name\":\"test-server\",\"version\":\"2.10.0\",\"host\":\"0.0.0.0\",\"port\":4222,\"headers\":true,\"max_payload\":1048576,\"proto\":1}";

        private static void EnqueueHandshake(MockTransport mock)
        {
            mock.EnqueueInbound($"INFO {TestInfoJson}\r\n");
            mock.EnqueueInbound("PONG\r\n");
        }

        private static NatsConnectionOptions DefaultOptions() => new NatsConnectionOptions
        {
            Url = "ws://localhost:4222",
            AllowReconnect = false,
            PingInterval = TimeSpan.FromHours(1), // disable for most tests
        };

        [Fact]
        public async Task ConnectAsync_PerformsHandshake()
        {
            var mock = new MockTransport();
            EnqueueHandshake(mock);
            var opts = DefaultOptions();
            var conn = new NatsConnection(opts, () => mock);

            await conn.ConnectAsync();

            conn.Status.Should().Be(NatsStatus.Connected);
            conn.ServerInfo.Should().NotBeNull();
            conn.ServerInfo.ServerId.Should().Be("test");
            conn.ServerInfo.Version.Should().Be("2.10.0");

            // Verify CONNECT and PING were sent
            var sent = mock.SentText;
            sent.Should().Contain("CONNECT ");
            sent.Should().Contain("PING\r\n");

            await conn.CloseAsync();
        }

        [Fact]
        public async Task ConnectAsync_WhenAlreadyConnected_Throws()
        {
            var mock = new MockTransport();
            EnqueueHandshake(mock);
            var opts = DefaultOptions();
            var conn = new NatsConnection(opts, () => mock);

            await conn.ConnectAsync();

            Func<Task> act = () => conn.ConnectAsync();
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*already*");

            await conn.CloseAsync();
        }

        [Fact]
        public async Task ConnectAsync_AuthFailure_ThrowsNatsAuthException()
        {
            var mock = new MockTransport();
            mock.EnqueueInbound($"INFO {TestInfoJson}\r\n");
            mock.EnqueueInbound("-ERR 'Authorization Violation'\r\n");

            var opts = DefaultOptions();
            var conn = new NatsConnection(opts, () => mock);

            Func<Task> act = () => conn.ConnectAsync();
            await act.Should().ThrowAsync<NatsAuthException>()
                .WithMessage("*Authentication failed*");

            conn.Dispose();
        }

        [Fact]
        public async Task ConnectAsync_Timeout_ThrowsNatsConnectionException()
        {
            var mock = new MockTransport();
            mock.SetNeverComplete(true);

            var opts = DefaultOptions();
            opts.ConnectTimeout = TimeSpan.FromMilliseconds(200);
            var conn = new NatsConnection(opts, () => mock);

            Func<Task> act = () => conn.ConnectAsync();
            await act.Should().ThrowAsync<NatsConnectionException>()
                .WithMessage("*timed out*");

            conn.Dispose();
        }

        [Fact]
        public async Task PublishAsync_SendsCorrectWireFormat()
        {
            var mock = new MockTransport();
            EnqueueHandshake(mock);
            var opts = DefaultOptions();
            var conn = new NatsConnection(opts, () => mock);
            await conn.ConnectAsync();

            var data = Encoding.UTF8.GetBytes("hello");
            await conn.PublishAsync("test.subject", data);

            var sent = mock.SentText;
            sent.Should().Contain("PUB test.subject 5\r\nhello\r\n");

            await conn.CloseAsync();
        }

        [Fact]
        public async Task PublishAsync_WithHeaders_SendsHPub()
        {
            var mock = new MockTransport();
            EnqueueHandshake(mock);
            var opts = DefaultOptions();
            var conn = new NatsConnection(opts, () => mock);
            await conn.ConnectAsync();

            var headers = new NatsHeaders();
            headers.Add("X-Test", "value");
            var data = Encoding.UTF8.GetBytes("body");
            await conn.PublishAsync("test.subject", data, headers);

            var sent = mock.SentText;
            sent.Should().Contain("HPUB test.subject");
            sent.Should().Contain("X-Test: value");

            await conn.CloseAsync();
        }

        [Fact]
        public async Task RequestAsync_ReceivesReply()
        {
            var mock = new MockTransport();
            EnqueueHandshake(mock);
            var opts = DefaultOptions();
            var conn = new NatsConnection(opts, () => mock);
            await conn.ConnectAsync();

            var requestTask = Task.Run(async () =>
            {
                // Wait for the PUB to appear
                string allSent = "";
                for (int i = 0; i < 40; i++)
                {
                    await Task.Delay(25);
                    allSent = mock.SentText;
                    if (allSent.Contains("PUB test.request "))
                        break;
                }

                var pubLine = allSent.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(l => l.StartsWith("PUB test.request "));

                if (pubLine == null) return;

                // PUB test.request _INBOX.xxx.1 5
                var pubParts = pubLine.Split(' ');
                var replyTo = pubParts[2];

                // Feed back a MSG reply
                var replyPayload = "world";
                mock.EnqueueInbound($"MSG {replyTo} 99 {Encoding.UTF8.GetByteCount(replyPayload)}\r\n{replyPayload}\r\n");
            });

            var reply = await conn.RequestAsync("test.request", Encoding.UTF8.GetBytes("hello"), timeout: TimeSpan.FromSeconds(5));

            await requestTask;
            reply.GetString().Should().Be("world");

            await conn.CloseAsync();
        }

        [Fact]
        public async Task RequestAsync_Timeout_ThrowsNatsRequestTimeoutException()
        {
            var mock = new MockTransport();
            EnqueueHandshake(mock);
            var opts = DefaultOptions();
            var conn = new NatsConnection(opts, () => mock);
            await conn.ConnectAsync();

            Func<Task> act = () => conn.RequestAsync("no.reply", Encoding.UTF8.GetBytes("test"), timeout: TimeSpan.FromMilliseconds(100));
            await act.Should().ThrowAsync<NatsRequestTimeoutException>();

            await conn.CloseAsync();
        }

        [Fact]
        public async Task RequestAsync_NoResponders_ThrowsNatsNoRespondersException()
        {
            var mock = new MockTransport();
            EnqueueHandshake(mock);
            var opts = DefaultOptions();
            var conn = new NatsConnection(opts, () => mock);
            await conn.ConnectAsync();

            var requestTask = Task.Run(async () =>
            {
                // Wait for PUB to appear
                string allSent = "";
                for (int i = 0; i < 40; i++)
                {
                    await Task.Delay(25);
                    allSent = mock.SentText;
                    if (allSent.Contains("PUB test.no-resp "))
                        break;
                }

                var pubLine = allSent.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(l => l.StartsWith("PUB test.no-resp "));

                if (pubLine == null) return;

                var pubParts = pubLine.Split(' ');
                var replyTo = pubParts[2];

                // Send a 503 No Responders as HMSG
                var hdrText = "NATS/1.0 503 No Responders\r\n\r\n";
                var hdrBytes = Encoding.UTF8.GetBytes(hdrText);
                var totalLen = hdrBytes.Length;

                // Build the full HMSG frame as one enqueue
                var frame = $"HMSG {replyTo} 99 {totalLen} {totalLen}\r\n" + hdrText + "\r\n";
                mock.EnqueueInbound(frame);
            });

            Func<Task> act = () => conn.RequestAsync("test.no-resp", Encoding.UTF8.GetBytes("test"), timeout: TimeSpan.FromSeconds(5));
            await act.Should().ThrowAsync<NatsNoRespondersException>();

            await requestTask;
            await conn.CloseAsync();
        }

        [Fact]
        public async Task SubscribeAsync_SendsSubCommand()
        {
            var mock = new MockTransport();
            EnqueueHandshake(mock);
            var opts = DefaultOptions();
            var conn = new NatsConnection(opts, () => mock);
            await conn.ConnectAsync();

            var sub = await conn.SubscribeAsync("test.sub", msg => { });

            var sent = mock.SentText;
            sent.Should().Contain("SUB test.sub ");

            sub.Dispose();
            await conn.CloseAsync();
        }

        [Fact]
        public async Task FlushAsync_SendsPingAndWaitsForPong()
        {
            var mock = new MockTransport();
            EnqueueHandshake(mock);
            var opts = DefaultOptions();
            var conn = new NatsConnection(opts, () => mock);
            await conn.ConnectAsync();

            // Feed a PONG shortly after flush sends PING
            var flushTask = Task.Run(async () =>
            {
                await Task.Delay(50);
                mock.EnqueueInbound("PONG\r\n");
            });

            await conn.FlushAsync();
            await flushTask;

            // Verify at least one PING was sent after the handshake
            var sentPings = mock.SentText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Count(l => l == "PING");
            sentPings.Should().BeGreaterOrEqualTo(2); // 1 from handshake + 1 from flush

            await conn.CloseAsync();
        }

        [Fact]
        public async Task Reconnect_ResubscribesOnSuccess()
        {
            // Use a shared mock that can be reset for reconnect
            var mock = new MockTransport();
            EnqueueHandshake(mock);

            var opts = DefaultOptions();
            opts.AllowReconnect = true;
            opts.MaxReconnectAttempts = 1;
            opts.ReconnectDelay = TimeSpan.FromMilliseconds(50);

            // The factory always returns the same mock, but we need a fresh one for reconnect
            var reconnectMock = new MockTransport();
            var callCount = 0;
            Func<MockTransport> factory = () =>
            {
                var count = Interlocked.Increment(ref callCount);
                if (count == 1) return mock;
                EnqueueHandshake(reconnectMock);
                return reconnectMock;
            };

            // Need to cast to Func<ITransport> for the constructor
            var conn = new NatsConnection(opts, () => factory());
            await conn.ConnectAsync();

            // Subscribe
            await conn.SubscribeAsync("test.resub", msg => { });

            // Verify initial SUB was sent
            mock.SentText.Should().Contain("SUB test.resub ");

            // Simulate disconnect
            mock.SimulateDisconnect();

            // Wait for reconnect to complete
            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(50);
                if (conn.Status == NatsStatus.Connected && callCount >= 2)
                    break;
            }

            // After reconnect, SUB test.resub should appear in the reconnect mock
            reconnectMock.SentText.Should().Contain("SUB test.resub ");

            await conn.CloseAsync();
        }
    }
}
