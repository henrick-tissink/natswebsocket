using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace NatsWebSocket.JetStream.Internal
{
    /// <summary>
    /// Lightweight JSON serializer for JetStream API messages.
    /// Handles snake_case naming convention used by NATS.
    /// </summary>
    internal static class JsonSerializer
    {
        #region Serialization

        public static string Serialize(object obj)
        {
            if (obj == null) return "null";
            var sb = new StringBuilder();
            SerializeValue(obj, sb);
            return sb.ToString();
        }

        private static void SerializeValue(object value, StringBuilder sb)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            var type = value.GetType();

            if (value is string s)
            {
                sb.Append('"').Append(EscapeString(s)).Append('"');
            }
            else if (value is bool b)
            {
                sb.Append(b ? "true" : "false");
            }
            else if (value is int || value is long || value is short || value is byte ||
                     value is uint || value is ulong || value is ushort || value is sbyte)
            {
                sb.Append(value.ToString());
            }
            else if (value is double d)
            {
                sb.Append(d.ToString(CultureInfo.InvariantCulture));
            }
            else if (value is float f)
            {
                sb.Append(f.ToString(CultureInfo.InvariantCulture));
            }
            else if (value is decimal m)
            {
                sb.Append(m.ToString(CultureInfo.InvariantCulture));
            }
            else if (value is DateTime dt)
            {
                sb.Append('"').Append(dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")).Append('"');
            }
            else if (value is DateTimeOffset dto)
            {
                sb.Append('"').Append(dto.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")).Append('"');
            }
            else if (value is IList list)
            {
                SerializeList(list, sb);
            }
            else if (value is IDictionary dict)
            {
                SerializeDictionary(dict, sb);
            }
            else if (type.IsClass || type.IsValueType && !type.IsPrimitive)
            {
                SerializeObject(value, type, sb);
            }
            else
            {
                sb.Append('"').Append(EscapeString(value.ToString())).Append('"');
            }
        }

        private static void SerializeList(IList list, StringBuilder sb)
        {
            sb.Append('[');
            var first = true;
            foreach (var item in list)
            {
                if (!first) sb.Append(',');
                first = false;
                SerializeValue(item, sb);
            }
            sb.Append(']');
        }

        private static void SerializeDictionary(IDictionary dict, StringBuilder sb)
        {
            sb.Append('{');
            var first = true;
            foreach (DictionaryEntry entry in dict)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(EscapeString(entry.Key.ToString())).Append("\":");
                SerializeValue(entry.Value, sb);
            }
            sb.Append('}');
        }

        private static void SerializeObject(object obj, Type type, StringBuilder sb)
        {
            sb.Append('{');
            var first = true;

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead) continue;

                var value = prop.GetValue(obj, null);

                // Skip nulls, empty strings, -1 values (defaults), empty lists
                if (value == null) continue;
                if (value is string str && string.IsNullOrEmpty(str)) continue;
                if (value is int i && i == -1) continue;
                if (value is long l && l == -1) continue;
                if (value is IList list && list.Count == 0) continue;

                // Skip false booleans unless explicitly needed
                // (We'll include them for object store config)

                if (!first) sb.Append(',');
                first = false;

                var name = ToSnakeCase(prop.Name);
                sb.Append('"').Append(name).Append("\":");
                SerializeValue(value, sb);
            }

            sb.Append('}');
        }

        private static string EscapeString(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.AppendFormat("\\u{0:x4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        internal static string ToSnakeCase(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            var sb = new StringBuilder();
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (char.IsUpper(c))
                {
                    if (i > 0) sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        #endregion

        #region Deserialization

        public static T Deserialize<T>(string json) where T : class, new()
        {
            if (string.IsNullOrEmpty(json)) return new T();
            var index = 0;
            var dict = ParseObject(json, ref index);
            return MapToObject<T>(dict);
        }

        public static Dictionary<string, object> DeserializeToDict(string json)
        {
            if (string.IsNullOrEmpty(json)) return new Dictionary<string, object>();
            var index = 0;
            return ParseObject(json, ref index);
        }

        private static Dictionary<string, object> ParseObject(string json, ref int index)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            SkipWhitespace(json, ref index);
            if (index >= json.Length || json[index] != '{') return result;
            index++;

            while (index < json.Length)
            {
                SkipWhitespace(json, ref index);
                if (index >= json.Length || json[index] == '}')
                {
                    index++;
                    break;
                }

                var key = ParseString(json, ref index);
                if (key == null) break;

                SkipWhitespace(json, ref index);
                if (index >= json.Length || json[index] != ':') break;
                index++;

                SkipWhitespace(json, ref index);
                var value = ParseValue(json, ref index);
                result[key] = value;

                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',')
                    index++;
            }

            return result;
        }

        private static object ParseValue(string json, ref int index)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length) return null;

            var c = json[index];

            if (c == '"') return ParseString(json, ref index);
            if (c == '{') return ParseObject(json, ref index);
            if (c == '[') return ParseArray(json, ref index);
            if (c == 't' || c == 'f') return ParseBool(json, ref index);
            if (c == 'n') return ParseNull(json, ref index);
            if (c == '-' || char.IsDigit(c)) return ParseNumber(json, ref index);

            return null;
        }

        private static string ParseString(string json, ref int index)
        {
            if (index >= json.Length || json[index] != '"') return null;
            index++;

            var sb = new StringBuilder();
            while (index < json.Length)
            {
                var c = json[index++];
                if (c == '"') break;
                if (c == '\\' && index < json.Length)
                {
                    c = json[index++];
                    switch (c)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (index + 4 <= json.Length)
                            {
                                var hex = json.Substring(index, 4);
                                if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                    sb.Append((char)code);
                                index += 4;
                            }
                            break;
                        default: sb.Append(c); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        private static List<object> ParseArray(string json, ref int index)
        {
            var result = new List<object>();
            if (index >= json.Length || json[index] != '[') return result;
            index++;

            while (index < json.Length)
            {
                SkipWhitespace(json, ref index);
                if (index >= json.Length || json[index] == ']')
                {
                    index++;
                    break;
                }

                var value = ParseValue(json, ref index);
                result.Add(value);

                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',')
                    index++;
            }

            return result;
        }

        private static bool ParseBool(string json, ref int index)
        {
            if (index + 4 <= json.Length && json.Substring(index, 4) == "true")
            {
                index += 4;
                return true;
            }
            if (index + 5 <= json.Length && json.Substring(index, 5) == "false")
            {
                index += 5;
                return false;
            }
            return false;
        }

        private static object ParseNull(string json, ref int index)
        {
            if (index + 4 <= json.Length && json.Substring(index, 4) == "null")
                index += 4;
            return null;
        }

        private static object ParseNumber(string json, ref int index)
        {
            var start = index;
            if (json[index] == '-') index++;
            while (index < json.Length && (char.IsDigit(json[index]) || json[index] == '.' ||
                   json[index] == 'e' || json[index] == 'E' || json[index] == '+' || json[index] == '-'))
                index++;

            var numStr = json.Substring(start, index - start);
            if (numStr.Contains(".") || numStr.Contains("e") || numStr.Contains("E"))
            {
                if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    return d;
            }
            else
            {
                if (long.TryParse(numStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                    return l;
            }
            return 0;
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index]))
                index++;
        }

        private static T MapToObject<T>(Dictionary<string, object> dict) where T : class, new()
        {
            var obj = new T();
            MapDictToObject(dict, obj, typeof(T));
            return obj;
        }

        private static void MapDictToObject(Dictionary<string, object> dict, object obj, Type type)
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanWrite) continue;

                // Try snake_case and PascalCase
                var snakeKey = ToSnakeCase(prop.Name);
                if (!dict.TryGetValue(snakeKey, out var value) && !dict.TryGetValue(prop.Name, out value))
                    continue;

                if (value == null) continue;

                try
                {
                    SetPropertyValue(prop, obj, value);
                }
                catch
                {
                    // Skip properties that can't be mapped
                }
            }
        }

        private static void SetPropertyValue(PropertyInfo prop, object obj, object value)
        {
            var propType = prop.PropertyType;

            if (propType == typeof(string))
            {
                prop.SetValue(obj, value?.ToString(), null);
            }
            else if (propType == typeof(int))
            {
                prop.SetValue(obj, Convert.ToInt32(value), null);
            }
            else if (propType == typeof(long))
            {
                prop.SetValue(obj, Convert.ToInt64(value), null);
            }
            else if (propType == typeof(bool))
            {
                prop.SetValue(obj, Convert.ToBoolean(value), null);
            }
            else if (propType == typeof(double))
            {
                prop.SetValue(obj, Convert.ToDouble(value), null);
            }
            else if (propType == typeof(DateTime))
            {
                if (DateTime.TryParse(value?.ToString(), out var dt))
                    prop.SetValue(obj, dt, null);
            }
            else if (propType.IsGenericType && propType.GetGenericTypeDefinition() == typeof(List<>))
            {
                if (value is List<object> sourceList)
                {
                    var elementType = propType.GetGenericArguments()[0];
                    var targetList = (IList)Activator.CreateInstance(propType);

                    foreach (var item in sourceList)
                    {
                        if (elementType == typeof(string))
                            targetList.Add(item?.ToString());
                        else if (item is Dictionary<string, object> itemDict)
                        {
                            var nestedObj = Activator.CreateInstance(elementType);
                            MapDictToObject(itemDict, nestedObj, elementType);
                            targetList.Add(nestedObj);
                        }
                    }

                    prop.SetValue(obj, targetList, null);
                }
            }
            else if (propType.IsGenericType && propType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                if (value is Dictionary<string, object> sourceDict)
                {
                    var keyType = propType.GetGenericArguments()[0];
                    var valueType = propType.GetGenericArguments()[1];

                    // Only handle string keys
                    if (keyType != typeof(string)) return;

                    var targetDict = (IDictionary)Activator.CreateInstance(propType);

                    foreach (var kvp in sourceDict)
                    {
                        object mappedValue = null;

                        if (valueType == typeof(string))
                        {
                            mappedValue = kvp.Value?.ToString();
                        }
                        else if (valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(List<>))
                        {
                            // Handle Dictionary<string, List<string>> like Headers
                            var listElementType = valueType.GetGenericArguments()[0];
                            if (listElementType == typeof(string) && kvp.Value is List<object> sourceList)
                            {
                                var stringList = new List<string>();
                                foreach (var item in sourceList)
                                {
                                    stringList.Add(item?.ToString());
                                }
                                mappedValue = stringList;
                            }
                        }
                        else if (valueType.IsClass && kvp.Value is Dictionary<string, object> nestedDict)
                        {
                            var nestedObj = Activator.CreateInstance(valueType);
                            MapDictToObject(nestedDict, nestedObj, valueType);
                            mappedValue = nestedObj;
                        }

                        if (mappedValue != null)
                        {
                            targetDict[kvp.Key] = mappedValue;
                        }
                    }

                    prop.SetValue(obj, targetDict, null);
                }
            }
            else if (propType.IsClass && value is Dictionary<string, object> nestedDict)
            {
                var nestedObj = Activator.CreateInstance(propType);
                MapDictToObject(nestedDict, nestedObj, propType);
                prop.SetValue(obj, nestedObj, null);
            }
        }

        #endregion
    }
}
