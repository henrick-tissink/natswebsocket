using System.Collections.Generic;
using FluentAssertions;
using NatsWebSocket.Protocol;
using Xunit;

namespace NatsWebSocket.Tests.Protocol;

public class JsonWriterTests
{
    [Fact]
    public void WriteObject_EmptyFields_ProducesEmptyObject()
    {
        var result = JsonWriter.WriteObject(new List<KeyValuePair<string, object>>());
        result.Should().Be("{}");
    }

    [Fact]
    public void WriteObject_StringValue_ProducesQuotedString()
    {
        var fields = new List<KeyValuePair<string, object>>
        {
            new("name", "test")
        };
        var result = JsonWriter.WriteObject(fields);
        result.Should().Be("{\"name\":\"test\"}");
    }

    [Fact]
    public void WriteObject_BoolValue_ProducesLowercaseBool()
    {
        var fields = new List<KeyValuePair<string, object>>
        {
            new("verbose", false),
            new("headers", true)
        };
        var result = JsonWriter.WriteObject(fields);
        result.Should().Be("{\"verbose\":false,\"headers\":true}");
    }

    [Fact]
    public void WriteObject_IntValue_ProducesNumber()
    {
        var fields = new List<KeyValuePair<string, object>>
        {
            new("protocol", 1)
        };
        var result = JsonWriter.WriteObject(fields);
        result.Should().Be("{\"protocol\":1}");
    }

    [Fact]
    public void WriteObject_NullValue_SkipsField()
    {
        var fields = new List<KeyValuePair<string, object>>
        {
            new("name", "test"),
            new("jwt", null),
            new("sig", "abc")
        };
        var result = JsonWriter.WriteObject(fields);
        result.Should().Be("{\"name\":\"test\",\"sig\":\"abc\"}");
    }

    [Fact]
    public void WriteObject_EscapesSpecialCharacters()
    {
        var fields = new List<KeyValuePair<string, object>>
        {
            new("msg", "line1\nline2\ttab\"quote\\backslash")
        };
        var result = JsonWriter.WriteObject(fields);
        result.Should().Be("{\"msg\":\"line1\\nline2\\ttab\\\"quote\\\\backslash\"}");
    }

    [Fact]
    public void WriteObject_MixedTypes_ProducesCorrectJson()
    {
        var fields = new List<KeyValuePair<string, object>>
        {
            new("verbose", false),
            new("pedantic", false),
            new("lang", "csharp"),
            new("version", "1.0.0"),
            new("protocol", 1),
            new("headers", true),
            new("no_responders", true)
        };
        var result = JsonWriter.WriteObject(fields);
        result.Should().Be("{\"verbose\":false,\"pedantic\":false,\"lang\":\"csharp\",\"version\":\"1.0.0\",\"protocol\":1,\"headers\":true,\"no_responders\":true}");
    }
}

public class JsonReaderTests
{
    [Fact]
    public void Parse_EmptyObject_ReturnsEmptyDictionary()
    {
        var result = JsonReader.Parse("{}");
        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_StringValues_ExtractsCorrectly()
    {
        var result = JsonReader.Parse("{\"server_id\":\"abc123\",\"version\":\"2.10.0\"}");
        result["server_id"].Should().Be("abc123");
        result["version"].Should().Be("2.10.0");
    }

    [Fact]
    public void Parse_NumberValues_ExtractsCorrectly()
    {
        var result = JsonReader.Parse("{\"port\":4222,\"max_payload\":1048576,\"proto\":1}");
        result["port"].Should().Be(4222L);
        result["max_payload"].Should().Be(1048576L);
        result["proto"].Should().Be(1L);
    }

    [Fact]
    public void Parse_BooleanValues_ExtractsCorrectly()
    {
        var result = JsonReader.Parse("{\"headers\":true,\"auth_required\":false}");
        result["headers"].Should().Be(true);
        result["auth_required"].Should().Be(false);
    }

    [Fact]
    public void Parse_WithWhitespace_ExtractsCorrectly()
    {
        var json = "{ \"key\" : \"value\" , \"num\" : 42 }";
        var result = JsonReader.Parse(json);
        result["key"].Should().Be("value");
        result["num"].Should().Be(42L);
    }

    [Fact]
    public void Parse_WithEscapedStrings_ExtractsCorrectly()
    {
        var result = JsonReader.Parse("{\"msg\":\"hello\\nworld\"}");
        result["msg"].Should().Be("hello\nworld");
    }

    [Fact]
    public void Parse_SkipsArrayValues()
    {
        var json = "{\"server_id\":\"test\",\"connect_urls\":[\"url1\",\"url2\"],\"version\":\"2.10.0\"}";
        var result = JsonReader.Parse(json);
        result["server_id"].Should().Be("test");
        result["version"].Should().Be("2.10.0");
    }

    [Fact]
    public void ParseServerInfo_ExtractsAllFields()
    {
        var json = "{\"server_id\":\"SRV1\",\"server_name\":\"main\",\"version\":\"2.10.0\",\"host\":\"0.0.0.0\",\"port\":4222,\"headers\":true,\"auth_required\":true,\"max_payload\":1048576,\"proto\":1,\"nonce\":\"abc123\"}";
        var info = JsonReader.ParseServerInfo(json);

        info.ServerId.Should().Be("SRV1");
        info.ServerName.Should().Be("main");
        info.Version.Should().Be("2.10.0");
        info.Host.Should().Be("0.0.0.0");
        info.Port.Should().Be(4222);
        info.HeadersSupported.Should().BeTrue();
        info.AuthRequired.Should().BeTrue();
        info.MaxPayload.Should().Be(1048576);
        info.ProtocolVersion.Should().Be(1);
        info.Nonce.Should().Be("abc123");
    }

    [Fact]
    public void Parse_IsCaseInsensitive()
    {
        var result = JsonReader.Parse("{\"Server_Id\":\"test\"}");
        result["server_id"].Should().Be("test");
    }

    [Fact]
    public void Parse_NullValues_AreParsed()
    {
        var result = JsonReader.Parse("{\"nonce\":null,\"server_id\":\"test\"}");
        result.Should().ContainKey("nonce");
        result["nonce"].Should().BeNull();
        result["server_id"].Should().Be("test");
    }
}
