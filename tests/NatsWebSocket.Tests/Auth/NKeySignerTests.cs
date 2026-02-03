using System;
using System.Text;
using FluentAssertions;
using NatsWebSocket.Auth;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Xunit;

namespace NatsWebSocket.Tests.Auth;

public class NKeySignerTests
{
    [Fact]
    public void Sign_ProducesValidEd25519Signature()
    {
        // Generate a deterministic 32-byte seed
        var seed = new byte[32];
        for (int i = 0; i < 32; i++)
            seed[i] = (byte)i;

        var data = Encoding.UTF8.GetBytes("test nonce data");

        var signature = NKeySigner.Sign(seed, data);

        signature.Should().HaveCount(64); // Ed25519 signatures are always 64 bytes

        // Verify with BouncyCastle
        var privateKey = new Ed25519PrivateKeyParameters(seed, 0);
        var publicKey = privateKey.GeneratePublicKey();
        var verifier = new Ed25519Signer();
        verifier.Init(false, publicKey);
        verifier.BlockUpdate(data, 0, data.Length);
        verifier.VerifySignature(signature).Should().BeTrue();
    }

    [Fact]
    public void Sign_DifferentData_ProducesDifferentSignatures()
    {
        var seed = new byte[32];
        for (int i = 0; i < 32; i++)
            seed[i] = (byte)(i + 100);

        var sig1 = NKeySigner.Sign(seed, Encoding.UTF8.GetBytes("data1"));
        var sig2 = NKeySigner.Sign(seed, Encoding.UTF8.GetBytes("data2"));

        sig1.Should().NotBeEquivalentTo(sig2);
    }

    [Fact]
    public void Sign_NullSeed_ThrowsArgumentNullException()
    {
        Action act = () => NKeySigner.Sign(null, Encoding.UTF8.GetBytes("data"));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Sign_NullData_ThrowsArgumentNullException()
    {
        Action act = () => NKeySigner.Sign(new byte[32], null);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Sign_WrongSeedLength_ThrowsArgumentException()
    {
        Action act = () => NKeySigner.Sign(new byte[16], Encoding.UTF8.GetBytes("data"));
        act.Should().Throw<ArgumentException>();
    }
}
