using System;

namespace NatsWebSocket.JetStream
{
    /// <summary>
    /// Exception thrown for JetStream-specific errors.
    /// </summary>
    public class NatsJSException : NatsException
    {
        /// <summary>
        /// JetStream error code.
        /// </summary>
        public int Code { get; }

        /// <summary>
        /// JetStream-specific error code (e.g., 10059 for "no message found").
        /// </summary>
        public int ErrCode { get; }

        public NatsJSException(string message, int code = 0, int errCode = 0)
            : base(message)
        {
            Code = code;
            ErrCode = errCode;
        }

        public NatsJSException(string message, int code, Exception innerException)
            : base(message, innerException)
        {
            Code = code;
        }
    }

    /// <summary>
    /// Thrown when a JetStream publish fails.
    /// </summary>
    public class NatsJSPublishException : NatsJSException
    {
        public string Stream { get; }

        public NatsJSPublishException(string message, string stream = null)
            : base(message)
        {
            Stream = stream;
        }
    }

    /// <summary>
    /// Thrown when a stream or bucket is not found.
    /// </summary>
    public class NatsJSStreamNotFoundException : NatsJSException
    {
        public string StreamName { get; }

        public NatsJSStreamNotFoundException(string streamName)
            : base($"Stream '{streamName}' not found", 404, 10059)
        {
            StreamName = streamName;
        }
    }

    /// <summary>
    /// Thrown when an object is not found in the object store.
    /// </summary>
    public class NatsObjNotFoundException : NatsJSException
    {
        public string Bucket { get; }
        public string ObjectName { get; }

        public NatsObjNotFoundException(string bucket, string objectName)
            : base($"Object '{objectName}' not found in bucket '{bucket}'", 404)
        {
            Bucket = bucket;
            ObjectName = objectName;
        }
    }

    /// <summary>
    /// General Object Store exception (data corruption, missing chunks, etc.).
    /// </summary>
    public class NatsObjException : NatsJSException
    {
        public NatsObjException(string message)
            : base(message)
        {
        }

        public NatsObjException(string message, Exception innerException)
            : base(message, 0, innerException)
        {
        }
    }
}
