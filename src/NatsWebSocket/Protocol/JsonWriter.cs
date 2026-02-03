using System.Collections.Generic;
using System.Text;

namespace NatsWebSocket.Protocol
{
    /// <summary>
    /// Minimal JSON writer for building flat JSON objects without any external dependency.
    /// Used exclusively for the CONNECT command payload.
    /// </summary>
    internal static class JsonWriter
    {
        public static string WriteObject(IEnumerable<KeyValuePair<string, object>> fields)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            var first = true;

            foreach (var kvp in fields)
            {
                if (kvp.Value == null)
                    continue;

                if (!first)
                    sb.Append(',');
                first = false;

                sb.Append('"');
                EscapeString(sb, kvp.Key);
                sb.Append("\":");

                WriteValue(sb, kvp.Value);
            }

            sb.Append('}');
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value)
        {
            if (value is string s)
            {
                sb.Append('"');
                EscapeString(sb, s);
                sb.Append('"');
            }
            else if (value is bool b)
            {
                sb.Append(b ? "true" : "false");
            }
            else if (value is int i)
            {
                sb.Append(i.ToString());
            }
            else if (value is long l)
            {
                sb.Append(l.ToString());
            }
            else
            {
                sb.Append('"');
                EscapeString(sb, value.ToString());
                sb.Append('"');
            }
        }

        private static void EscapeString(StringBuilder sb, string s)
        {
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
        }
    }
}
