using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace NatsWebSocket.Transport
{
    /// <summary>
    /// WebSocket transport implementation wrapping System.Net.WebSockets.ClientWebSocket.
    /// Compatible with .NET Framework 4.6.2+ and .NET Standard 2.0.
    /// </summary>
    internal sealed class WebSocketTransport : ITransport
    {
        private ClientWebSocket _ws;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

        public bool IsConnected => _ws?.State == WebSocketState.Open;

        public async Task ConnectAsync(Uri uri, CancellationToken ct)
        {
            // Ensure TLS 1.2 is enabled (needed for .NET Framework 4.6.2)
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(uri, ct).ConfigureAwait(false);
        }

        public async Task<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            if (_ws == null || _ws.State != WebSocketState.Open)
                throw new NatsConnectionException("WebSocket is not connected");

            var result = await _ws.ReceiveAsync(
                new ArraySegment<byte>(buffer, offset, count), ct).ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
                return 0; // Signal close

            return result.Count;
        }

        public async Task SendAsync(byte[] data, CancellationToken ct)
        {
            if (_ws == null || _ws.State != WebSocketState.Open)
                throw new NatsConnectionException("WebSocket is not connected");

            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _ws.SendAsync(
                    new ArraySegment<byte>(data),
                    WebSocketMessageType.Binary,
                    true,
                    ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task CloseAsync(CancellationToken ct)
        {
            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                try
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", ct).ConfigureAwait(false);
                }
                catch
                {
                    // Best effort close
                }
            }
        }

        public void Dispose()
        {
            _ws?.Dispose();
            _writeLock.Dispose();
        }
    }
}
