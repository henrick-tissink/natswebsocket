using System;
using System.Threading;
using System.Threading.Tasks;

namespace NatsWebSocket.Subscriptions
{
    /// <summary>
    /// Per-subscription state tracking the subject, SID, queue group, and handler.
    /// </summary>
    internal sealed class SubscriptionState
    {
        public string Sid { get; }
        public string Subject { get; }
        public string QueueGroup { get; }
        public Action<NatsMsg> SyncHandler { get; }
        public Func<NatsMsg, Task> AsyncHandler { get; }
        private int _active = 1;

        public bool IsActive => Interlocked.CompareExchange(ref _active, 0, 0) == 1;

        public SubscriptionState(string sid, string subject, string queueGroup, Action<NatsMsg> handler)
        {
            Sid = sid;
            Subject = subject;
            QueueGroup = queueGroup;
            SyncHandler = handler;
        }

        public SubscriptionState(string sid, string subject, string queueGroup, Func<NatsMsg, Task> handler)
        {
            Sid = sid;
            Subject = subject;
            QueueGroup = queueGroup;
            AsyncHandler = handler;
        }

        public void Deactivate()
        {
            Interlocked.Exchange(ref _active, 0);
        }

        public void Dispatch(NatsMsg msg, Action<Exception> onError = null)
        {
            if (!IsActive) return;

            if (SyncHandler != null)
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { SyncHandler(msg); }
                    catch (Exception ex) { onError?.Invoke(ex); }
                }, null);
            }
            else if (AsyncHandler != null)
            {
                var handler = AsyncHandler;
                Task.Run(async () =>
                {
                    try { await handler(msg).ConfigureAwait(false); }
                    catch (Exception ex) { onError?.Invoke(ex); }
                });
            }
        }
    }
}
