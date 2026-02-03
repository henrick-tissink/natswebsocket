using System;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace NatsWebSocket.Auth
{
    /// <summary>
    /// Ed25519 signing for NATS NKEY authentication using BouncyCastle.
    /// </summary>
    public static class NKeySigner
    {
        /// <summary>
        /// Sign data using an Ed25519 private key derived from the 32-byte seed.
        /// </summary>
        public static byte[] Sign(byte[] seed, byte[] data)
        {
            if (seed == null) throw new ArgumentNullException(nameof(seed));
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (seed.Length != 32) throw new ArgumentException("Seed must be exactly 32 bytes", nameof(seed));

            var privateKey = new Ed25519PrivateKeyParameters(seed, 0);
            var signer = new Ed25519Signer();
            signer.Init(true, privateKey);
            signer.BlockUpdate(data, 0, data.Length);
            return signer.GenerateSignature();
        }
    }
}
