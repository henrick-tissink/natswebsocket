using System;
using System.IO;
using FluentAssertions;
using NatsWebSocket.Auth;
using Xunit;

namespace NatsWebSocket.Tests.Auth;

public class CredentialFileTests
{
    [Fact]
    public void ExtractJwtFromText_ValidCredsContent_ExtractsJwt()
    {
        var content = @"-----BEGIN NATS USER JWT-----
eyJhbGciOiJFZDI1NTE5IiwidHlwIjoiSldUIn0.test_jwt_content.signature
------END NATS USER JWT------

-----BEGIN USER NKEY SEED-----
SUAM6L2AXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX
------END USER NKEY SEED------";

        var jwt = CredentialFile.ExtractJwtFromText(content);
        jwt.Should().Be("eyJhbGciOiJFZDI1NTE5IiwidHlwIjoiSldUIn0.test_jwt_content.signature");
    }

    [Fact]
    public void ExtractJwtFromText_NoJwtBlock_ThrowsNatsAuthException()
    {
        var content = "no jwt here";
        Action act = () => CredentialFile.ExtractJwtFromText(content);
        act.Should().Throw<NatsAuthException>();
    }

    [Fact]
    public void ExtractSeedFromText_InvalidSeedLength_ThrowsNatsAuthException()
    {
        // "AA" base32 decodes to a single byte — way too short for an NKEY seed
        var content = @"-----BEGIN USER NKEY SEED-----
AA
------END USER NKEY SEED------";

        Action act = () => CredentialFile.ExtractSeedFromText(content);
        act.Should().Throw<NatsAuthException>().WithMessage("*Invalid NKEY seed length*");
    }

    [Fact]
    public void ExtractSeedFromText_NoSeedBlock_ThrowsNatsAuthException()
    {
        var content = "no seed here";
        Action act = () => CredentialFile.ExtractSeedFromText(content);
        act.Should().Throw<NatsAuthException>();
    }

    [Fact]
    public void ExtractSeedFromText_ValidSeed_Returns32Bytes()
    {
        // Build a valid NKEY seed: 2-byte prefix + 32-byte seed + 2-byte CRC
        var raw = new byte[36];
        raw[0] = 0x90;
        raw[1] = 0xA0;
        for (int i = 0; i < 32; i++)
            raw[i + 2] = (byte)i;
        var crc = CredentialFile.Crc16(raw, 0, 34);
        raw[34] = (byte)(crc & 0xFF);
        raw[35] = (byte)((crc >> 8) & 0xFF);

        var encoded = Base32.Encode(raw);
        var content = $@"-----BEGIN USER NKEY SEED-----
{encoded}
------END USER NKEY SEED------";

        var seed = CredentialFile.ExtractSeedFromText(content);
        seed.Should().HaveCount(32);
        seed[0].Should().Be(0);
        seed[31].Should().Be(31);
    }

    [Fact]
    public void ExtractSeedFromText_CorruptCrc_ThrowsNatsAuthException()
    {
        var raw = new byte[36];
        raw[0] = 0x90;
        raw[1] = 0xA0;
        for (int i = 0; i < 32; i++)
            raw[i + 2] = (byte)i;
        raw[34] = 0xFF; // intentionally wrong CRC
        raw[35] = 0xFE;

        var encoded = Base32.Encode(raw);
        var content = $@"-----BEGIN USER NKEY SEED-----
{encoded}
------END USER NKEY SEED------";

        Action act = () => CredentialFile.ExtractSeedFromText(content);
        act.Should().Throw<NatsAuthException>().WithMessage("*CRC validation failed*");
    }
}
