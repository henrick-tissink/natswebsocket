using System;
using System.Text;
using FluentAssertions;
using NatsWebSocket.Auth;
using Xunit;

namespace NatsWebSocket.Tests.Auth;

public class Base32Tests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("MY======", "f")]
    [InlineData("MZXQ====", "fo")]
    [InlineData("MZXW6===", "foo")]
    [InlineData("MZXW6YQ=", "foob")]
    [InlineData("MZXW6YTB", "fooba")]
    [InlineData("MZXW6YTBOI======", "foobar")]
    public void Decode_Rfc4648Vectors(string encoded, string expected)
    {
        var result = Base32.Decode(encoded);
        Encoding.UTF8.GetString(result).Should().Be(expected);
    }

    [Fact]
    public void Decode_WithoutPadding_DecodesCorrectly()
    {
        var result = Base32.Decode("MZXW6YTB");
        Encoding.UTF8.GetString(result).Should().Be("fooba");
    }

    [Fact]
    public void Decode_CaseInsensitive()
    {
        var result = Base32.Decode("mzxw6ytb");
        Encoding.UTF8.GetString(result).Should().Be("fooba");
    }

    [Fact]
    public void Decode_InvalidCharacter_ThrowsFormatException()
    {
        Action act = () => Base32.Decode("INVALID!");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Decode_NullInput_ThrowsArgumentNullException()
    {
        Action act = () => Base32.Decode(null);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Encode_EmptyInput_ReturnsEmptyString()
    {
        Base32.Encode(Array.Empty<byte>()).Should().BeEmpty();
    }

    [Fact]
    public void Encode_NullInput_ThrowsArgumentNullException()
    {
        Action act = () => Base32.Encode(null);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RoundTrip_PreservesData()
    {
        var original = new byte[] { 0x01, 0x02, 0x03, 0xFF, 0xFE, 0xFD };
        var encoded = Base32.Encode(original);
        var decoded = Base32.Decode(encoded);

        decoded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Decode_NkeySeedFormat_ExtractsSeedBytes()
    {
        // Simulate an NKEY seed: 2-byte prefix + 32-byte seed + 2-byte CRC = 36 bytes
        var raw = new byte[36];
        raw[0] = 0x90; // prefix (seed)
        raw[1] = 0xA0; // type (user)
        for (int i = 0; i < 32; i++)
            raw[i + 2] = (byte)(i + 1);
        raw[34] = 0xAA; // CRC lo
        raw[35] = 0xBB; // CRC hi

        var encoded = Base32.Encode(raw);
        var decoded = Base32.Decode(encoded);

        decoded.Should().HaveCount(36);
        decoded[0].Should().Be(0x90);
        decoded[1].Should().Be(0xA0);
        decoded[2].Should().Be(1);
        decoded[33].Should().Be(32);
    }
}
