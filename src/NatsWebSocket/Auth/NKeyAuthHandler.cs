using System;
using System.Text;

namespace NatsWebSocket.Auth
{
    /// <summary>
    /// NKEY + JWT authentication handler. Reads credentials from a .creds file
    /// or accepts raw JWT and seed values directly.
    /// </summary>
    public sealed class NKeyAuthHandler : INatsAuthHandler
    {
        private readonly string _jwt;
        private readonly byte[] _seed;

        /// <summary>
        /// Create from a .creds file path.
        /// </summary>
        public NKeyAuthHandler(string credsPath)
        {
            if (credsPath == null) throw new ArgumentNullException(nameof(credsPath));
            _jwt = CredentialFile.ExtractJwt(credsPath);
            _seed = CredentialFile.ExtractSeed(credsPath);
        }

        /// <summary>
        /// Create from raw JWT string and 32-byte NKEY seed.
        /// </summary>
        public NKeyAuthHandler(string jwt, byte[] nkeySeed)
        {
            _jwt = jwt ?? throw new ArgumentNullException(nameof(jwt));
            _seed = nkeySeed ?? throw new ArgumentNullException(nameof(nkeySeed));
            if (_seed.Length != 32)
                throw new ArgumentException("NKEY seed must be exactly 32 bytes", nameof(nkeySeed));
        }

        /// <summary>
        /// The JWT token extracted from the credentials.
        /// </summary>
        public string Jwt => _jwt;

        public NatsAuthResult Authenticate(string nonce)
        {
            var result = new NatsAuthResult { Jwt = _jwt };

            if (!string.IsNullOrEmpty(nonce))
            {
                var sigBytes = NKeySigner.Sign(_seed, Encoding.UTF8.GetBytes(nonce));
                result.Signature = Convert.ToBase64String(sigBytes);
            }

            return result;
        }
    }
}
