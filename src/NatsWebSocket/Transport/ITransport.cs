using System;
using System.Threading;
using System.Threading.Tasks;

namespace NatsWebSocket.Transport
{
    /// <summary>
    /// Abstraction over the WebSocket transport for testability.
    /// </summary>
    internal interface ITransport : IDisposable
    {
        bool IsConnected { get; }
        Task ConnectAsync(Uri uri, CancellationToken ct);
        Task<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken ct);
        Task SendAsync(byte[] data, CancellationToken ct);
        Task CloseAsync(CancellationToken ct);
    }
}
