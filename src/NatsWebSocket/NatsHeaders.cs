using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace NatsWebSocket
{
    /// <summary>
    /// Collection of NATS message headers. Supports multiple values per key.
    /// Wire format: "NATS/1.0[ status description]\r\nkey: value\r\n...\r\n\r\n"
    /// </summary>
    public sealed class NatsHeaders : IEnumerable<KeyValuePair<string, List<string>>>
    {
        private readonly Dictionary<string, List<string>> _headers =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Optional NATS status code from the header status line (e.g. 503).
        /// </summary>
        public int? StatusCode { get; internal set; }

        /// <summary>
        /// Optional NATS status description from the header status line.
        /// </summary>
        public string StatusDescription { get; internal set; }

        public void Add(string key, string value)
        {
            if (!_headers.TryGetValue(key, out var list))
            {
                list = new List<string>();
                _headers[key] = list;
            }
            list.Add(value);
        }

        public string GetFirst(string key)
        {
            if (_headers.TryGetValue(key, out var list) && list.Count > 0)
                return list[0];
            return null;
        }

        public IReadOnlyList<string> GetAll(string key)
        {
            if (_headers.TryGetValue(key, out var list))
                return list;
            return Array.Empty<string>();
        }

        public bool ContainsKey(string key)
        {
            return _headers.ContainsKey(key);
        }

        public int Count => _headers.Count;

        /// <summary>
        /// Serialize headers to NATS wire format bytes.
        /// </summary>
        public byte[] ToWireBytes()
        {
            var sb = new StringBuilder();
            sb.Append("NATS/1.0\r\n");
            foreach (var kvp in _headers)
            {
                foreach (var value in kvp.Value)
                {
                    sb.Append(kvp.Key);
                    sb.Append(": ");
                    sb.Append(value);
                    sb.Append("\r\n");
                }
            }
            sb.Append("\r\n");
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        /// <summary>
        /// Parse headers from NATS wire format bytes.
        /// </summary>
        public static NatsHeaders FromWireBytes(byte[] data, int offset, int length)
        {
            var headers = new NatsHeaders();
            var str = Encoding.UTF8.GetString(data, offset, length);
            var lines = str.Split(new[] { "\r\n" }, StringSplitOptions.None);

            // First line: "NATS/1.0" or "NATS/1.0 503 No Responders"
            if (lines.Length > 0 && lines[0].StartsWith("NATS/1.0"))
            {
                var statusPart = lines[0].Length > 8 ? lines[0].Substring(8).Trim() : "";
                if (!string.IsNullOrEmpty(statusPart))
                {
                    var spaceIdx = statusPart.IndexOf(' ');
                    if (spaceIdx > 0)
                    {
                        if (int.TryParse(statusPart.Substring(0, spaceIdx), out var code))
                        {
                            headers.StatusCode = code;
                            headers.StatusDescription = statusPart.Substring(spaceIdx + 1).Trim();
                        }
                    }
                    else if (int.TryParse(statusPart, out var code))
                    {
                        headers.StatusCode = code;
                    }
                }
            }

            for (int i = 1; i < lines.Length; i++)
            {
                var colonPos = lines[i].IndexOf(':');
                if (colonPos > 0)
                {
                    var key = lines[i].Substring(0, colonPos).Trim();
                    var value = lines[i].Substring(colonPos + 1).Trim();
                    headers.Add(key, value);
                }
            }

            return headers;
        }

        public IEnumerator<KeyValuePair<string, List<string>>> GetEnumerator() => _headers.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
