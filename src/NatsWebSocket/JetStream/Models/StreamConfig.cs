using System;
using System.Collections.Generic;

namespace NatsWebSocket.JetStream.Models
{
    /// <summary>
    /// JetStream stream configuration.
    /// </summary>
    public class StreamConfig
    {
        /// <summary>
        /// Stream name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Subjects the stream listens on.
        /// </summary>
        public List<string> Subjects { get; set; } = new List<string>();

        /// <summary>
        /// Storage type: "file" or "memory".
        /// </summary>
        public string Storage { get; set; } = "file";

        /// <summary>
        /// Number of replicas.
        /// </summary>
        public int NumReplicas { get; set; } = 1;

        /// <summary>
        /// Retention policy: "limits", "interest", or "workqueue".
        /// </summary>
        public string Retention { get; set; } = "limits";

        /// <summary>
        /// Discard policy: "old" or "new".
        /// </summary>
        public string Discard { get; set; } = "old";

        /// <summary>
        /// Maximum number of messages.
        /// </summary>
        public long MaxMsgs { get; set; } = -1;

        /// <summary>
        /// Maximum bytes.
        /// </summary>
        public long MaxBytes { get; set; } = -1;

        /// <summary>
        /// Maximum age in nanoseconds.
        /// </summary>
        public long MaxAge { get; set; } = 0;

        /// <summary>
        /// Maximum message size.
        /// </summary>
        public int MaxMsgSize { get; set; } = -1;

        /// <summary>
        /// Maximum messages per subject.
        /// </summary>
        public long MaxMsgsPerSubject { get; set; } = -1;

        /// <summary>
        /// Duplicate window in nanoseconds.
        /// </summary>
        public long DuplicateWindow { get; set; } = 120000000000; // 2 minutes

        /// <summary>
        /// Allow rollup headers (required for ObjectStore).
        /// </summary>
        public bool AllowRollupHdrs { get; set; }

        /// <summary>
        /// Allow direct access (required for ObjectStore).
        /// </summary>
        public bool AllowDirect { get; set; }

        /// <summary>
        /// Compression algorithm: "none" or "s2".
        /// </summary>
        public string Compression { get; set; } = "none";

        /// <summary>
        /// Optional description.
        /// </summary>
        public string Description { get; set; }
    }

    /// <summary>
    /// JetStream stream information.
    /// </summary>
    public class StreamInfo
    {
        public StreamConfig Config { get; set; }
        public StreamState State { get; set; }
        public DateTime Created { get; set; }
    }

    /// <summary>
    /// JetStream stream state.
    /// </summary>
    public class StreamState
    {
        public long Messages { get; set; }
        public long Bytes { get; set; }
        public long FirstSeq { get; set; }
        public long LastSeq { get; set; }
        public int ConsumerCount { get; set; }
        public int NumSubjects { get; set; }
        public Dictionary<string, long> Subjects { get; set; }
    }

    /// <summary>
    /// Request for stream info with options.
    /// </summary>
    public class StreamInfoRequest
    {
        /// <summary>
        /// Filter to return only subjects matching this pattern.
        /// </summary>
        public string SubjectsFilter { get; set; }

        /// <summary>
        /// Pagination offset for subjects.
        /// </summary>
        public int Offset { get; set; }
    }

    /// <summary>
    /// Response from stream creation/info API.
    /// </summary>
    public class StreamInfoResponse
    {
        public string Type { get; set; }
        public StreamConfig Config { get; set; }
        public StreamState State { get; set; }
        public DateTime Created { get; set; }
        public ApiError Error { get; set; }
    }

    /// <summary>
    /// JetStream API error.
    /// </summary>
    public class ApiError
    {
        public int Code { get; set; }
        public string Description { get; set; }
        public int ErrCode { get; set; }
    }

    /// <summary>
    /// Response from JetStream publish.
    /// </summary>
    public class PubAck
    {
        public string Stream { get; set; }
        public long Seq { get; set; }
        public string Domain { get; set; }
        public bool Duplicate { get; set; }
    }

    /// <summary>
    /// Request for stream purge.
    /// </summary>
    public class StreamPurgeRequest
    {
        /// <summary>
        /// Purge messages matching this subject filter.
        /// </summary>
        public string Filter { get; set; }

        /// <summary>
        /// Purge up to this sequence (exclusive).
        /// </summary>
        public long Seq { get; set; }

        /// <summary>
        /// Keep this many messages.
        /// </summary>
        public long Keep { get; set; }
    }

    /// <summary>
    /// Response from stream purge.
    /// </summary>
    public class StreamPurgeResponse
    {
        public string Type { get; set; }
        public bool Success { get; set; }
        public long Purged { get; set; }
        public ApiError Error { get; set; }
    }

    /// <summary>
    /// Request for direct message get.
    /// </summary>
    public class DirectGetRequest
    {
        public long Seq { get; set; }
        public string LastBySubject { get; set; }
        public string NextBySubject { get; set; }
    }
}
