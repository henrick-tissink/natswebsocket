using System;

namespace NatsWebSocket.Auth
{
    /// <summary>
    /// Simple token authentication handler.
    /// </summary>
    public sealed class TokenAuthHandler : INatsAuthHandler
    {
        private readonly string _token;

        public TokenAuthHandler(string token)
        {
            _token = token ?? throw new ArgumentNullException(nameof(token));
        }

        public NatsAuthResult Authenticate(string nonce)
        {
            return new NatsAuthResult { AuthToken = _token };
        }
    }
}
