using System;
using System.Collections.Generic;

namespace NatsWebSocket.Protocol
{
    /// <summary>
    /// Minimal JSON reader for parsing NATS INFO payloads. Handles flat JSON objects
    /// with string, number, and boolean values. No external dependency.
    /// </summary>
    internal static class JsonReader
    {
        public static Dictionary<string, object> Parse(string json)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var i = 0;

            SkipWhitespace(json, ref i);
            if (i >= json.Length || json[i] != '{')
                return result;
            i++; // skip {

            while (i < json.Length)
            {
                SkipWhitespace(json, ref i);
                if (i >= json.Length || json[i] == '}')
                    break;

                if (json[i] == ',')
                {
                    i++;
                    continue;
                }

                // Key
                var key = ReadString(json, ref i);
                if (key == null) break;

                SkipWhitespace(json, ref i);
                if (i >= json.Length || json[i] != ':') break;
                i++; // skip :

                SkipWhitespace(json, ref i);
                if (i >= json.Length) break;

                // Value
                var value = ReadValue(json, ref i);
                if (key != null)
                    result[key] = value;
            }

            return result;
        }

        public static ServerInfo ParseServerInfo(string json)
        {
            var fields = Parse(json);
            var info = new ServerInfo();

            if (fields.TryGetValue("server_id", out var sid)) info.ServerId = sid as string;
            if (fields.TryGetValue("server_name", out var sname)) info.ServerName = sname as string;
            if (fields.TryGetValue("version", out var ver)) info.Version = ver as string;
            if (fields.TryGetValue("host", out var host)) info.Host = host as string;
            if (fields.TryGetValue("port", out var port)) info.Port = ToInt(port);
            if (fields.TryGetValue("headers", out var hdr)) info.HeadersSupported = ToBool(hdr);
            if (fields.TryGetValue("auth_required", out var auth)) info.AuthRequired = ToBool(auth);
            if (fields.TryGetValue("max_payload", out var mp)) info.MaxPayload = ToInt(mp);
            if (fields.TryGetValue("proto", out var proto)) info.ProtocolVersion = ToInt(proto);
            if (fields.TryGetValue("nonce", out var nonce)) info.Nonce = nonce as string;

            return info;
        }

        private static object ReadValue(string json, ref int i)
        {
            if (i >= json.Length) return null;

            var c = json[i];
            if (c == '"')
                return ReadString(json, ref i);
            if (c == 't' || c == 'f')
                return ReadBool(json, ref i);
            if (c == 'n')
                return ReadNull(json, ref i);
            if (c == '-' || (c >= '0' && c <= '9'))
                return ReadNumber(json, ref i);
            if (c == '[')
                return SkipArray(json, ref i);
            if (c == '{')
                return SkipObject(json, ref i);

            i++;
            return null;
        }

        private static string ReadString(string json, ref int i)
        {
            if (i >= json.Length || json[i] != '"') return null;
            i++; // skip opening "

            var start = i;
            var hasEscape = false;

            while (i < json.Length)
            {
                if (json[i] == '\\')
                {
                    hasEscape = true;
                    i += 2;
                    continue;
                }
                if (json[i] == '"')
                {
                    var raw = json.Substring(start, i - start);
                    i++; // skip closing "
                    return hasEscape ? Unescape(raw) : raw;
                }
                i++;
            }
            return null;
        }

        private static string Unescape(string s)
        {
            var chars = new char[s.Length];
            var wi = 0;
            for (var ri = 0; ri < s.Length; ri++)
            {
                if (s[ri] == '\\' && ri + 1 < s.Length)
                {
                    ri++;
                    switch (s[ri])
                    {
                        case '"': chars[wi++] = '"'; break;
                        case '\\': chars[wi++] = '\\'; break;
                        case '/': chars[wi++] = '/'; break;
                        case 'n': chars[wi++] = '\n'; break;
                        case 'r': chars[wi++] = '\r'; break;
                        case 't': chars[wi++] = '\t'; break;
                        case 'u':
                            if (ri + 4 < s.Length)
                            {
                                var hex = s.Substring(ri + 1, 4);
                                chars[wi++] = (char)Convert.ToInt32(hex, 16);
                                ri += 4;
                            }
                            break;
                        default: chars[wi++] = s[ri]; break;
                    }
                }
                else
                {
                    chars[wi++] = s[ri];
                }
            }
            return new string(chars, 0, wi);
        }

        private static object ReadBool(string json, ref int i)
        {
            if (json.Length - i >= 4 && json.Substring(i, 4) == "true")
            {
                i += 4;
                return true;
            }
            if (json.Length - i >= 5 && json.Substring(i, 5) == "false")
            {
                i += 5;
                return false;
            }
            i++;
            return false;
        }

        private static object ReadNull(string json, ref int i)
        {
            if (json.Length - i >= 4 && json.Substring(i, 4) == "null")
            {
                i += 4;
                return null;
            }
            i++;
            return null;
        }

        private static object ReadNumber(string json, ref int i)
        {
            var start = i;
            if (json[i] == '-') i++;
            while (i < json.Length && json[i] >= '0' && json[i] <= '9') i++;

            var isFloat = false;
            if (i < json.Length && json[i] == '.')
            {
                isFloat = true;
                i++;
                while (i < json.Length && json[i] >= '0' && json[i] <= '9') i++;
            }
            if (i < json.Length && (json[i] == 'e' || json[i] == 'E'))
            {
                isFloat = true;
                i++;
                if (i < json.Length && (json[i] == '+' || json[i] == '-')) i++;
                while (i < json.Length && json[i] >= '0' && json[i] <= '9') i++;
            }

            var numStr = json.Substring(start, i - start);
            if (isFloat)
            {
                double.TryParse(numStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var d);
                return d;
            }
            if (long.TryParse(numStr, out var l))
                return l;
            return 0L;
        }

        private static object SkipArray(string json, ref int i)
        {
            // Skip nested arrays (e.g. connect_urls) — we don't need them
            var depth = 0;
            while (i < json.Length)
            {
                if (json[i] == '[') depth++;
                else if (json[i] == ']') { depth--; if (depth == 0) { i++; return null; } }
                else if (json[i] == '"') ReadString(json, ref i);
                else i++;
            }
            return null;
        }

        private static object SkipObject(string json, ref int i)
        {
            var depth = 0;
            while (i < json.Length)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}') { depth--; if (depth == 0) { i++; return null; } }
                else if (json[i] == '"') ReadString(json, ref i);
                else i++;
            }
            return null;
        }

        private static void SkipWhitespace(string json, ref int i)
        {
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t' || json[i] == '\r' || json[i] == '\n'))
                i++;
        }

        private static int ToInt(object value)
        {
            if (value is long l) return (int)l;
            if (value is int i) return i;
            if (value is double d) return (int)d;
            return 0;
        }

        private static bool ToBool(object value)
        {
            if (value is bool b) return b;
            return false;
        }
    }
}
