using System;
using System.Collections.Generic;

namespace NatsWebSocket.ObjectStore.Models
{
    /// <summary>
    /// Configuration for creating an object store bucket.
    /// </summary>
    public class ObjectStoreConfig
    {
        /// <summary>
        /// Bucket name (alphanumeric, dash, underscore only).
        /// </summary>
        public string Bucket { get; set; }

        /// <summary>
        /// Optional description.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Maximum age of objects in the bucket (TimeSpan).
        /// </summary>
        public TimeSpan? MaxAge { get; set; }

        /// <summary>
        /// Maximum total size of the bucket in bytes.
        /// </summary>
        public long? MaxBytes { get; set; }

        /// <summary>
        /// Storage type: "file" or "memory".
        /// </summary>
        public string Storage { get; set; } = "file";

        /// <summary>
        /// Number of replicas.
        /// </summary>
        public int Replicas { get; set; } = 1;

        /// <summary>
        /// Enable S2 compression.
        /// </summary>
        public bool Compression { get; set; }

        /// <summary>
        /// Custom metadata for the bucket.
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; }
    }

    /// <summary>
    /// Metadata for an object in the store.
    /// </summary>
    public class ObjectMeta
    {
        /// <summary>
        /// Object name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Optional description.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Custom headers.
        /// </summary>
        public Dictionary<string, List<string>> Headers { get; set; }

        /// <summary>
        /// Custom metadata.
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; }

        /// <summary>
        /// Object options.
        /// </summary>
        public ObjectOptions Options { get; set; }
    }

    /// <summary>
    /// Object options.
    /// </summary>
    public class ObjectOptions
    {
        /// <summary>
        /// Maximum chunk size in bytes. Default is 128KB.
        /// </summary>
        public int MaxChunkSize { get; set; } = 128 * 1024;

        /// <summary>
        /// Link to another object (for linked objects).
        /// </summary>
        public ObjectLink Link { get; set; }
    }

    /// <summary>
    /// Link to another object.
    /// </summary>
    public class ObjectLink
    {
        public string Bucket { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// Complete object information including internal fields.
    /// </summary>
    public class ObjectInfo
    {
        /// <summary>
        /// Object name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Optional description.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Bucket name.
        /// </summary>
        public string Bucket { get; set; }

        /// <summary>
        /// Internal unique identifier (NUID).
        /// </summary>
        public string Nuid { get; set; }

        /// <summary>
        /// Total size in bytes.
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// Number of chunks.
        /// </summary>
        public int Chunks { get; set; }

        /// <summary>
        /// Content digest in format "SHA-256={base64}".
        /// </summary>
        public string Digest { get; set; }

        /// <summary>
        /// Modification time (from server timestamp).
        /// </summary>
        public DateTime Mtime { get; set; }

        /// <summary>
        /// Whether the object is deleted.
        /// </summary>
        public bool Deleted { get; set; }

        /// <summary>
        /// Custom headers.
        /// </summary>
        public Dictionary<string, List<string>> Headers { get; set; }

        /// <summary>
        /// Custom metadata.
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; }

        /// <summary>
        /// Object options.
        /// </summary>
        public ObjectOptions Options { get; set; }
    }

    /// <summary>
    /// Object store status information.
    /// </summary>
    public class ObjectStoreStatus
    {
        /// <summary>
        /// Bucket name.
        /// </summary>
        public string Bucket { get; set; }

        /// <summary>
        /// Description.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Number of objects.
        /// </summary>
        public long Objects { get; set; }

        /// <summary>
        /// Total size in bytes.
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// Storage type.
        /// </summary>
        public string Storage { get; set; }

        /// <summary>
        /// Number of replicas.
        /// </summary>
        public int Replicas { get; set; }

        /// <summary>
        /// Whether compression is enabled.
        /// </summary>
        public bool Compression { get; set; }

        /// <summary>
        /// Backing stream name.
        /// </summary>
        public string StreamName { get; set; }
    }
}
