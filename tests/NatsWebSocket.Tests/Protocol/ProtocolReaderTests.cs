using System.Text;
using FluentAssertions;
using NatsWebSocket.Protocol;
using Xunit;

namespace NatsWebSocket.Tests.Protocol;

public class ProtocolReaderTests
{
    [Fact]
    public void TryParse_Ping_ReturnsPingCommand()
    {
        var reader = new ProtocolReader();
        reader.Append(Encoding.UTF8.GetBytes("PING\r\n"), 0, 6);

        var msg = reader.TryParse();

        msg.Should().NotBeNull();
        msg.Command.Should().Be("PING");
    }

    [Fact]
    public void TryParse_Pong_ReturnsPongCommand()
    {
        var reader = new ProtocolReader();
        reader.Append(Encoding.UTF8.GetBytes("PONG\r\n"), 0, 6);

        var msg = reader.TryParse();

        msg.Should().NotBeNull();
        msg.Command.Should().Be("PONG");
    }

    [Fact]
    public void TryParse_Ok_ReturnsOkCommand()
    {
        var reader = new ProtocolReader();
        reader.Append(Encoding.UTF8.GetBytes("+OK\r\n"), 0, 5);

        var msg = reader.TryParse();

        msg.Should().NotBeNull();
        msg.Command.Should().Be("+OK");
    }

    [Fact]
    public void TryParse_Err_ReturnsErrWithMessage()
    {
        var reader = new ProtocolReader();
        var data = Encoding.UTF8.GetBytes("-ERR 'Authorization Violation'\r\n");
        reader.Append(data, 0, data.Length);

        var msg = reader.TryParse();

        msg.Should().NotBeNull();
        msg.Command.Should().Be("-ERR");
        msg.RawLine.Should().Be("Authorization Violation");
    }

    [Fact]
    public void TryParse_Info_ReturnsInfoWithJson()
    {
        var json = "{\"server_id\":\"test\",\"version\":\"2.10.0\"}";
        var data = Encoding.UTF8.GetBytes($"INFO {json}\r\n");
        var reader = new ProtocolReader();
        reader.Append(data, 0, data.Length);

        var msg = reader.TryParse();

        msg.Should().NotBeNull();
        msg.Command.Should().Be("INFO");
        msg.RawLine.Should().Be(json);
    }

    [Fact]
    public void TryParse_Msg_WithoutReplyTo_ParsesCorrectly()
    {
        var reader = new ProtocolReader();
        var payload = Encoding.UTF8.GetBytes("hello world");
        var header = Encoding.UTF8.GetBytes($"MSG test.subject 1 {payload.Length}\r\n");
        var crlf = Encoding.UTF8.GetBytes("\r\n");

        reader.Append(header, 0, header.Length);
        reader.Append(payload, 0, payload.Length);
        reader.Append(crlf, 0, crlf.Length);

        var msg = reader.TryParse();

        msg.Should().NotBeNull();
        msg.Command.Should().Be("MSG");
        msg.Subject.Should().Be("test.subject");
        msg.Sid.Should().Be("1");
        msg.ReplyTo.Should().BeNull();
        Encoding.UTF8.GetString(msg.Payload).Should().Be("hello world");
    }

    [Fact]
    public void TryParse_Msg_WithReplyTo_ParsesCorrectly()
    {
        var reader = new ProtocolReader();
        var payload = Encoding.UTF8.GetBytes("data");
        var header = Encoding.UTF8.GetBytes($"MSG test.subject 5 _INBOX.reply 4\r\n");
        var crlf = Encoding.UTF8.GetBytes("\r\n");

        reader.Append(header, 0, header.Length);
        reader.Append(payload, 0, payload.Length);
        reader.Append(crlf, 0, crlf.Length);

        var msg = reader.TryParse();

        msg.Should().NotBeNull();
        msg.Subject.Should().Be("test.subject");
        msg.Sid.Should().Be("5");
        msg.ReplyTo.Should().Be("_INBOX.reply");
        msg.Payload.Should().HaveCount(4);
    }

    [Fact]
    public void TryParse_Hmsg_WithHeaders_ParsesCorrectly()
    {
        var reader = new ProtocolReader();
        var hdrStr = "NATS/1.0\r\nX-Test: value\r\n\r\n";
        var hdrBytes = Encoding.UTF8.GetBytes(hdrStr);
        var payload = Encoding.UTF8.GetBytes("{\"ok\":true}");
        var totalLen = hdrBytes.Length + payload.Length;
        var header = Encoding.UTF8.GetBytes($"HMSG test.subject 2 {hdrBytes.Length} {totalLen}\r\n");
        var crlf = Encoding.UTF8.GetBytes("\r\n");

        reader.Append(header, 0, header.Length);
        reader.Append(hdrBytes, 0, hdrBytes.Length);
        reader.Append(payload, 0, payload.Length);
        reader.Append(crlf, 0, crlf.Length);

        var msg = reader.TryParse();

        msg.Should().NotBeNull();
        msg.Command.Should().Be("HMSG");
        msg.Subject.Should().Be("test.subject");
        msg.Sid.Should().Be("2");
        msg.HeaderBytes.Should().HaveCount(hdrBytes.Length);
        Encoding.UTF8.GetString(msg.Payload).Should().Be("{\"ok\":true}");
    }

    [Fact]
    public void TryParse_Hmsg_WithReplyTo_ParsesCorrectly()
    {
        var reader = new ProtocolReader();
        var hdrStr = "NATS/1.0 503 No Responders\r\n\r\n";
        var hdrBytes = Encoding.UTF8.GetBytes(hdrStr);
        var totalLen = hdrBytes.Length;
        var header = Encoding.UTF8.GetBytes($"HMSG test.subject 3 _INBOX.rep {hdrBytes.Length} {totalLen}\r\n");
        var crlf = Encoding.UTF8.GetBytes("\r\n");

        reader.Append(header, 0, header.Length);
        reader.Append(hdrBytes, 0, hdrBytes.Length);
        reader.Append(crlf, 0, crlf.Length);

        var msg = reader.TryParse();

        msg.Should().NotBeNull();
        msg.Command.Should().Be("HMSG");
        msg.Subject.Should().Be("test.subject");
        msg.ReplyTo.Should().Be("_INBOX.rep");
        msg.Payload.Should().HaveCount(0);
    }

    [Fact]
    public void TryParse_IncompleteData_ReturnsNull()
    {
        var reader = new ProtocolReader();
        reader.Append(Encoding.UTF8.GetBytes("PIN"), 0, 3);

        var msg = reader.TryParse();

        msg.Should().BeNull();
    }

    [Fact]
    public void TryParse_IncompleteMsg_ReturnsNull()
    {
        var reader = new ProtocolReader();
        var header = Encoding.UTF8.GetBytes("MSG test.subject 1 100\r\n");
        reader.Append(header, 0, header.Length);
        // Only 10 bytes of the 100-byte payload
        reader.Append(new byte[10], 0, 10);

        var msg = reader.TryParse();

        msg.Should().BeNull();
    }

    [Fact]
    public void TryParse_MultipleMessages_ParsesSequentially()
    {
        var reader = new ProtocolReader();
        var data = Encoding.UTF8.GetBytes("PING\r\nPONG\r\n+OK\r\n");
        reader.Append(data, 0, data.Length);

        reader.TryParse().Command.Should().Be("PING");
        reader.TryParse().Command.Should().Be("PONG");
        reader.TryParse().Command.Should().Be("+OK");
        reader.TryParse().Should().BeNull();
    }

    [Fact]
    public void Append_GrowsBuffer_WhenNeeded()
    {
        var reader = new ProtocolReader(16); // tiny initial buffer
        var data = Encoding.UTF8.GetBytes("MSG test.subject 1 5\r\nhello\r\n");
        reader.Append(data, 0, data.Length);

        var msg = reader.TryParse();

        msg.Should().NotBeNull();
        msg.Command.Should().Be("MSG");
        Encoding.UTF8.GetString(msg.Payload).Should().Be("hello");
    }
}
