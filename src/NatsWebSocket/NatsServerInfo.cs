namespace NatsWebSocket
{
    /// <summary>
    /// Public representation of NATS server information received during the handshake.
    /// </summary>
    public sealed class NatsServerInfo
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

        internal static NatsServerInfo FromInternal(Protocol.ServerInfo info)
        {
            if (info == null) return null;
            return new NatsServerInfo
            {
                ServerId = info.ServerId,
                ServerName = info.ServerName,
                Version = info.Version,
                Host = info.Host,
                Port = info.Port,
                HeadersSupported = info.HeadersSupported,
                AuthRequired = info.AuthRequired,
                MaxPayload = info.MaxPayload,
                ProtocolVersion = info.ProtocolVersion,
                Nonce = info.Nonce,
            };
        }
    }
}
