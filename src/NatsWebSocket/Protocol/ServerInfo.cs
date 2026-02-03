namespace NatsWebSocket.Protocol
{
    /// <summary>
    /// Internal parsed NATS INFO message fields.
    /// </summary>
    internal sealed class ServerInfo
    {
        public string ServerId { get; set; }
        public string ServerName { get; set; }
        public string Version { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public bool HeadersSupported { get; set; }
        public bool AuthRequired { get; set; }
        public int MaxPayload { get; set; }
        public int ProtocolVersion { get; set; }
        public string Nonce { get; set; }
    }
}
