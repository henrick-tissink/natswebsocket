using System;
using System.Threading;
using System.Threading.Tasks;

namespace NatsWebSocket
{
    /// <summary>
    /// Public interface for a NATS connection over WebSocket.
    /// </summary>
    public interface INatsConnection : IDisposable
    {
        /// <summary>
        /// Current connection status.
        /// </summary>
        NatsStatus Status { get; }

        /// <summary>
        /// Server information received during the initial handshake.
        /// </summary>
        NatsServerInfo ServerInfo { get; }

        /// <summary>
        /// Raised when the connection status changes.
        /// </summary>
        event EventHandler<NatsStatusEventArgs> StatusChanged;

        /// <summary>
        /// Raised when an error occurs (transport failure, server error, etc.).
        /// </summary>
        event EventHandler<NatsErrorEventArgs> Error;

        /// <summary>
        /// Connect to the NATS server and perform the protocol handshake.
        /// </summary>
        Task ConnectAsync(CancellationToken ct = default);

        /// <summary>
        /// Publish a message to a subject.
        /// </summary>
        Task PublishAsync(string subject, byte[] data, NatsHeaders headers = null, CancellationToken ct = default);

        /// <summary>
        /// Send a request and wait for a reply (request-reply pattern).
        /// </summary>
        Task<NatsMsg> RequestAsync(string subject, byte[] data, NatsHeaders headers = null, TimeSpan? timeout = null, CancellationToken ct = default);

        /// <summary>
        /// Subscribe to a subject with a synchronous handler.
        /// </summary>
        Task<NatsSubscription> SubscribeAsync(string subject, Action<NatsMsg> handler, CancellationToken ct = default);

        /// <summary>
        /// Subscribe to a subject with a queue group and synchronous handler.
        /// </summary>
        Task<NatsSubscription> SubscribeAsync(string subject, string queueGroup, Action<NatsMsg> handler, CancellationToken ct = default);

        /// <summary>
        /// Subscribe to a subject with an async handler.
        /// </summary>
        Task<NatsSubscription> SubscribeAsync(string subject, Func<NatsMsg, Task> handler, CancellationToken ct = default);

        /// <summary>
        /// Subscribe to a subject with a queue group and async handler.
        /// </summary>
        Task<NatsSubscription> SubscribeAsync(string subject, string queueGroup, Func<NatsMsg, Task> handler, CancellationToken ct = default);

        /// <summary>
        /// Flush any buffered data to the server by sending PING and awaiting PONG.
        /// </summary>
        Task FlushAsync(CancellationToken ct = default);

        /// <summary>
        /// Gracefully close the connection.
        /// </summary>
        Task CloseAsync(CancellationToken ct = default);
    }
}
