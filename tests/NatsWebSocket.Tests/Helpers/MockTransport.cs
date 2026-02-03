using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NatsWebSocket.Transport;

namespace NatsWebSocket.Tests.Helpers
{
    /// <summary>
    /// Mock transport for testing NatsConnection without a real WebSocket.
    /// Queues inbound data for the connection to read, and captures outbound data.
    /// </summary>
    internal sealed class MockTransport : ITransport
    {
        private readonly ConcurrentQueue<byte[]> _inbound = new ConcurrentQueue<byte[]>();
        private readonly SemaphoreSlim _inboundSignal = new SemaphoreSlim(0);
        private readonly List<byte[]> _sent = new List<byte[]>();
        private readonly object _sentLock = new object();

        private bool _isConnected;
        private bool _connectShouldFail;
        private bool _neverComplete;

        public bool IsConnected => _isConnected;

        /// <summary>
        /// All bytes sent by the connection, in order.
        /// </summary>
        public List<byte[]> SentMessages
        {
            get { lock (_sentLock) return new List<byte[]>(_sent); }
        }

        /// <summary>
        /// Get all sent data concatenated as a string (for protocol inspection).
        /// </summary>
        public string SentText
        {
            get
            {
                lock (_sentLock)
                {
                    var sb = new StringBuilder();
                    foreach (var msg in _sent)
                        sb.Append(Encoding.UTF8.GetString(msg));
                    return sb.ToString();
                }
            }
        }

        /// <summary>
        /// Configure the transport to fail on connect.
        /// </summary>
        public void SetConnectShouldFail(bool fail) => _connectShouldFail = fail;

        /// <summary>
        /// Configure the transport to never complete connect (for timeout testing).
        /// </summary>
        public void SetNeverComplete(bool neverComplete)
        {
            _neverComplete = neverComplete;
        }

        /// <summary>
        /// Enqueue data that the connection will read.
        /// </summary>
        public void EnqueueInbound(string natsProtocolLine)
        {
            var data = Encoding.UTF8.GetBytes(natsProtocolLine);
            _inbound.Enqueue(data);
            _inboundSignal.Release();
        }

        /// <summary>
        /// Enqueue raw bytes that the connection will read.
        /// </summary>
        public void EnqueueInbound(byte[] data)
        {
            _inbound.Enqueue(data);
            _inboundSignal.Release();
        }

        /// <summary>
        /// Simulate server disconnect by setting IsConnected=false and signaling.
        /// </summary>
        public void SimulateDisconnect()
        {
            _isConnected = false;
            // Enqueue empty to unblock any pending ReceiveAsync
            _inbound.Enqueue(Array.Empty<byte>());
            _inboundSignal.Release();
        }

        public async Task ConnectAsync(Uri uri, CancellationToken ct)
        {
            if (_connectShouldFail)
                throw new Exception("Connection refused");

            if (_neverComplete)
            {
                // Block until cancellation — properly observes the token
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (ct.Register(() => tcs.TrySetCanceled(ct)))
                    await tcs.Task.ConfigureAwait(false);
                return;
            }

            _isConnected = true;
        }

        public async Task<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            if (!_isConnected) return 0;

            try
            {
                await _inboundSignal.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
            catch (ObjectDisposedException)
            {
                return 0;
            }

            if (_inbound.TryDequeue(out var data))
            {
                if (data.Length == 0)
                    return 0; // simulate disconnect

                var toCopy = Math.Min(data.Length, count);
                Buffer.BlockCopy(data, 0, buffer, offset, toCopy);
                return toCopy;
            }

            return 0;
        }

        public Task SendAsync(byte[] data, CancellationToken ct)
        {
            if (!_isConnected)
                throw new InvalidOperationException("Not connected");

            lock (_sentLock)
            {
                var copy = new byte[data.Length];
                Buffer.BlockCopy(data, 0, copy, 0, data.Length);
                _sent.Add(copy);
            }
            return Task.CompletedTask;
        }

        public Task CloseAsync(CancellationToken ct)
        {
            _isConnected = false;
            // Unblock any pending ReceiveAsync
            try { _inboundSignal.Release(); }
            catch { /* already disposed or max count reached */ }
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _isConnected = false;
        }
    }
}
