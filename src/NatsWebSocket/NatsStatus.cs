using System;

namespace NatsWebSocket
{
    /// <summary>
    /// Represents the current state of a NATS connection.
    /// </summary>
    public enum NatsStatus
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting,
        Closed
    }

    /// <summary>
    /// Event args for connection status changes.
    /// </summary>
    public class NatsStatusEventArgs : EventArgs
    {
        public NatsStatus Status { get; }

        public NatsStatusEventArgs(NatsStatus status)
        {
            Status = status;
        }
    }

    /// <summary>
    /// Event args for connection errors.
    /// </summary>
    public class NatsErrorEventArgs : EventArgs
    {
        public Exception Exception { get; }

        public NatsErrorEventArgs(Exception exception)
        {
            Exception = exception;
        }
    }
}
