using System;
using System.Threading;

namespace NatsWebSocket
{
    /// <summary>
    /// Handle to an active NATS subscription. Dispose to unsubscribe.
    /// </summary>
    public sealed class NatsSubscription : IDisposable
    {
        private readonly Action<string> _unsubscribe;
        private int _disposed;

        internal string Sid { get; }
        internal string Subject { get; }

        internal NatsSubscription(string sid, string subject, Action<string> unsubscribe)
        {
            Sid = sid;
            Subject = subject;
            _unsubscribe = unsubscribe;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _unsubscribe?.Invoke(Sid);
        }
    }
}
