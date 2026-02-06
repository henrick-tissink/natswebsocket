using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NatsWebSocket.JetStream;
using NatsWebSocket.JetStream.Internal;
using NatsWebSocket.JetStream.Models;
using NatsWebSocket.ObjectStore.Models;

namespace NatsWebSocket.ObjectStore
{
    /// <summary>
    /// Represents a NATS JetStream Object Store bucket.
    /// Provides methods to store, retrieve, and manage objects.
    /// </summary>
    public class NatsObjStore
    {
        private readonly NatsJSContext _js;
        private readonly string _bucket;
        private readonly string _streamName;

        /// <summary>
        /// Default chunk size: 128 KB.
        /// </summary>
        public const int DefaultChunkSize = 128 * 1024;

        /// <summary>
        /// Bucket name.
        /// </summary>
        public string Bucket => _bucket;

        /// <summary>
        /// Backing stream name.
        /// </summary>
        public string StreamName => _streamName;

        internal NatsObjStore(NatsJSContext js, string bucket, string streamName)
        {
            _js = js ?? throw new ArgumentNullException(nameof(js));
            _bucket = bucket ?? throw new ArgumentNullException(nameof(bucket));
            _streamName = streamName ?? throw new ArgumentNullException(nameof(streamName));
        }

        #region Put Operations

        /// <summary>
        /// Put an object from a file path.
        /// </summary>
        public async Task<ObjectInfo> PutAsync(string name, string filePath, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));
            if (!File.Exists(filePath))
                throw new FileNotFoundException("File not found", filePath);

            using (var stream = File.OpenRead(filePath))
            {
                var meta = new ObjectMeta { Name = name };
                return await PutAsync(meta, stream, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Put an object from a byte array.
        /// </summary>
        public async Task<ObjectInfo> PutAsync(string name, byte[] data, CancellationToken ct = default)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            using (var stream = new MemoryStream(data))
            {
                var meta = new ObjectMeta { Name = name };
                return await PutAsync(meta, stream, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Put an object from a stream with metadata.
        /// </summary>
        public async Task<ObjectInfo> PutAsync(ObjectMeta meta, Stream data, CancellationToken ct = default)
        {
            if (meta == null) throw new ArgumentNullException(nameof(meta));
            if (string.IsNullOrEmpty(meta.Name)) throw new ArgumentException("Object name is required");
            if (data == null) throw new ArgumentNullException(nameof(data));

            var chunkSize = meta.Options?.MaxChunkSize ?? DefaultChunkSize;
            var nuid = Nuid.Next();
            var chunkSubject = GetChunkSubject(nuid);

            // Note: We don't check for existing objects before uploading because:
            // 1. It requires DIRECT.GET permissions which may not be available
            // 2. The metadata rollup header will replace old metadata automatically
            // 3. Old chunks may become orphaned but will be cleaned up by stream limits

            // Read and publish chunks, computing digest
            long totalSize = 0;
            int chunkCount = 0;
            bool uploadStarted = false;

            try
            {
                using (var sha256 = SHA256.Create())
                {
                    var buffer = new byte[chunkSize];
                    int bytesRead;

                    while ((bytesRead = await data.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                    {
                        uploadStarted = true;

                        // Update hash
                        sha256.TransformBlock(buffer, 0, bytesRead, null, 0);

                        // Publish chunk
                        var chunkData = bytesRead == buffer.Length ? buffer : CopyBytes(buffer, bytesRead);
                        await _js.PublishAsync(chunkSubject, chunkData, null, ct).ConfigureAwait(false);

                        totalSize += bytesRead;
                        chunkCount++;
                    }

                    // Finalize hash
                    sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    var digest = "SHA-256=" + Convert.ToBase64String(sha256.Hash);

                    // Create and publish metadata
                    var info = new ObjectInfo
                    {
                        Name = meta.Name,
                        Description = meta.Description,
                        Bucket = _bucket,
                        Nuid = nuid,
                        Size = totalSize,
                        Chunks = chunkCount,
                        Digest = digest,
                        Mtime = DateTime.UtcNow,
                        Deleted = false,
                        Headers = meta.Headers,
                        Metadata = meta.Metadata,
                        Options = meta.Options ?? new ObjectOptions { MaxChunkSize = chunkSize }
                    };

                    await PublishMetaAsync(info, ct).ConfigureAwait(false);

                    return info;
                }
            }
            catch
            {
                // Clean up any chunks we published before the failure
                if (uploadStarted)
                {
                    try
                    {
                        await DeleteChunksAsync(nuid, ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Best effort cleanup
                    }
                }
                throw;
            }
        }

        #endregion

        #region Get Operations

        /// <summary>
        /// Get object information without downloading the data.
        /// </summary>
        public async Task<ObjectInfo> GetInfoAsync(string name, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            var metaSubject = GetMetaSubject(name);
            var msg = await _js.GetLastMessageAsync(_streamName, metaSubject, ct).ConfigureAwait(false);

            if (msg == null || msg.Data == null || msg.Data.Length == 0)
            {
                throw new NatsObjNotFoundException(_bucket, name);
            }

            var json = Encoding.UTF8.GetString(msg.Data);
            var info = JsonSerializer.Deserialize<ObjectInfo>(json);

            if (info.Deleted)
            {
                throw new NatsObjNotFoundException(_bucket, name);
            }

            return info;
        }

        /// <summary>
        /// Get object data as a byte array.
        /// </summary>
        public async Task<byte[]> GetBytesAsync(string name, CancellationToken ct = default)
        {
            var info = await GetInfoAsync(name, ct).ConfigureAwait(false);

            using (var ms = new MemoryStream((int)info.Size))
            {
                await GetAsync(info, ms, ct).ConfigureAwait(false);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Get object data and write to a stream.
        /// </summary>
        public async Task GetAsync(string name, Stream destination, CancellationToken ct = default)
        {
            var info = await GetInfoAsync(name, ct).ConfigureAwait(false);
            await GetAsync(info, destination, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Get object data using existing info.
        /// </summary>
        public async Task GetAsync(ObjectInfo info, Stream destination, CancellationToken ct = default)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));
            if (destination == null) throw new ArgumentNullException(nameof(destination));

            if (info.Chunks == 0)
            {
                return; // Empty object
            }

            var chunkSubject = GetChunkSubject(info.Nuid);
            long nextSeq = 1;

            using (var sha256 = SHA256.Create())
            {
                for (int i = 0; i < info.Chunks; i++)
                {
                    // Get next chunk on subject starting from nextSeq
                    var msg = await _js.GetNextMessageAsync(_streamName, chunkSubject, nextSeq, ct).ConfigureAwait(false);
                    if (msg == null)
                    {
                        throw new NatsObjException($"Missing chunk {i + 1} of {info.Chunks} for object '{info.Name}'");
                    }

                    // Extract sequence from Nats-Sequence header for next iteration
                    var seqHeader = msg.Headers?.GetFirst("Nats-Sequence");
                    if (!string.IsNullOrEmpty(seqHeader) && long.TryParse(seqHeader, out var seq))
                    {
                        nextSeq = seq + 1;
                    }
                    else
                    {
                        // Fallback: increment by 1 (may skip messages on other subjects)
                        nextSeq++;
                    }

                    if (msg.Data != null && msg.Data.Length > 0)
                    {
                        // Update hash
                        sha256.TransformBlock(msg.Data, 0, msg.Data.Length, null, 0);

                        // Write to destination
                        await destination.WriteAsync(msg.Data, 0, msg.Data.Length, ct).ConfigureAwait(false);
                    }
                }

                // Verify digest if present
                if (!string.IsNullOrEmpty(info.Digest))
                {
                    sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    var computedDigest = "SHA-256=" + Convert.ToBase64String(sha256.Hash);
                    if (!string.Equals(info.Digest, computedDigest, StringComparison.Ordinal))
                    {
                        throw new NatsObjException($"Digest mismatch for object '{info.Name}'. Expected: {info.Digest}, Got: {computedDigest}");
                    }
                }
            }
        }

        #endregion

        #region Delete Operations

        /// <summary>
        /// Delete an object.
        /// </summary>
        public async Task DeleteAsync(string name, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            // Get current info to find the NUID
            ObjectInfo info;
            try
            {
                info = await GetInfoAsync(name, ct).ConfigureAwait(false);
            }
            catch (NatsObjNotFoundException)
            {
                return; // Already deleted
            }

            // Mark as deleted in metadata
            info.Deleted = true;
            info.Size = 0;
            info.Chunks = 0;
            info.Digest = null;

            await PublishMetaAsync(info, ct).ConfigureAwait(false);

            // Purge chunk data
            await DeleteChunksAsync(info.Nuid, ct).ConfigureAwait(false);
        }

        private async Task DeleteChunksAsync(string nuid, CancellationToken ct)
        {
            try
            {
                var filter = $"$O.{_bucket}.C.{nuid}";
                await _js.PurgeStreamAsync(_streamName, new StreamPurgeRequest { Filter = filter }, ct)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Ignore purge errors - chunks may not exist
            }
        }

        #endregion

        #region List Operations

        /// <summary>
        /// List all objects in the bucket.
        /// </summary>
        /// <param name="includeDeleted">Whether to include deleted objects</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>List of object info</returns>
        public async Task<List<ObjectInfo>> ListAsync(bool includeDeleted = false, CancellationToken ct = default)
        {
            var results = new List<ObjectInfo>();
            var metaSubjectFilter = $"$O.{_bucket}.M.>";

            // Get stream info with subjects to find all metadata entries
            var streamInfo = await _js.GetStreamInfoWithSubjectsAsync(_streamName, metaSubjectFilter, ct)
                .ConfigureAwait(false);

            if (streamInfo.State?.Subjects == null || streamInfo.State.Subjects.Count == 0)
            {
                return results;
            }

            // For each metadata subject, fetch the object info
            foreach (var subject in streamInfo.State.Subjects.Keys)
            {
                try
                {
                    var msg = await _js.GetLastMessageAsync(_streamName, subject, ct).ConfigureAwait(false);
                    if (msg?.Data == null || msg.Data.Length == 0)
                        continue;

                    var json = Encoding.UTF8.GetString(msg.Data);
                    var info = JsonSerializer.Deserialize<ObjectInfo>(json);

                    if (info == null)
                        continue;

                    // Skip deleted unless requested
                    if (info.Deleted && !includeDeleted)
                        continue;

                    results.Add(info);
                }
                catch
                {
                    // Skip objects that fail to load
                }
            }

            return results;
        }

        /// <summary>
        /// Get the names of all objects in the bucket.
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        /// <returns>List of object names</returns>
        public async Task<List<string>> ListNamesAsync(CancellationToken ct = default)
        {
            var infos = await ListAsync(false, ct).ConfigureAwait(false);
            var names = new List<string>(infos.Count);
            foreach (var info in infos)
            {
                names.Add(info.Name);
            }
            return names;
        }

        /// <summary>
        /// Check if an object exists.
        /// </summary>
        public async Task<bool> ExistsAsync(string name, CancellationToken ct = default)
        {
            try
            {
                var info = await GetInfoAsync(name, ct).ConfigureAwait(false);
                return info != null && !info.Deleted;
            }
            catch (NatsObjNotFoundException)
            {
                return false;
            }
        }

        #endregion

        #region Helpers

        private string GetChunkSubject(string nuid)
        {
            return $"$O.{_bucket}.C.{nuid}";
        }

        private string GetMetaSubject(string name)
        {
            var encoded = Base64Url.Encode(name);
            return $"$O.{_bucket}.M.{encoded}";
        }

        private async Task PublishMetaAsync(ObjectInfo info, CancellationToken ct)
        {
            var metaSubject = GetMetaSubject(info.Name);
            var json = SerializeObjectInfo(info);
            var data = Encoding.UTF8.GetBytes(json);

            await _js.PublishWithRollupAsync(metaSubject, data, ct).ConfigureAwait(false);
        }

        private static string SerializeObjectInfo(ObjectInfo info)
        {
            // Manual serialization to match NATS format exactly
            var sb = new StringBuilder();
            sb.Append('{');

            sb.AppendFormat("\"name\":\"{0}\"", EscapeJson(info.Name));
            sb.AppendFormat(",\"bucket\":\"{0}\"", EscapeJson(info.Bucket));
            sb.AppendFormat(",\"nuid\":\"{0}\"", EscapeJson(info.Nuid));
            sb.AppendFormat(",\"size\":{0}", info.Size);
            sb.AppendFormat(",\"chunks\":{0}", info.Chunks);

            if (!string.IsNullOrEmpty(info.Digest))
                sb.AppendFormat(",\"digest\":\"{0}\"", EscapeJson(info.Digest));

            if (!string.IsNullOrEmpty(info.Description))
                sb.AppendFormat(",\"description\":\"{0}\"", EscapeJson(info.Description));

            sb.AppendFormat(",\"deleted\":{0}", info.Deleted ? "true" : "false");

            if (info.Options != null)
            {
                sb.Append(",\"options\":{");
                sb.AppendFormat("\"max_chunk_size\":{0}", info.Options.MaxChunkSize);
                sb.Append('}');
            }

            // Serialize headers if present
            if (info.Headers != null && info.Headers.Count > 0)
            {
                sb.Append(",\"headers\":{");
                bool firstHeader = true;
                foreach (var kvp in info.Headers)
                {
                    if (!firstHeader) sb.Append(',');
                    firstHeader = false;
                    sb.AppendFormat("\"{0}\":[", EscapeJson(kvp.Key));
                    for (int i = 0; i < kvp.Value.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.AppendFormat("\"{0}\"", EscapeJson(kvp.Value[i]));
                    }
                    sb.Append(']');
                }
                sb.Append('}');
            }

            // Serialize metadata if present
            if (info.Metadata != null && info.Metadata.Count > 0)
            {
                sb.Append(",\"metadata\":{");
                bool firstMeta = true;
                foreach (var kvp in info.Metadata)
                {
                    if (!firstMeta) sb.Append(',');
                    firstMeta = false;
                    sb.AppendFormat("\"{0}\":\"{1}\"", EscapeJson(kvp.Key), EscapeJson(kvp.Value));
                }
                sb.Append('}');
            }

            // Note: mtime is set by server from message timestamp, not included in payload

            sb.Append('}');
            return sb.ToString();
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }

        private static byte[] CopyBytes(byte[] source, int length)
        {
            var result = new byte[length];
            Array.Copy(source, result, length);
            return result;
        }

        #endregion
    }
}
