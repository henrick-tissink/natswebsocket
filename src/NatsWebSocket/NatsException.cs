using System;

namespace NatsWebSocket
{
    /// <summary>
    /// Base exception for all NATS-related errors.
    /// </summary>
    public class NatsException : Exception
    {
        public NatsException(string message) : base(message) { }
        public NatsException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Thrown when the NATS transport (WebSocket) fails to connect or disconnects unexpectedly.
    /// </summary>
    public class NatsConnectionException : NatsException
    {
        public NatsConnectionException(string message) : base(message) { }
        public NatsConnectionException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Thrown when the NATS server rejects authentication credentials.
    /// </summary>
    public class NatsAuthException : NatsException
    {
        public NatsAuthException(string message) : base(message) { }
        public NatsAuthException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Thrown when a NATS request times out waiting for a reply.
    /// </summary>
    public class NatsRequestTimeoutException : NatsException
    {
        public string Subject { get; }
        public TimeSpan Timeout { get; }

        public NatsRequestTimeoutException(string subject, TimeSpan timeout)
            : base($"Request to '{subject}' timed out after {timeout.TotalSeconds:F1}s")
        {
            Subject = subject;
            Timeout = timeout;
        }
    }

    /// <summary>
    /// Thrown when a NATS request receives a 503 No Responders status.
    /// </summary>
    public class NatsNoRespondersException : NatsException
    {
        public string Subject { get; }

        public NatsNoRespondersException(string subject)
            : base($"No responders for subject '{subject}'")
        {
            Subject = subject;
        }
    }

    /// <summary>
    /// Thrown when the NATS server sends a -ERR response.
    /// </summary>
    public class NatsServerException : NatsException
    {
        public string ServerError { get; }

        public NatsServerException(string serverError)
            : base($"NATS server error: {serverError}")
        {
            ServerError = serverError;
        }
    }
}
