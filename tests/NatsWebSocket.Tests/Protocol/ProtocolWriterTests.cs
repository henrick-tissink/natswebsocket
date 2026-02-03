using System.Text;
using FluentAssertions;
using NatsWebSocket.Protocol;
using Xunit;

namespace NatsWebSocket.Tests.Protocol;

public class ProtocolWriterTests
{
    [Fact]
    public void Connect_ProducesCorrectWireFormat()
    {
        var json = "{\"verbose\":false}";
        var result = Encoding.UTF8.GetString(ProtocolWriter.Connect(json));

        result.Should().Be("CONNECT {\"verbose\":false}\r\n");
    }

    [Fact]
    public void Ping_ProducesCorrectWireFormat()
    {
        Encoding.UTF8.GetString(ProtocolWriter.Ping()).Should().Be("PING\r\n");
    }

    [Fact]
    public void Pong_ProducesCorrectWireFormat()
    {
        Encoding.UTF8.GetString(ProtocolWriter.Pong()).Should().Be("PONG\r\n");
    }

    [Fact]
    public void Sub_WithoutQueueGroup_ProducesCorrectFormat()
    {
        var result = Encoding.UTF8.GetString(ProtocolWriter.Sub("test.subject", "1"));
        result.Should().Be("SUB test.subject 1\r\n");
    }

    [Fact]
    public void Sub_WithQueueGroup_ProducesCorrectFormat()
    {
        var result = Encoding.UTF8.GetString(ProtocolWriter.Sub("test.subject", "1", "workers"));
        result.Should().Be("SUB test.subject workers 1\r\n");
    }

    [Fact]
    public void Unsub_WithoutMax_ProducesCorrectFormat()
    {
        var result = Encoding.UTF8.GetString(ProtocolWriter.Unsub("5"));
        result.Should().Be("UNSUB 5\r\n");
    }

    [Fact]
    public void Unsub_WithMax_ProducesCorrectFormat()
    {
        var result = Encoding.UTF8.GetString(ProtocolWriter.Unsub("5", 1));
        result.Should().Be("UNSUB 5 1\r\n");
    }

    [Fact]
    public void Pub_WithoutReplyTo_ProducesCorrectFormat()
    {
        var payload = Encoding.UTF8.GetBytes("hello");
        var result = Encoding.UTF8.GetString(ProtocolWriter.Pub("test.subject", null, payload));

        result.Should().Be("PUB test.subject 5\r\nhello\r\n");
    }

    [Fact]
    public void Pub_WithReplyTo_ProducesCorrectFormat()
    {
        var payload = Encoding.UTF8.GetBytes("data");
        var result = Encoding.UTF8.GetString(ProtocolWriter.Pub("test.subject", "_INBOX.reply", payload));

        result.Should().Be("PUB test.subject _INBOX.reply 4\r\ndata\r\n");
    }

    [Fact]
    public void Pub_EmptyPayload_ProducesCorrectFormat()
    {
        var result = Encoding.UTF8.GetString(ProtocolWriter.Pub("test.subject", null, new byte[0]));
        result.Should().Be("PUB test.subject 0\r\n\r\n");
    }

    [Fact]
    public void HPub_WithoutReplyTo_ProducesCorrectFormat()
    {
        var hdr = Encoding.UTF8.GetBytes("NATS/1.0\r\nX-Key: val\r\n\r\n");
        var payload = Encoding.UTF8.GetBytes("body");
        var result = Encoding.UTF8.GetString(ProtocolWriter.HPub("test.subject", null, hdr, payload));

        var expected = $"HPUB test.subject {hdr.Length} {hdr.Length + payload.Length}\r\nNATS/1.0\r\nX-Key: val\r\n\r\nbody\r\n";
        result.Should().Be(expected);
    }

    [Fact]
    public void HPub_WithReplyTo_ProducesCorrectFormat()
    {
        var hdr = Encoding.UTF8.GetBytes("NATS/1.0\r\n\r\n");
        var payload = Encoding.UTF8.GetBytes("test");
        var result = Encoding.UTF8.GetString(ProtocolWriter.HPub("sub", "_INBOX.r", hdr, payload));

        var expected = $"HPUB sub _INBOX.r {hdr.Length} {hdr.Length + payload.Length}\r\nNATS/1.0\r\n\r\ntest\r\n";
        result.Should().Be(expected);
    }
}
