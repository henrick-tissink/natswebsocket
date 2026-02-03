using System.Text;

namespace NatsWebSocket
{
    /// <summary>
    /// An incoming NATS message received via a subscription or request-reply.
    /// </summary>
    public sealed class NatsMsg
    {
        public string Subject { get; internal set; }
        public string Sid { get; internal set; }
        public string ReplyTo { get; internal set; }
        public NatsHeaders Headers { get; internal set; }
        public byte[] Data { get; internal set; }

        /// <summary>
        /// Whether this message has a non-200 status code in its headers.
        /// </summary>
        public bool IsError =>
            Headers != null && Headers.StatusCode.HasValue && Headers.StatusCode.Value != 200;

        /// <summary>
        /// Whether this message is a 503 No Responders status.
        /// </summary>
        public bool IsNoResponders =>
            Headers != null && Headers.StatusCode.HasValue && Headers.StatusCode.Value == 503;

        /// <summary>
        /// Decode the payload as a UTF-8 string.
        /// </summary>
        public string GetString()
        {
            if (Data == null || Data.Length == 0)
                return string.Empty;
            return Encoding.UTF8.GetString(Data);
        }
    }
}
