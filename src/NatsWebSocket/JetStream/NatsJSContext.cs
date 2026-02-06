using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NatsWebSocket.JetStream.Internal;
using NatsWebSocket.JetStream.Models;

namespace NatsWebSocket.JetStream
{
    /// <summary>
    /// JetStream context providing access to JetStream API operations.
    /// </summary>
    public class NatsJSContext
    {
        private readonly INatsConnection _connection;
        private readonly string _prefix;
        private readonly TimeSpan _timeout;
        private readonly NatsHeaders _headers;

        /// <summary>
        /// Default API prefix for JetStream.
        /// </summary>
        public const string DefaultApiPrefix = "$JS.API";

        /// <summary>
        /// Creates a new JetStream context.
        /// </summary>
        /// <param name="connection">NATS connection</param>
        /// <param name="domain">Optional JetStream domain</param>
        /// <param name="timeout">API request timeout</param>
        /// <param name="headers">Optional headers to include with all JetStream API requests (e.g., JWT token)</param>
        public NatsJSContext(INatsConnection connection, string domain = null, TimeSpan? timeout = null, NatsHeaders headers = null)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _prefix = string.IsNullOrEmpty(domain) ? DefaultApiPrefix : $"$JS.{domain}.API";
            _timeout = timeout ?? TimeSpan.FromSeconds(30);
            _headers = headers;
        }

        /// <summary>
        /// Get the underlying NATS connection.
        /// </summary>
        public INatsConnection Connection => _connection;

        #region Stream Management

        /// <summary>
        /// Create or update a stream.
        /// </summary>
        public async Task<StreamInfo> CreateStreamAsync(StreamConfig config, CancellationToken ct = default)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrEmpty(config.Name)) throw new ArgumentException("Stream name is required");

            var subject = $"{_prefix}.STREAM.CREATE.{config.Name}";
            var response = await RequestAsync<StreamInfoResponse>(subject, config, ct).ConfigureAwait(false);

            if (response.Error != null)
            {
                throw new NatsJSException(response.Error.Description, response.Error.Code, response.Error.ErrCode);
            }

            return new StreamInfo
            {
                Config = response.Config,
                State = response.State,
                Created = response.Created
            };
        }

        /// <summary>
        /// Get information about a stream.
        /// </summary>
        public async Task<StreamInfo> GetStreamAsync(string streamName, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(streamName)) throw new ArgumentNullException(nameof(streamName));

            var subject = $"{_prefix}.STREAM.INFO.{streamName}";
            var response = await RequestAsync<StreamInfoResponse>(subject, null, ct).ConfigureAwait(false);

            if (response.Error != null)
            {
                if (response.Error.Code == 404 || response.Error.ErrCode == 10059)
                    throw new NatsJSStreamNotFoundException(streamName);
                throw new NatsJSException(response.Error.Description, response.Error.Code, response.Error.ErrCode);
            }

            return new StreamInfo
            {
                Config = response.Config,
                State = response.State,
                Created = response.Created
            };
        }

        /// <summary>
        /// Get stream info with subject details (for listing objects).
        /// </summary>
        /// <param name="streamName">Stream name</param>
        /// <param name="subjectsFilter">Optional subject filter pattern (e.g., "$O.bucket.M.>")</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Stream info with subjects populated</returns>
        public async Task<StreamInfo> GetStreamInfoWithSubjectsAsync(string streamName, string subjectsFilter = null, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(streamName)) throw new ArgumentNullException(nameof(streamName));

            var subject = $"{_prefix}.STREAM.INFO.{streamName}";
            var request = new StreamInfoRequest { SubjectsFilter = subjectsFilter };
            var response = await RequestAsync<StreamInfoResponse>(subject, request, ct).ConfigureAwait(false);

            if (response.Error != null)
            {
                if (response.Error.Code == 404 || response.Error.ErrCode == 10059)
                    throw new NatsJSStreamNotFoundException(streamName);
                throw new NatsJSException(response.Error.Description, response.Error.Code, response.Error.ErrCode);
            }

            return new StreamInfo
            {
                Config = response.Config,
                State = response.State,
                Created = response.Created
            };
        }

        /// <summary>
        /// Delete a stream.
        /// </summary>
        public async Task DeleteStreamAsync(string streamName, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(streamName)) throw new ArgumentNullException(nameof(streamName));

            var subject = $"{_prefix}.STREAM.DELETE.{streamName}";
            var response = await RequestAsync<StreamInfoResponse>(subject, null, ct).ConfigureAwait(false);

            if (response.Error != null && response.Error.Code != 404)
            {
                throw new NatsJSException(response.Error.Description, response.Error.Code, response.Error.ErrCode);
            }
        }

        /// <summary>
        /// Purge messages from a stream.
        /// </summary>
        public async Task<long> PurgeStreamAsync(string streamName, StreamPurgeRequest request = null, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(streamName)) throw new ArgumentNullException(nameof(streamName));

            var subject = $"{_prefix}.STREAM.PURGE.{streamName}";
            var response = await RequestAsync<StreamPurgeResponse>(subject, request, ct).ConfigureAwait(false);

            if (response.Error != null)
            {
                throw new NatsJSException(response.Error.Description, response.Error.Code, response.Error.ErrCode);
            }

            return response.Purged;
        }

        #endregion

        #region Publishing

        /// <summary>
        /// Publish a message to JetStream and wait for acknowledgment.
        /// </summary>
        /// <param name="subject">Subject to publish to</param>
        /// <param name="data">Message payload</param>
        /// <param name="headers">Optional headers</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Publish acknowledgment with stream and sequence</returns>
        public async Task<PubAck> PublishAsync(string subject, byte[] data, NatsHeaders headers = null, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(subject)) throw new ArgumentNullException(nameof(subject));

            var reply = await _connection.RequestAsync(subject, data ?? Array.Empty<byte>(), headers, _timeout, ct)
                .ConfigureAwait(false);

            if (reply.Data == null || reply.Data.Length == 0)
            {
                // Check for error in headers
                var errorHeader = reply.Headers?.GetFirst("Nats-Service-Error");
                if (!string.IsNullOrEmpty(errorHeader))
                {
                    throw new NatsJSPublishException(errorHeader, subject);
                }
                throw new NatsJSPublishException("No acknowledgment received from JetStream", subject);
            }

            var json = Encoding.UTF8.GetString(reply.Data);

            // Check for error response
            if (json.Contains("\"error\""))
            {
                var errorDict = JsonSerializer.DeserializeToDict(json);
                if (errorDict.TryGetValue("error", out var errorObj) && errorObj is string errorMsg)
                {
                    throw new NatsJSPublishException(errorMsg, subject);
                }
            }

            var ack = JsonSerializer.Deserialize<PubAck>(json);
            return ack;
        }

        /// <summary>
        /// Publish with rollup header (used by ObjectStore for metadata).
        /// </summary>
        public async Task<PubAck> PublishWithRollupAsync(string subject, byte[] data, CancellationToken ct = default)
        {
            var headers = new NatsHeaders();
            headers.Add("Nats-Rollup", "sub");
            return await PublishAsync(subject, data, headers, ct).ConfigureAwait(false);
        }

        #endregion

        #region Direct Access

        /// <summary>
        /// Get a message directly from a stream by last message on subject.
        /// </summary>
        public async Task<NatsMsg> GetLastMessageAsync(string streamName, string subject, CancellationToken ct = default)
        {
            var apiSubject = $"{_prefix}.DIRECT.GET.{streamName}";
            var request = new { last_by_subj = subject };
            var json = JsonSerializer.Serialize(request);
            var data = Encoding.UTF8.GetBytes(json);

            var reply = await _connection.RequestAsync(apiSubject, data, _headers, _timeout, ct).ConfigureAwait(false);

            // Check for 404 in headers
            var statusHeader = reply.Headers?.GetFirst("Status");
            if (statusHeader == "404")
            {
                return null;
            }

            return reply;
        }

        /// <summary>
        /// Get a message directly from a stream by sequence number.
        /// </summary>
        public async Task<NatsMsg> GetMessageBySeqAsync(string streamName, long seq, CancellationToken ct = default)
        {
            var apiSubject = $"{_prefix}.DIRECT.GET.{streamName}";
            var request = new { seq = seq };
            var json = JsonSerializer.Serialize(request);
            var data = Encoding.UTF8.GetBytes(json);

            var reply = await _connection.RequestAsync(apiSubject, data, _headers, _timeout, ct).ConfigureAwait(false);

            var statusHeader = reply.Headers?.GetFirst("Status");
            if (statusHeader == "404")
            {
                return null;
            }

            return reply;
        }

        /// <summary>
        /// Get next message on subject after a given sequence (inclusive start).
        /// </summary>
        public async Task<NatsMsg> GetNextMessageAsync(string streamName, string subject, long startSeq, CancellationToken ct = default)
        {
            var apiSubject = $"{_prefix}.DIRECT.GET.{streamName}";
            var request = new { next_by_subj = subject, seq = startSeq };
            var json = JsonSerializer.Serialize(request);
            var data = Encoding.UTF8.GetBytes(json);

            var reply = await _connection.RequestAsync(apiSubject, data, _headers, _timeout, ct).ConfigureAwait(false);

            var statusHeader = reply.Headers?.GetFirst("Status");
            if (statusHeader == "404")
            {
                return null;
            }

            return reply;
        }

        #endregion

        #region Internal

        private async Task<T> RequestAsync<T>(string subject, object payload, CancellationToken ct) where T : class, new()
        {
            var json = payload != null ? JsonSerializer.Serialize(payload) : "{}";
            var data = Encoding.UTF8.GetBytes(json);

            var reply = await _connection.RequestAsync(subject, data, _headers, _timeout, ct).ConfigureAwait(false);

            if (reply.Data == null || reply.Data.Length == 0)
            {
                return new T();
            }

            var responseJson = Encoding.UTF8.GetString(reply.Data);
            return JsonSerializer.Deserialize<T>(responseJson);
        }

        #endregion
    }
}
