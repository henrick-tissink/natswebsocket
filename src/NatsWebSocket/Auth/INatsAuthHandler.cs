namespace NatsWebSocket.Auth
{
    /// <summary>
    /// Pluggable authentication handler for NATS connections.
    /// Implementations provide credentials for the CONNECT command.
    /// </summary>
    public interface INatsAuthHandler
    {
        /// <summary>
        /// Sign the server nonce (if present) and return auth fields for the CONNECT command.
        /// </summary>
        NatsAuthResult Authenticate(string nonce);
    }

    /// <summary>
    /// Authentication result containing fields to include in the CONNECT command.
    /// </summary>
    public sealed class NatsAuthResult
    {
        public string Jwt { get; set; }
        public string Signature { get; set; }
        public string AuthToken { get; set; }
        public string User { get; set; }
        public string Pass { get; set; }
        public string Nkey { get; set; }
    }
}
