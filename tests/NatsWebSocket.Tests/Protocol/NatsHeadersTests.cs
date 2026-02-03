using System.Text;
using FluentAssertions;
using Xunit;

namespace NatsWebSocket.Tests.Protocol;

public class NatsHeadersTests
{
    [Fact]
    public void ToWireBytes_EmptyHeaders_ProducesStatusLineOnly()
    {
        var headers = new NatsHeaders();
        var result = Encoding.UTF8.GetString(headers.ToWireBytes());

        result.Should().Be("NATS/1.0\r\n\r\n");
    }

    [Fact]
    public void ToWireBytes_WithHeaders_ProducesCorrectFormat()
    {
        var headers = new NatsHeaders();
        headers.Add("token", "abc123");
        headers.Add("X-Custom", "value");

        var result = Encoding.UTF8.GetString(headers.ToWireBytes());

        result.Should().Contain("NATS/1.0\r\n");
        result.Should().Contain("token: abc123\r\n");
        result.Should().Contain("X-Custom: value\r\n");
        result.Should().EndWith("\r\n\r\n");
    }

    [Fact]
    public void FromWireBytes_StatusLineOnly_ParsesCorrectly()
    {
        var wire = Encoding.UTF8.GetBytes("NATS/1.0\r\n\r\n");
        var headers = NatsHeaders.FromWireBytes(wire, 0, wire.Length);

        headers.StatusCode.Should().BeNull();
        headers.Count.Should().Be(0);
    }

    [Fact]
    public void FromWireBytes_WithStatusCode_ParsesStatusCodeAndDescription()
    {
        var wire = Encoding.UTF8.GetBytes("NATS/1.0 503 No Responders\r\n\r\n");
        var headers = NatsHeaders.FromWireBytes(wire, 0, wire.Length);

        headers.StatusCode.Should().Be(503);
        headers.StatusDescription.Should().Be("No Responders");
    }

    [Fact]
    public void FromWireBytes_WithStatusCodeOnly_ParsesStatusCode()
    {
        var wire = Encoding.UTF8.GetBytes("NATS/1.0 200\r\n\r\n");
        var headers = NatsHeaders.FromWireBytes(wire, 0, wire.Length);

        headers.StatusCode.Should().Be(200);
        headers.StatusDescription.Should().BeNull();
    }

    [Fact]
    public void FromWireBytes_WithHeaders_ParsesKeyValuePairs()
    {
        var wire = Encoding.UTF8.GetBytes("NATS/1.0\r\nNats-Service-Error: not found\r\nNats-Service-Error-Code: 404\r\n\r\n");
        var headers = NatsHeaders.FromWireBytes(wire, 0, wire.Length);

        headers.GetFirst("Nats-Service-Error").Should().Be("not found");
        headers.GetFirst("Nats-Service-Error-Code").Should().Be("404");
    }

    [Fact]
    public void FromWireBytes_CaseInsensitiveLookup()
    {
        var wire = Encoding.UTF8.GetBytes("NATS/1.0\r\nX-Custom: value\r\n\r\n");
        var headers = NatsHeaders.FromWireBytes(wire, 0, wire.Length);

        headers.GetFirst("x-custom").Should().Be("value");
        headers.GetFirst("X-CUSTOM").Should().Be("value");
    }

    [Fact]
    public void Add_MultipleValues_StoresAll()
    {
        var headers = new NatsHeaders();
        headers.Add("key", "v1");
        headers.Add("key", "v2");

        headers.GetAll("key").Should().HaveCount(2);
        headers.GetAll("key")[0].Should().Be("v1");
        headers.GetAll("key")[1].Should().Be("v2");
    }

    [Fact]
    public void GetFirst_NonExistentKey_ReturnsNull()
    {
        var headers = new NatsHeaders();
        headers.GetFirst("missing").Should().BeNull();
    }

    [Fact]
    public void GetAll_NonExistentKey_ReturnsEmptyList()
    {
        var headers = new NatsHeaders();
        headers.GetAll("missing").Should().BeEmpty();
    }

    [Fact]
    public void ContainsKey_ExistingKey_ReturnsTrue()
    {
        var headers = new NatsHeaders();
        headers.Add("key", "value");
        headers.ContainsKey("key").Should().BeTrue();
    }

    [Fact]
    public void ContainsKey_NonExistentKey_ReturnsFalse()
    {
        var headers = new NatsHeaders();
        headers.ContainsKey("missing").Should().BeFalse();
    }

    [Fact]
    public void RoundTrip_WireBytes_PreservesHeaders()
    {
        var original = new NatsHeaders();
        original.Add("token", "jwt123");
        original.Add("X-Request-Id", "req-456");

        var wire = original.ToWireBytes();
        var parsed = NatsHeaders.FromWireBytes(wire, 0, wire.Length);

        parsed.GetFirst("token").Should().Be("jwt123");
        parsed.GetFirst("X-Request-Id").Should().Be("req-456");
    }
}
