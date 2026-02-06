using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NatsWebSocket.JetStream;
using NatsWebSocket.JetStream.Models;
using NatsWebSocket.ObjectStore.Models;

namespace NatsWebSocket.ObjectStore
{
    /// <summary>
    /// Object Store context providing access to NATS JetStream Object Store.
    /// </summary>
    public class NatsObjContext
    {
        private readonly NatsJSContext _js;

        // Valid bucket name pattern: alphanumeric, dash, underscore
        private static readonly Regex BucketNamePattern = new Regex(@"^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

        /// <summary>
        /// Creates a new Object Store context.
        /// </summary>
        public NatsObjContext(NatsJSContext js)
        {
            _js = js ?? throw new ArgumentNullException(nameof(js));
        }

        /// <summary>
        /// Create a new object store bucket.
        /// </summary>
        public async Task<NatsObjStore> CreateObjectStoreAsync(ObjectStoreConfig config, CancellationToken ct = default)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            ValidateBucketName(config.Bucket);

            var streamName = GetStreamName(config.Bucket);
            var streamConfig = new StreamConfig
            {
                Name = streamName,
                Description = config.Description,
                Subjects = new List<string>
                {
                    $"$O.{config.Bucket}.C.>",  // Chunks
                    $"$O.{config.Bucket}.M.>"   // Metadata
                },
                Storage = config.Storage ?? "file",
                NumReplicas = config.Replicas > 0 ? config.Replicas : 1,
                Retention = "limits",
                Discard = "new",
                AllowRollupHdrs = true,  // Required for ObjectStore
                AllowDirect = true,       // Required for ObjectStore
                MaxMsgsPerSubject = 1,    // Only keep latest metadata per object
                Compression = config.Compression ? "s2" : "none"
            };

            if (config.MaxAge.HasValue)
            {
                // Convert to nanoseconds: 1 tick = 100 nanoseconds
                streamConfig.MaxAge = config.MaxAge.Value.Ticks * 100;
            }

            if (config.MaxBytes.HasValue)
            {
                streamConfig.MaxBytes = config.MaxBytes.Value;
            }

            await _js.CreateStreamAsync(streamConfig, ct).ConfigureAwait(false);

            return new NatsObjStore(_js, config.Bucket, streamName);
        }

        /// <summary>
        /// Get an existing object store bucket.
        /// </summary>
        public async Task<NatsObjStore> GetObjectStoreAsync(string bucket, CancellationToken ct = default)
        {
            ValidateBucketName(bucket);

            var streamName = GetStreamName(bucket);

            // Verify the stream exists
            try
            {
                await _js.GetStreamAsync(streamName, ct).ConfigureAwait(false);
            }
            catch (NatsJSStreamNotFoundException)
            {
                throw new NatsJSException($"Object store bucket '{bucket}' not found", 404);
            }

            return new NatsObjStore(_js, bucket, streamName);
        }

        /// <summary>
        /// Create object store if it doesn't exist, or get existing.
        /// </summary>
        public async Task<NatsObjStore> GetOrCreateObjectStoreAsync(ObjectStoreConfig config, CancellationToken ct = default)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            ValidateBucketName(config.Bucket);

            try
            {
                return await GetObjectStoreAsync(config.Bucket, ct).ConfigureAwait(false);
            }
            catch (NatsJSException ex) when (ex.Code == 404)
            {
                return await CreateObjectStoreAsync(config, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Delete an object store bucket and all its contents.
        /// </summary>
        public async Task DeleteObjectStoreAsync(string bucket, CancellationToken ct = default)
        {
            ValidateBucketName(bucket);
            var streamName = GetStreamName(bucket);
            await _js.DeleteStreamAsync(streamName, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Get the backing stream name for a bucket.
        /// </summary>
        public static string GetStreamName(string bucket)
        {
            return $"OBJ_{bucket}";
        }

        private static void ValidateBucketName(string bucket)
        {
            if (string.IsNullOrEmpty(bucket))
                throw new ArgumentException("Bucket name is required", nameof(bucket));

            if (bucket.StartsWith(".") || bucket.EndsWith("."))
                throw new ArgumentException("Bucket name cannot start or end with a period", nameof(bucket));

            if (!BucketNamePattern.IsMatch(bucket))
                throw new ArgumentException(
                    "Bucket name can only contain alphanumeric characters, dashes, and underscores",
                    nameof(bucket));
        }
    }
}
