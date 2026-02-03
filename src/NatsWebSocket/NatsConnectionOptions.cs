using System;
using NatsWebSocket.Auth;

namespace NatsWebSocket
{
    /// <summary>
    /// Configuration options for a NATS connection.
    /// </summary>
    public sealed class NatsConnectionOptions
    {
        /// <summary>
        /// NATS server URL. Must be ws:// or wss://.
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// Pluggable authentication handler (NKEY, token, or user/password).
        /// </summary>
        public INatsAuthHandler AuthHandler { get; set; }

        /// <summary>
        /// Client name sent in the CONNECT command.
        /// </summary>
        public string Name { get; set; } = "NatsWebSocket";

        /// <summary>
        /// Timeout for the initial WebSocket + NATS handshake.
        /// </summary>
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Default timeout for request-reply operations.
        /// </summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Whether to automatically reconnect on disconnect.
        /// </summary>
        public bool AllowReconnect { get; set; } = true;

        /// <summary>
        /// Maximum number of reconnect attempts. -1 for unlimited.
        /// </summary>
        public int MaxReconnectAttempts { get; set; } = -1;

        /// <summary>
        /// Initial delay between reconnect attempts. Doubles each attempt.
        /// </summary>
        public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Maximum delay between reconnect attempts.
        /// </summary>
        public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Whether to add +/- 25% random jitter to reconnect delays.
        /// </summary>
        public bool ReconnectJitter { get; set; } = true;

        /// <summary>
        /// Whether to advertise header support in the CONNECT command.
        /// </summary>
        public bool Headers { get; set; } = true;

        /// <summary>
        /// Whether to request 503 No Responders status headers.
        /// </summary>
        public bool NoResponders { get; set; } = true;

        /// <summary>
        /// Size of the receive buffer for the WebSocket read loop.
        /// </summary>
        public int ReceiveBufferSize { get; set; } = 65536;

        /// <summary>
        /// Interval between PING keep-alive messages sent to the server.
        /// </summary>
        public TimeSpan PingInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Number of consecutive missed PONGs before triggering a disconnect/reconnect.
        /// </summary>
        public int MaxPingOut { get; set; } = 3;
    }
}
