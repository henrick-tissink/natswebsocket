using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NatsWebSocket.Protocol;

namespace NatsWebSocket.Subscriptions
{
    /// <summary>
    /// Manages subscriptions, SID allocation, dispatch, and resubscription on reconnect.
    /// </summary>
    internal sealed class SubscriptionManager
    {
        private readonly Action<Exception> _onError;
        private int _sidCounter;
        private readonly ConcurrentDictionary<string, SubscriptionState> _subscriptions =
            new ConcurrentDictionary<string, SubscriptionState>();

        public SubscriptionManager(Action<Exception> onError = null)
        {
            _onError = onError;
        }

        public string NextSid()
        {
            return Interlocked.Increment(ref _sidCounter).ToString();
        }

        public SubscriptionState Add(string subject, string queueGroup, Action<NatsMsg> handler)
        {
            var sid = NextSid();
            var state = new SubscriptionState(sid, subject, queueGroup, handler);
            _subscriptions[sid] = state;
            return state;
        }

        public SubscriptionState AddAsync(string subject, string queueGroup, Func<NatsMsg, Task> handler)
        {
            var sid = NextSid();
            var state = new SubscriptionState(sid, subject, queueGroup, handler);
            _subscriptions[sid] = state;
            return state;
        }

        public void Remove(string sid)
        {
            if (_subscriptions.TryRemove(sid, out var state))
            {
                state.Deactivate();
            }
        }

        public bool TryGet(string sid, out SubscriptionState state)
        {
            return _subscriptions.TryGetValue(sid, out state);
        }

        /// <summary>
        /// Dispatch a message to the appropriate subscription handler by SID.
        /// </summary>
        public void Dispatch(ParsedMsg parsed)
        {
            if (parsed.Sid == null) return;

            if (_subscriptions.TryGetValue(parsed.Sid, out var state) && state.IsActive)
            {
                var msg = new NatsMsg
                {
                    Subject = parsed.Subject,
                    Sid = parsed.Sid,
                    ReplyTo = parsed.ReplyTo,
                    Data = parsed.Payload ?? Array.Empty<byte>()
                };

                if (parsed.HeaderBytes != null && parsed.HeaderBytes.Length > 0)
                {
                    msg.Headers = NatsHeaders.FromWireBytes(parsed.HeaderBytes, 0, parsed.HeaderBytes.Length);
                }

                state.Dispatch(msg, _onError);
            }
        }

        /// <summary>
        /// Get SUB commands for all active subscriptions (for reconnection).
        /// </summary>
        public List<byte[]> GetResubscribeCommands()
        {
            var commands = new List<byte[]>();
            foreach (var kvp in _subscriptions)
            {
                var state = kvp.Value;
                if (state.IsActive)
                {
                    commands.Add(ProtocolWriter.Sub(state.Subject, state.Sid, state.QueueGroup));
                }
            }
            return commands;
        }

        public int Count => _subscriptions.Count;
    }
}
